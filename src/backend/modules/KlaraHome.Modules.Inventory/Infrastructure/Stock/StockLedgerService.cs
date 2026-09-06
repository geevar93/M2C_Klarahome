using KlaraHome.Modules.Inventory.Domain;
using KlaraHome.Modules.Inventory.Infrastructure.Events;
using KlaraHome.Modules.Inventory.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace KlaraHome.Modules.Inventory.Infrastructure.Stock;

/// <summary>What a movement did, once the database had applied it.</summary>
/// <param name="Applied">
/// Whether the movement went through. False means the row refused it — there was not enough on hand,
/// or not enough unheld stock — and nothing at all was written.
/// </param>
/// <param name="QuantityOnHand">Units on hand after.</param>
/// <param name="QuantityReserved">Units reserved after.</param>
/// <param name="WasAvailable">Whether anything was available before.</param>
internal readonly record struct StockMovementResult(
    bool Applied,
    int QuantityOnHand,
    int QuantityReserved,
    bool WasAvailable)
{
    /// <summary>The movement the row refused.</summary>
    public static StockMovementResult Refused { get; } = new(false, 0, 0, false);

    /// <summary>What may still be sold after the movement.</summary>
    public int QuantityAvailable => Math.Max(0, QuantityOnHand - QuantityReserved);
}

