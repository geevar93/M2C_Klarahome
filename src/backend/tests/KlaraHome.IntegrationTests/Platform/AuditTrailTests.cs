using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Platform.Application.Auditing;
using KlaraHome.Modules.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace KlaraHome.IntegrationTests.Platform;

/// <summary>
/// The audit trail's guarantees are database guarantees: it is partitioned, it is append-only, and
/// the append-only part is enforced by the engine rather than by our own good intentions.
/// </summary>
[Collection(KlaraHomeSchema.CollectionName)]
public sealed class AuditTrailTests(KlaraHomeSchemaFixture fixture)
{
    [Fact]
    public async Task An_entry_is_written_and_can_be_read_back()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        var entityId = Guid.CreateVersion7().ToString();

        using var scope = fixture.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuditLogger>().RecordAsync(
            new AuditEntry
            {
                Action = "platform.test.recorded",
                EntityType = "AuditTrailTest",
                EntityId = entityId,
                Before = new { value = 1 },
                After = new { value = 2 },
                ActorType = AuditActorType.StaffUser,
            },
            TestContext.Current.CancellationToken);

        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var entry = await context.AuditLogs
            .AsNoTracking()
            .SingleAsync(row => row.EntityId == entityId, TestContext.Current.CancellationToken);

        Assert.Equal("platform.test.recorded", entry.Action);
        Assert.Equal(AuditActorType.StaffUser, entry.ActorType);
        // Parsed rather than string-matched: jsonb is stored normalised, not as it was written.
        Assert.Equal(1, ValueOf(entry.Before));
        Assert.Equal(2, ValueOf(entry.After));

        // The correlation id is filled in from the ambient context, never by the caller.
        Assert.False(string.IsNullOrWhiteSpace(entry.CorrelationId));
    }

    [Fact]
    public async Task An_entry_lands_in_the_partition_for_its_month()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        var entityId = Guid.CreateVersion7().ToString();

        using var scope = fixture.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuditLogger>().RecordAsync(
            new AuditEntry { Action = "platform.test.partitioned", EntityType = "AuditTrailTest", EntityId = entityId },
            TestContext.Current.CancellationToken);

        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        // tableoid names the partition the row actually lives in. If routing were broken the row
        // would be in audit_logs_default, and every query would stop being pruned.
        var partition = await context.Database
            .SqlQuery<string>($"""
                SELECT c.relname AS "Value"
                FROM platform.audit_logs a
                JOIN pg_class c ON c.oid = a.tableoid
                WHERE a.entity_id = {entityId}
                """)
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.StartsWith("audit_logs_", partition, StringComparison.Ordinal);
        Assert.NotEqual("audit_logs_default", partition);
    }

    [Fact]
    public async Task An_update_is_refused_by_the_database()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await InsertProbeAsync(connection, "platform.test.immutable-update");

        var failure = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var command = new NpgsqlCommand(
                "UPDATE platform.audit_logs SET action = 'tampered' WHERE action = 'platform.test.immutable-update'",
                connection);

            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        });

        Assert.Contains("append-only", failure.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_delete_is_refused_by_the_database()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await InsertProbeAsync(connection, "platform.test.immutable-delete");

        // Retention is a matter of dropping a partition, which is DDL. Deleting a row is not, and
        // an audit trail somebody can quietly prune is not an audit trail.
        var failure = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var command = new NpgsqlCommand(
                "DELETE FROM platform.audit_logs WHERE action = 'platform.test.immutable-delete'",
                connection);

            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        });

        Assert.Contains("append-only", failure.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_search_pages_forward_without_repeating_or_skipping_an_entry()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        var entityType = "Paging" + Guid.CreateVersion7().ToString("n");

        using (var writeScope = fixture.Services.CreateScope())
        {
            var audit = writeScope.ServiceProvider.GetRequiredService<IAuditLogger>();

            for (var index = 0; index < 7; index++)
            {
                await audit.RecordAsync(
                    new AuditEntry
                    {
                        Action = "platform.test.paged",
                        EntityType = entityType,
                        EntityId = index.ToString("00", null),
                    },
                    TestContext.Current.CancellationToken);
            }
        }

        using var scope = fixture.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var seen = new List<AuditLogResponse>();
        string? cursor = null;

        for (var page = 0; page < 5; page++)
        {
            var result = await dispatcher.QueryAsync(
                new SearchAuditLogsQuery(entityType, null, null, null, null, null, cursor, 3),
                TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : string.Empty);
            seen.AddRange(result.Value.Items);

            cursor = result.Value.Page.NextCursor;
            if (cursor is null)
            {
                break;
            }
        }

        // Every entry, exactly once, across three pages and a bit.
        Assert.Null(cursor);
        Assert.Equal(7, seen.Count);
        Assert.Distinct(seen.Select(entry => entry.Id));

        Assert.Equal(
            Enumerable.Range(0, 7).Select(index => index.ToString("00", null)).Order(StringComparer.Ordinal),
            seen.Select(entry => entry.EntityId!).Order(StringComparer.Ordinal));

        // Newest first, and the order holds across the page boundaries rather than only inside a
        // page. Timestamps can tie - the wall clock is coarser than the loop - so the tie-break on
        // the id is part of what is being asserted here.
        var ordered = seen
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.Id)
            .ToList();

        Assert.Equal(ordered.Select(entry => entry.Id), seen.Select(entry => entry.Id));
    }

    [Fact]
    public async Task A_malformed_cursor_is_a_client_error_rather_than_a_crash()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var scope = fixture.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.QueryAsync(
            new SearchAuditLogsQuery(null, null, null, null, null, null, Cursor.Encode("nonsense"), 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("CURSOR_INVALID", result.Error.Code);
    }

    [Fact]
    public async Task An_entity_id_without_an_entity_type_is_refused()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var scope = fixture.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        // An id on its own identifies nothing: two modules can both have an entity 42.
        var result = await dispatcher.QueryAsync(
            new SearchAuditLogsQuery(null, "42", null, null, null, null, null, null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("VALIDATION_FAILED", result.Error.Code);
    }

    private static int ValueOf(string? json)
    {
        Assert.NotNull(json);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.GetProperty("value").GetInt32();
    }

    private static async Task InsertProbeAsync(NpgsqlConnection connection, string action)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO platform.audit_logs
                (occurred_at, id, tenant_id, actor_type, action, entity_type)
            VALUES
                (now(), gen_random_uuid(), gen_random_uuid(), 'System', @action, 'AuditTrailTest')
            """,
            connection);

        command.Parameters.AddWithValue("action", action);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
