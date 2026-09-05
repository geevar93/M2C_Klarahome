using KlaraHome.Contracts.IntegrationEvents;
using KlaraHome.Infrastructure.Correlation;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.SharedKernel.Time;
using KlaraHome.Testing.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.IntegrationTests.Persistence;

/// <summary>An integration event used only by these tests.</summary>
internal sealed record KettleBoiled(Guid KettleId, int Millilitres) : IntegrationEvent;

/// <summary>
/// The outbox's one promise: an integration event and the state change that caused it commit
/// together or not at all (ADR-003). Everything else about the pattern is an optimisation.
/// </summary>
[Collection(PostgresDatabase.CollectionName)]
public sealed class OutboxTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly Guid _tenant = Guid.CreateVersion7();

    public async ValueTask InitializeAsync()
    {
        Assert.SkipWhen(postgres.SkipReason is not null, postgres.SkipReason ?? string.Empty);

        await using var context = postgres.CreateContext(_tenant);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static DbContextOutbox OutboxFor(ConventionsDbContext context, Guid tenant, string correlationId = "test-correlation")
        => new DbContextOutbox(
            context,
            new FixedTenantContext(tenant),
            new StubCorrelationContext(correlationId),
            SystemClock.Instance);

    [Fact]
    public async Task A_published_event_commits_with_the_state_change_that_caused_it()
    {
        var published = new KettleBoiled(Guid.CreateVersion7(), 1500);

        await using (var context = postgres.CreateContext(_tenant))
        {
            context.AuditedThings.Add(new AuditedThing { DisplayName = "kettle" });
            OutboxFor(context, _tenant).Enqueue(published);

            // One SaveChanges, one transaction, both rows.
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reader = postgres.CreateContext(_tenant);

        Assert.Single(await reader.AuditedThings.ToListAsync(TestContext.Current.CancellationToken));

        var message = await reader.OutboxMessages.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(published.EventId, message.Id);
        Assert.Equal(_tenant, message.TenantId);
        Assert.Equal("KlaraHome.IntegrationTests.Persistence.KettleBoiled", message.Type);
        Assert.Null(message.ProcessedAt);
        Assert.Equal(0, message.Attempts);
        Assert.Equal("test-correlation", message.CorrelationId);
    }

    [Fact]
    public async Task A_rolled_back_change_publishes_nothing()
    {
        await using (var context = postgres.CreateContext(_tenant))
        {
            // The transaction is rolled back the way one actually is in production: the work
            // throws, so the helper never reaches its commit.
            await Assert.ThrowsAsync<NotSupportedException>(() => context.ExecuteInTransactionAsync(
                async (transactional, token) =>
                {
                    transactional.Set<AuditedThing>().Add(new AuditedThing { DisplayName = "never" });
                    OutboxFor(context, _tenant).Enqueue(new KettleBoiled(Guid.CreateVersion7(), 500));

                    await transactional.SaveChangesAsync(token);

                    throw new NotSupportedException("the handler failed after publishing");
                },
                TestContext.Current.CancellationToken));
        }

        await using var reader = postgres.CreateContext(_tenant);

        // This is the whole reason the outbox is a table and not a queue client: an announcement
        // of something that did not happen is worse than no announcement at all.
        Assert.Empty(await reader.AuditedThings.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await reader.OutboxMessages.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_payload_survives_the_round_trip_through_jsonb()
    {
        var published = new KettleBoiled(Guid.CreateVersion7(), 1750);

        await using (var context = postgres.CreateContext(_tenant))
        {
            OutboxFor(context, _tenant).Enqueue(published);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reader = postgres.CreateContext(_tenant);
        var message = await reader.OutboxMessages.SingleAsync(TestContext.Current.CancellationToken);

        var restored = System.Text.Json.JsonSerializer.Deserialize<KettleBoiled>(
            message.Payload,
            OutboxSerialization.Options)!;

        Assert.Equal(published.KettleId, restored.KettleId);
        Assert.Equal(published.Millilitres, restored.Millilitres);
    }

    [Fact]
    public async Task The_pending_query_reads_in_occurrence_order_and_skips_processed_messages()
    {
        var start = new DateTimeOffset(2026, 9, 5, 6, 0, 0, TimeSpan.Zero);

        await using (var context = postgres.CreateContext(_tenant))
        {
            var outbox = OutboxFor(context, _tenant);

            foreach (var minutes in new[] { 30, 10, 20 })
            {
                outbox.Enqueue(new KettleBoiled(Guid.CreateVersion7(), minutes)
                {
                    OccurredAtUtc = start.AddMinutes(minutes),
                });
            }

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var processor = postgres.CreateContext(_tenant))
        {
            var first = await processor.OutboxMessages
                .OrderBy(message => message.OccurredAt)
                .FirstAsync(TestContext.Current.CancellationToken);

            first.ProcessedAt = start.AddHours(1);
            await processor.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reader = postgres.CreateContext(_tenant);

        var pending = await reader.OutboxMessages
            .Where(message => message.ProcessedAt == null)
            .OrderBy(message => message.OccurredAt)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([start.AddMinutes(20), start.AddMinutes(30)], pending.Select(message => message.OccurredAt));
    }

    [Fact]
    public async Task The_inbox_key_refuses_a_second_completion_for_the_same_handler()
    {
        var messageId = Guid.CreateVersion7();

        await using (var context = postgres.CreateContext(_tenant))
        {
            context.InboxMessages.Add(new InboxMessage
            {
                MessageId = messageId,
                Handler = "SendConfirmationEmail",
                ProcessedAt = DateTimeOffset.UtcNow,
            });

            // A different handler for the same message is a different row, and must be allowed.
            context.InboxMessages.Add(new InboxMessage
            {
                MessageId = messageId,
                Handler = "DeductStock",
                ProcessedAt = DateTimeOffset.UtcNow,
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var duplicate = postgres.CreateContext(_tenant);
        duplicate.InboxMessages.Add(new InboxMessage
        {
            MessageId = messageId,
            Handler = "SendConfirmationEmail",
            ProcessedAt = DateTimeOffset.UtcNow,
        });

        // The primary key is the idempotency guarantee: the database refuses, so a handler cannot
        // run twice for one message even if two workers deliver it at the same instant.
        await Assert.ThrowsAsync<DbUpdateException>(
            () => duplicate.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    private sealed class StubCorrelationContext(string correlationId) : ICorrelationContext
    {
        public string CorrelationId { get; } = correlationId;
    }
}
