using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Runs the real outbox dispatcher over the host under test, once, and waits for the queue to
/// empty.
/// </summary>
/// <remarks>
/// <para>
/// The API host does not dispatch its own outbox — that is the worker's job, and every API replica
/// polling would contend for the same rows — so a test that needs an integration event to actually
/// reach its handler has to supply the missing half. This is the real
/// <see cref="OutboxDispatcher"/> over the real service provider: the same type map, the same
/// <c>FOR UPDATE SKIP LOCKED</c> claim, the same handler resolution. What a test proves through it
/// is what the worker will do.
/// </para>
/// <para>
/// Constructed rather than resolved because the API host never registers it, and started rather
/// than called because the drain loop is private. Waiting for the pending count to reach zero keeps
/// the test honest on a slow machine, where a fixed sleep would be either flaky or slow.
/// </para>
/// </remarks>
internal static class OutboxDrain
{
    /// <summary>Dispatches everything pending in the outbox, and waits until nothing is left.</summary>
    /// <param name="factory">The host whose handlers should receive the events.</param>
    /// <param name="database">Direct SQL, for observing when the queue is empty.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task RunAsync(
        CommerceApiFactory factory,
        Sql database,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(database);

        var options = new OutboxOptions
        {
            Enabled = true,
            PollIntervalSeconds = 1,
            BatchSize = 200,
            MaxAttempts = 3,
        };

        using var dispatcher = new OutboxDispatcher(
            factory.Services,
            factory.Services.GetServices<ModuleDbContextDescriptor>(),
            new FixedOutboxOptions(options),
            new IntegrationEventTypeMap([typeof(Contracts.ContractsAssembly).Assembly]),
            SystemClock.Instance,
            NullLogger<OutboxDispatcher>.Instance);

        await dispatcher.StartAsync(cancellationToken);

        try
        {
            await WaitForEmptyAsync(database, options.MaxAttempts, cancellationToken);
        }
        finally
        {
            // Stopping cancels the dispatcher's token. Anything mid-batch is rolled back, which is
            // correct at-least-once behaviour — which is why the wait above observes a committed
            // effect rather than merely elapsed time.
            await dispatcher.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>How many messages are still eligible for delivery.</summary>
    /// <param name="database">Direct SQL.</param>
    /// <param name="maxAttempts">The retry budget, past which a message is left alone.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task<long> PendingAsync(Sql database, int maxAttempts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        return database.CountAsync(
            "SELECT COUNT(*) FROM platform.outbox_messages WHERE processed_at IS NULL AND attempts < $1",
            cancellationToken,
            maxAttempts);
    }

    private static async Task WaitForEmptyAsync(Sql database, int maxAttempts, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await PendingAsync(database, maxAttempts, cancellationToken) == 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        Assert.Fail("The outbox still held undelivered messages after 60 seconds.");
    }

    /// <summary>The dispatcher reads its options from a monitor; this one never changes.</summary>
    /// <param name="value">The settings for this run.</param>
    private sealed class FixedOutboxOptions(OutboxOptions value) : IOptionsMonitor<OutboxOptions>
    {
        public OutboxOptions CurrentValue => value;

        public OutboxOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<OutboxOptions, string?> listener) => null;
    }
}
