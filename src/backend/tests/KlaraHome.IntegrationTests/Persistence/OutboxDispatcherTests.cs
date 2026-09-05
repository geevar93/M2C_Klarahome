using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.SharedKernel.Time;
using KlaraHome.Testing.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KlaraHome.IntegrationTests.Persistence;

/// <summary>
/// The dispatcher end to end: real database, real rows, real handlers.
/// </summary>
/// <remarks>
/// It runs as a hosted service in a loop, which is exactly the shape of code whose bugs surface in
/// production rather than in review — so it is driven here through its own <c>StartAsync</c> and
/// observed through the table it is supposed to drain.
/// </remarks>
[Collection(PostgresDatabase.CollectionName)]
public sealed class OutboxDispatcherTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly Guid _tenant = Guid.CreateVersion7();

    public async ValueTask InitializeAsync()
    {
        Assert.SkipWhen(postgres.SkipReason is not null, postgres.SkipReason ?? string.Empty);

        await using var context = postgres.CreateContext(_tenant);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task A_published_event_reaches_its_handler_and_the_row_is_marked_processed()
    {
        var published = new KettleBoiled(Guid.CreateVersion7(), 900);
        var handler = new RecordingHandler();

        await EnqueueAsync(published);
        await RunDispatcherAsync(handler);

        Assert.Equal(published.KettleId, Assert.Single(handler.Received).KettleId);

        var message = await SingleMessageAsync();
        Assert.NotNull(message.ProcessedAt);
        Assert.Equal(1, message.Attempts);
        Assert.Null(message.Error);
    }

    [Fact]
    public async Task A_processed_message_is_not_delivered_a_second_time()
    {
        await EnqueueAsync(new KettleBoiled(Guid.CreateVersion7(), 100));

        var handler = new RecordingHandler();
        await RunDispatcherAsync(handler);
        await RunDispatcherAsync(handler, expectPoll: false);

        // The partial index and the processed_at guard are what keep the queue from replaying
        // itself every poll for the lifetime of the deployment.
        Assert.Single(handler.Received);
    }

    [Fact]
    public async Task A_failing_handler_leaves_the_message_pending_with_its_error_recorded()
    {
        await EnqueueAsync(new KettleBoiled(Guid.CreateVersion7(), 200));

        await RunDispatcherAsync(new ThrowingHandler());

        var message = await SingleMessageAsync();

        Assert.Null(message.ProcessedAt);
        Assert.Equal(1, message.Attempts);
        Assert.Contains("kettle exploded", message.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_message_that_keeps_failing_stops_being_retried()
    {
        await EnqueueAsync(new KettleBoiled(Guid.CreateVersion7(), 300));

        var handler = new ThrowingHandler();

        // Three attempts allowed; the fourth poll must find nothing to do. Without the cap a
        // poison message would be retried at the poll interval forever, filling the log and
        // starving nothing but itself.
        var persisted = new List<int>();

        for (var poll = 0; poll < 5; poll++)
        {
            await RunDispatcherAsync(handler, maxAttempts: 3, expectPoll: poll < 3);
            persisted.Add((await SingleMessageAsync()).Attempts);
        }

        Assert.Equal([1, 2, 3, 3, 3], persisted);
        Assert.Equal(3, handler.Attempts);
    }

    [Fact]
    public async Task A_message_no_handler_claims_is_marked_processed_rather_than_blocking_the_queue()
    {
        await EnqueueAsync(new KettleBoiled(Guid.CreateVersion7(), 400));

        // No handler registered at all — an event published before its consumer was written.
        await RunDispatcherAsync(handler: null);

        var message = await SingleMessageAsync();

        Assert.NotNull(message.ProcessedAt);
        Assert.Null(message.Error);
    }

    [Fact]
    public async Task Messages_are_delivered_in_the_order_the_facts_occurred()
    {
        var start = new DateTimeOffset(2026, 9, 5, 8, 0, 0, TimeSpan.Zero);

        await EnqueueAsync(
            new KettleBoiled(Guid.CreateVersion7(), 3) { OccurredAtUtc = start.AddMinutes(30) },
            new KettleBoiled(Guid.CreateVersion7(), 1) { OccurredAtUtc = start.AddMinutes(10) },
            new KettleBoiled(Guid.CreateVersion7(), 2) { OccurredAtUtc = start.AddMinutes(20) });

        var handler = new RecordingHandler();
        await RunDispatcherAsync(handler);

        Assert.Equal([1, 2, 3], handler.Received.Select(received => received.Millilitres));
    }

    private async Task EnqueueAsync(params KettleBoiled[] events)
    {
        await using var context = postgres.CreateContext(_tenant);

        var outbox = new DbContextOutbox(
            context,
            new FixedTenantContext(_tenant),
            new StubCorrelation(),
            SystemClock.Instance);

        foreach (var published in events)
        {
            outbox.Enqueue(published);
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<OutboxMessage> SingleMessageAsync()
    {
        await using var context = postgres.CreateContext(_tenant);

        return await context.OutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Starts the real dispatcher, lets it complete one poll, and stops it. Waiting for the queue
    /// to drain rather than sleeping a fixed interval keeps the test honest on a slow machine.
    /// </summary>
    /// <param name="handler">The handler to register, or null to register none.</param>
    /// <param name="maxAttempts">The retry budget for this run.</param>
    /// <param name="expectPoll">False when the queue should already be exhausted.</param>
    private async Task RunDispatcherAsync(
        IIntegrationEventHandler<KettleBoiled>? handler,
        int maxAttempts = 8,
        bool expectPoll = true)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => postgres.CreateContext(_tenant));

        if (handler is not null)
        {
            services.AddSingleton(handler);
        }

        await using var provider = services.BuildServiceProvider();

        var options = new OutboxOptions
        {
            Enabled = true,
            PollIntervalSeconds = 1,
            BatchSize = 50,
            MaxAttempts = maxAttempts,
        };

        using var dispatcher = new OutboxDispatcher(
            provider,
            [new ModuleDbContextDescriptor("Test", ConventionsDbContext.SchemaName, 1, typeof(ConventionsDbContext))],
            new StaticOptionsMonitor(options),
            new IntegrationEventTypeMap([typeof(KettleBoiled).Assembly]),
            SystemClock.Instance,
            NullLogger<OutboxDispatcher>.Instance);

        // Captured before the dispatcher starts: a poll is observable as an increase in the total
        // attempt count, which every dispatch path produces — success, failure and no-handler alike.
        var before = await TotalAttemptsAsync();

        await dispatcher.StartAsync(TestContext.Current.CancellationToken);

        try
        {
            await WaitForPollAsync(before, expectPoll);
        }
        finally
        {
            // Stopping cancels the dispatcher's token. If it were mid-batch the commit would be
            // cancelled and the batch rolled back — correct at-least-once behaviour, and the reason
            // the wait above must observe a *committed* effect rather than merely elapsed time.
            await dispatcher.StopAsync(CancellationToken.None);
        }
    }

    private async Task<int> TotalAttemptsAsync()
    {
        await using var context = postgres.CreateContext(_tenant);

        return await context.OutboxMessages.SumAsync(
            message => message.Attempts,
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Waits for one committed poll, or — when the queue is expected to be exhausted — for long
    /// enough to be confident no poll happened.
    /// </summary>
    /// <param name="before">The attempt total before the dispatcher started.</param>
    /// <param name="expectPoll">Whether a message should still be eligible for delivery.</param>
    private async Task WaitForPollAsync(int before, bool expectPoll)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(expectPoll ? 15 : 3);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await TotalAttemptsAsync() > before)
            {
                Assert.True(expectPoll, "The dispatcher delivered a message that had exhausted its attempts.");
                return;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.False(expectPoll, "The dispatcher did not process the queue within 15 seconds.");
    }

    private sealed class RecordingHandler : IIntegrationEventHandler<KettleBoiled>
    {
        private readonly List<KettleBoiled> _received = [];

        public IReadOnlyList<KettleBoiled> Received => _received;

        public Task HandleAsync(KettleBoiled integrationEvent, CancellationToken cancellationToken)
        {
            _received.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : IIntegrationEventHandler<KettleBoiled>
    {
        public int Attempts { get; private set; }

        public Task HandleAsync(KettleBoiled integrationEvent, CancellationToken cancellationToken)
        {
            Attempts++;
            throw new InvalidOperationException("kettle exploded");
        }
    }

    private sealed class StubCorrelation : Infrastructure.Correlation.ICorrelationContext
    {
        public string CorrelationId => "dispatcher-test";
    }

    private sealed class StaticOptionsMonitor(OutboxOptions value) : IOptionsMonitor<OutboxOptions>
    {
        public OutboxOptions CurrentValue => value;

        public OutboxOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<OutboxOptions, string?> listener) => null;
    }
}