/// <summary>
/// The only way stock moves on this platform.
/// </summary>
/// <remarks>
/// <para>
/// Every movement is a <b>single conditional <c>UPDATE</c></b> that both tests the invariant and
/// applies the change, and then a ledger entry recording what that update returned. The shape is
/// the whole oversell defence (docs/02-domain-model.md §4.2): a read followed by a write leaves a
/// window in which two callers each see the last unit and each take it, and no amount of
/// application-level checking closes it. PostgreSQL holds a row lock for the duration of the
/// statement, so the second caller's <c>UPDATE</c> re-evaluates its <c>WHERE</c> against the
/// already-updated row and matches nothing.
/// </para>
/// <para>
/// Refusal is therefore <em>no row returned</em>, not an exception. The check constraints on
/// <c>stock_items</c> restate the same predicates, and they are the backstop rather than the
/// mechanism: a constraint violation here would be a bug in this file, not a rejected sale.
/// </para>
/// <para>
/// Nothing here commits. The caller's <c>SaveChangesAsync</c> does, which is what puts the ledger
/// entry, the reservation row and the outbox message into one transaction — an event announcing a
/// movement that was rolled back would be worse than no event at all.
/// </para>
/// <para>
/// Every statement moves both columns and takes the same five parameters, so the ledger entry, the
/// event and the parameter binding have exactly one shape to be right about.
/// </para>
/// </remarks>
/// <param name="context">The Inventory data context.</param>
/// <param name="publisher">Announces the movement downstream once the transaction commits.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class StockLedgerService(
    InventoryDbContext context,
    InventoryEventPublisher publisher,
    IClock clock)
{
    private const string Table = $"{InventoryModule.SchemaName}.stock_items";

    /// <summary>The tail every statement shares: the balances the ledger entry is made of.</summary>
    /// <remarks>
    /// <c>xmin</c> comes back with them because the raw update has just bumped the row version, and
    /// an entity whose original <c>xmin</c> is stale would fail its next optimistic-concurrency
    /// check for a conflict that never happened. Cast through text because <c>xid</c> has no
    /// portable numeric read.
    /// </remarks>
    private const string Returning = "RETURNING quantity_on_hand, quantity_reserved, xmin::text::bigint";

    private const string Where = "WHERE id = @id AND tenant_id = @tenant";

    /// <summary>
    /// Applies a movement to on hand — a receipt, an adjustment, a write-off, a correction, a
    /// transfer leg.
    /// </summary>
    /// <remarks>
    /// The guard is <c>quantity_on_hand + change &gt;= 0</c>, so a negative movement can never take
    /// a location below empty. It deliberately does <em>not</em> respect existing holds: writing off
    /// damaged goods somebody has in their basket is a real situation, and the resulting shortfall
    /// belongs on a report rather than being hidden by refusing the write-off.
    /// </remarks>
    /// <param name="item">The stock row. Must already exist in the database.</param>
    /// <param name="change">Signed units.</param>
    /// <param name="reason">Why.</param>
    /// <param name="referenceType">What caused it.</param>
    /// <param name="referenceId">The id of whatever caused it.</param>
    /// <param name="note">What the operator wrote.</param>
    /// <param name="actorId">Who did it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<StockMovementResult> MoveAsync(
        StockItem item,
        int change,
        StockMovementReason reason,
        string? referenceType = null,
        Guid? referenceId = null,
        string? note = null,
        Guid? actorId = null,
        CancellationToken cancellationToken = default)
        => ApplyAsync(
            item,
            change,
            reservedChange: 0,
            $"""
             UPDATE {Table}
             SET quantity_on_hand = quantity_on_hand + @change,
                 quantity_reserved = quantity_reserved + @reserved,
                 updated_at = @now
             {Where}
               AND quantity_on_hand + @change >= 0
             {Returning}
             """,
            reason,
            referenceType,
            referenceId,
            note,
            actorId,
            cancellationToken);

    /// <summary>
    /// Holds units for a cart or an order. This is the oversell boundary.
    /// </summary>
    /// <remarks>
    /// The guard is "the new reserved total still fits inside on hand", relaxed where the seller has
    /// said they will backorder or pre-order. It is the same predicate as the
    /// <c>ck_stock_items_reserved_within_hand</c> constraint, on purpose.
    /// </remarks>
    /// <param name="item">The stock row.</param>
    /// <param name="quantity">How many units to hold. Must be positive.</param>
    /// <param name="referenceType">What is holding them.</param>
    /// <param name="referenceId">The cart or order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<StockMovementResult> ReserveAsync(
        StockItem item,
        int quantity,
        string referenceType,
        Guid referenceId,
        CancellationToken cancellationToken = default)
        => ApplyAsync(
            item,
            change: 0,
            reservedChange: quantity,
            $"""
             UPDATE {Table}
             SET quantity_on_hand = quantity_on_hand + @change,
                 quantity_reserved = quantity_reserved + @reserved,
                 updated_at = @now
             {Where}
               AND (allow_backorder
                    OR allow_preorder
                    OR quantity_reserved + @reserved <= quantity_on_hand)
             {Returning}
             """,
            StockMovementReason.Reservation,
            referenceType,
            referenceId,
            note: null,
            actorId: null,
            cancellationToken);

    /// <summary>Gives a hold back. On hand does not move; the units become available again.</summary>
    /// <param name="item">The stock row.</param>
    /// <param name="quantity">How many units were held.</param>
    /// <param name="referenceType">What was holding them.</param>
    /// <param name="referenceId">The cart or order.</param>
    /// <param name="note">Why, for a sweep.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<StockMovementResult> ReleaseAsync(
        StockItem item,
        int quantity,
        string? referenceType,
        Guid? referenceId,
        string? note = null,
        CancellationToken cancellationToken = default)
        => ApplyAsync(
            item,
            change: 0,
            reservedChange: -quantity,

            // Floored at zero rather than guarded above it. A release that would take reserved
            // negative means a hold was settled twice, and putting the units back on sale is the
            // safe half of that mistake — refusing would strand them held for ever.
            $"""
             UPDATE {Table}
             SET quantity_on_hand = quantity_on_hand + @change,
                 quantity_reserved = GREATEST(0, quantity_reserved + @reserved),
                 updated_at = @now
             {Where}
             {Returning}
             """,
            StockMovementReason.Release,
            referenceType,
            referenceId,
            note,
            actorId: null,
            cancellationToken);

    /// <summary>
    /// Commits a hold into a sale: the units leave stock and stop being held, in one statement.
    /// </summary>
    /// <remarks>
    /// Both columns move together because they have to. Two statements would leave a window in
    /// which the units were neither held nor sold, and a concurrent reservation could take stock
    /// that has already been paid for.
    /// </remarks>
    /// <param name="item">The stock row.</param>
    /// <param name="quantity">How many units were held.</param>
    /// <param name="referenceType">What held them.</param>
    /// <param name="referenceId">The order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<StockMovementResult> CommitAsync(
        StockItem item,
        int quantity,
        string? referenceType,
        Guid? referenceId,
        CancellationToken cancellationToken = default)
        => ApplyAsync(
            item,
            change: -quantity,
            reservedChange: -quantity,
            $"""
             UPDATE {Table}
             SET quantity_on_hand = quantity_on_hand + @change,
                 quantity_reserved = GREATEST(0, quantity_reserved + @reserved),
                 updated_at = @now
             {Where}
               AND quantity_on_hand + @change >= 0
             {Returning}
             """,
            StockMovementReason.Sale,
            referenceType,
            referenceId,
            note: null,
            actorId: null,
            cancellationToken);

    /// <summary>
    /// Runs one conditional update, records the ledger entry it justifies, and queues the events
    /// that follow from it.
    /// </summary>
    private async Task<StockMovementResult> ApplyAsync(
        StockItem item,
        int change,
        int reservedChange,
        string sql,
        StockMovementReason reason,
        string? referenceType,
        Guid? referenceId,
        string? note,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        // Nothing to do, and a zero-quantity ledger entry is noise in the one table that has to
        // stay readable.
        if (change == 0 && reservedChange == 0)
        {
            return new StockMovementResult(true, item.QuantityOnHand, item.QuantityReserved, item.IsAvailable);
        }

        var entry = context.Entry(item);

        // A row that has not been inserted yet cannot be updated. Flushing an Added stock item is
        // the one case where this service saves on the caller's behalf, and it is cheaper than
        // making every caller remember.
        if (entry.State == EntityState.Added)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var wasAvailable = item.IsAvailable;
        var now = clock.UtcNow;

        var applied = await ExecuteAsync(item.Id, change, reservedChange, now, sql, cancellationToken)
            .ConfigureAwait(false);

        if (applied is not { } row)
        {
            return StockMovementResult.Refused;
        }

        Resynchronise(entry, item, row);

        context.LedgerEntries.Add(StockLedgerEntry.Record(
            item.Id,
            change,
            row.OnHand,
            reservedChange,
            row.Reserved,
            reason,
            now,
            referenceType,
            referenceId,
            note,
            actorId));

        publisher.StockChanged(item, wasAvailable);

        if (item.TryRaiseLowStock(now))
        {
            publisher.RunningLow(item);
        }

        return new StockMovementResult(true, row.OnHand, row.Reserved, wasAvailable);
    }

    /// <summary>
    /// Makes the tracked entity agree with the row the update just wrote, without asking EF to
    /// write it back.
    /// </summary>
    /// <remarks>
    /// Both quantities and the row version have their <em>original</em> values set to what the
    /// database now holds. Setting the current value alone would make EF think the caller had
    /// edited them and issue a second update, and leaving <c>xmin</c> stale would make the entity's
    /// next save fail its concurrency check against a conflict this service caused itself.
    /// </remarks>
    private static void Resynchronise(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<StockItem> entry,
        StockItem item,
        (int OnHand, int Reserved, long Version) row)
    {
        item.SyncQuantities(row.OnHand, row.Reserved);

        entry.Property(nameof(StockItem.QuantityOnHand)).OriginalValue = row.OnHand;
        entry.Property(nameof(StockItem.QuantityReserved)).OriginalValue = row.Reserved;

        const string version = KlaraHome.Infrastructure.Persistence.ModelConventions.ConcurrencyTokenName;

        if (entry.Metadata.FindProperty(version) is not null)
        {
            entry.Property(version).OriginalValue = unchecked((uint)row.Version);
        }
    }

    /// <summary>
    /// Runs the statement and reads back the two balances and the new row version, or null when the
    /// row refused it.
    /// </summary>
    /// <remarks>
    /// A raw command rather than <c>ExecuteUpdate</c>, because EF cannot express <c>RETURNING</c> on
    /// an update and the returned balances are what the ledger entry is made of. Reading them back
    /// afterwards would be a second statement and a second race.
    /// </remarks>
    private async Task<(int OnHand, int Reserved, long Version)?> ExecuteAsync(
        Guid stockItemId,
        int change,
        int reservedChange,
        DateTimeOffset now,
        string sql,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;

        if (opened)
        {
            await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            // Enlisted explicitly: EF only does this for its own commands, and a movement that ran
            // outside the caller's transaction would commit even when the handler rolled back.
            if (context.Database.CurrentTransaction is { } transaction)
            {
                command.Transaction = transaction.GetDbTransaction();
            }

            command.Parameters.Add(new NpgsqlParameter<Guid>("id", stockItemId));
            command.Parameters.Add(new NpgsqlParameter<Guid>("tenant", context.TenantId));
            command.Parameters.Add(new NpgsqlParameter<DateTimeOffset>("now", now));
            command.Parameters.Add(new NpgsqlParameter<int>("change", change));
            command.Parameters.Add(new NpgsqlParameter<int>("reserved", reservedChange));

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            // No row back means the WHERE did not match, which means the invariant refused the
            // movement. That is the ordinary "not enough stock" answer, not a failure.
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt64(2));
        }
        finally
        {
            if (opened)
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }
}
