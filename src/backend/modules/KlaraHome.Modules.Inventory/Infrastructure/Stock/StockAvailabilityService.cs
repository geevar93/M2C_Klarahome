using KlaraHome.Contracts.Inventory;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Modules.Inventory.Domain;
using KlaraHome.Modules.Inventory.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KlaraHome.Modules.Inventory.Infrastructure.Stock;

/// <summary>
/// The published face of this module: what other modules may read and do
/// (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// Availability is aggregated across warehouses, because that is the question a cart and a product
/// page actually have. A hold, by contrast, has to land on one row — units are on a shelf, not in an
/// abstraction — so <see cref="HoldAsync"/> walks the seller's locations in priority order and takes
/// the first that can supply the whole quantity.
/// </para>
/// <para>
/// It deliberately does not split a hold across locations. A line fulfilled from two warehouses is
/// two parcels, two dispatch SLAs and two shipping charges, and deciding that is Shipping's problem
/// at Step 16 rather than something to be implied here by a convenience.
/// </para>
/// </remarks>
/// <param name="context">The Inventory data context.</param>
/// <param name="ledger">Applies the movement atomically.</param>
/// <param name="options">The reservation TTL bounds.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports refusals, which are the interesting half.</param>
internal sealed partial class StockAvailabilityService(
    InventoryDbContext context,
    StockLedgerService ledger,
    IOptions<InventoryOptions> options,
    IClock clock,
    ILogger<StockAvailabilityService> logger) : IStockAvailability
{
    /// <summary>The partial unique index that permits one live hold per cart or order line.</summary>
    private const string LiveHoldIndex = "ux_stock_reservations_live";

    /// <inheritdoc />
    public async ValueTask<StockAvailability> FindAsync(
        Guid listingId,
        CancellationToken cancellationToken = default)
    {
        var rows = await context.StockItems
            .AsNoTracking()
            .IgnoreQueryFilters([ModelConventions.VendorFilter])
            .Where(item => item.ListingId == listingId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Aggregate(listingId, rows);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<Guid, StockAvailability>> FindManyAsync(
        IReadOnlyCollection<Guid> listingIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(listingIds);

        if (listingIds.Count == 0)
        {
            return new Dictionary<Guid, StockAvailability>();
        }

        var distinct = listingIds.Distinct().ToArray();

        var rows = await context.StockItems
            .AsNoTracking()
            .IgnoreQueryFilters([ModelConventions.VendorFilter])
            .Where(item => distinct.Contains(item.ListingId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byListing = rows
            .GroupBy(item => item.ListingId)
            .ToDictionary(group => group.Key, group => group.ToList());

        // Every requested id is present, tracked or not: "no stock row" is an answer a cart has to
        // render, and a caller that had to distinguish "absent" from "zero" would get it wrong.
        return distinct.ToDictionary(
            listingId => listingId,
            listingId => Aggregate(
                listingId,
                byListing.TryGetValue(listingId, out var items) ? items : []));
    }

    /// <inheritdoc />
    public async ValueTask<Guid?> HoldAsync(
        Guid listingId,
        int quantity,
        string referenceType,
        Guid referenceId,
        Guid lineReferenceId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
        {
            return null;
        }

        // A hold that outlives the ceiling would keep the last unit off sale for as long as the
        // caller felt like asking for, so the ceiling is applied here rather than trusted.
        var now = clock.UtcNow;
        var ceiling = now.AddMinutes(options.Value.MaxReservationMinutes);
        var expiry = expiresAt <= now ? now.AddMinutes(options.Value.DefaultReservationMinutes)
            : expiresAt > ceiling ? ceiling
            : expiresAt;

        Guid? held = null;
        var considered = 0;

        // One transaction around the whole thing, and it is not a nicety. The movement is a raw
        // command: outside a transaction it commits the instant it runs, so the units were held
        // before the reservation row that justifies them existed. Anything that then refused the
        // insert — and the partial unique index refuses one on every simultaneous retry of the same
        // cart line — left the stock held by nothing at all, with no ledger entry to explain it and
        // no hold anybody could release.
        try
        {
            await context.ExecuteInTransactionAsync(
                async (_, token) =>
                {
                    held = null;
                    considered = 0;

                    var existing = await context.Reservations
                        .FirstOrDefaultAsync(
                            reservation => reservation.ListingId == listingId
                                           && reservation.ReferenceType == referenceType
                                           && reservation.ReferenceId == referenceId
                                           && reservation.LineReferenceId == lineReferenceId
                                           && reservation.Status == ReservationStatus.Held,
                            token)
                        .ConfigureAwait(false);

                    // Idempotent on the line: a retried checkout gets its own hold back rather than
                    // taking the stock a second time. The expiry is pushed out, because the shopper
                    // is evidently still there.
                    if (existing is not null)
                    {
                        if (existing.Quantity >= quantity)
                        {
                            existing.ExtendTo(expiry);
                            await context.SaveChangesAsync(token).ConfigureAwait(false);
                            held = existing.Id;
                            return;
                        }

                        // They want more than they hold. Release what they have and take the whole
                        // quantity afresh, so the outcome is one hold of the requested size rather
                        // than two of unclear provenance.
                        //
                        // Saved before the new hold is added, and that ordering is load-bearing: the
                        // live-hold index permits one Held row per line, and EF writes an insert
                        // ahead of an update in the same batch — so the new row would arrive while
                        // the old one was still Held and collide with it.
                        await SettleOneAsync(existing, ReservationOutcome.Released, now, token)
                            .ConfigureAwait(false);

                        await context.SaveChangesAsync(token).ConfigureAwait(false);
                    }

                    var locations = await context.StockItems
                        .IgnoreQueryFilters([ModelConventions.VendorFilter])
                        .Where(item => item.ListingId == listingId)
                        .Join(
                            context.Warehouses.AsNoTracking().IgnoreQueryFilters([ModelConventions.VendorFilter]),
                            item => item.WarehouseId,
                            warehouse => warehouse.Id,
                            (item, warehouse) => new { item, warehouse.Priority, warehouse.Code, warehouse.IsActive })
                        .Where(row => row.IsActive)
                        .OrderBy(row => row.Priority)
                        .ThenBy(row => row.Code)
                        .Select(row => row.item)
                        .ToListAsync(token)
                        .ConfigureAwait(false);

                    considered = locations.Count;

                    foreach (var item in locations)
                    {
                        var result = await ledger
                            .ReserveAsync(item, quantity, referenceType, referenceId, token)
                            .ConfigureAwait(false);

                        if (!result.Applied)
                        {
                            continue;
                        }

                        var reservation = StockReservation.Hold(
                            item.Id,
                            listingId,
                            quantity,
                            referenceType,
                            referenceId,
                            lineReferenceId,
                            expiry);

                        context.Reservations.Add(reservation);
                        await context.SaveChangesAsync(token).ConfigureAwait(false);

                        held = reservation.Id;
                        return;
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsLiveHoldCollision(exception))
        {
            // Somebody else's retry of this very line committed between our read and our insert.
            // The whole attempt has rolled back, units included, so the honest answer is the hold
            // that did win — which is what idempotency on the line means. The change tracker is
            // cleared because everything in it belongs to the unit of work that was just discarded.
            context.ChangeTracker.Clear();

            HoldCollided(logger, listingId, referenceId, lineReferenceId);

            return await LiveHoldAsync(listingId, referenceType, referenceId, lineReferenceId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (held is null)
        {
            HoldRefused(logger, listingId, quantity, considered);
        }

        return held;
    }

    /// <summary>The id of the live hold on one line, or null if there is none.</summary>
    /// <param name="listingId">The offer.</param>
    /// <param name="referenceType">What is holding it.</param>
    /// <param name="referenceId">The cart or order.</param>
    /// <param name="lineReferenceId">The line within it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<Guid?> LiveHoldAsync(
        Guid listingId,
        string referenceType,
        Guid referenceId,
        Guid lineReferenceId,
        CancellationToken cancellationToken)
        => await context.Reservations
            .AsNoTracking()
            .Where(reservation => reservation.ListingId == listingId
                                  && reservation.ReferenceType == referenceType
                                  && reservation.ReferenceId == referenceId
                                  && reservation.LineReferenceId == lineReferenceId
                                  && reservation.Status == ReservationStatus.Held)
            .Select(reservation => (Guid?)reservation.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Whether a save failed because this line already has a live hold.</summary>
    /// <remarks>
    /// Matched on the index name rather than on the SQLSTATE alone: <c>23505</c> covers every unique
    /// violation in the schema, and swallowing an unrelated one as "somebody beat us to it" would
    /// turn a real bug into a wrong answer.
    /// </remarks>
    /// <param name="exception">The failure.</param>
    private static bool IsLiveHoldCollision(DbUpdateException exception)
        => exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: LiveHoldIndex,
        };

    /// <inheritdoc />
    public async ValueTask<int> SettleAsync(
        string referenceType,
        Guid referenceId,
        ReservationOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        var held = await context.Reservations
            .Where(reservation => reservation.ReferenceType == referenceType
                                  && reservation.ReferenceId == referenceId
                                  && reservation.Status == ReservationStatus.Held)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (held.Count == 0)
        {
            return 0;
        }

        var now = clock.UtcNow;
        var settled = 0;

        // In one transaction, for the same reason the hold is: each settlement runs a raw movement
        // that commits on its own outside one, so a failure part-way through a multi-line order
        // would leave some units moved with no ledger entry behind them and their holds still live.
        await context.ExecuteInTransactionAsync(
            async (_, token) =>
            {
                settled = 0;

                foreach (var reservation in held)
                {
                    if (await SettleOneAsync(reservation, outcome, now, token).ConfigureAwait(false))
                    {
                        settled++;
                    }
                }

                await context.SaveChangesAsync(token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        return settled;
    }

    /// <summary>
    /// Settles one hold: moves the stock, then marks the row. In that order, because a row marked
    /// settled whose units never moved is a hold nothing will ever release.
    /// </summary>
    private async Task<bool> SettleOneAsync(
        StockReservation reservation,
        ReservationOutcome outcome,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var item = await context.StockItems
            .IgnoreQueryFilters([ModelConventions.VendorFilter])
            .FirstOrDefaultAsync(candidate => candidate.Id == reservation.StockItemId, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            // The stock row is gone, so there is nothing to give back. Close the hold rather than
            // leaving it live for ever against a row that no longer exists.
            return reservation.Settle(ReservationStatus.Released, now);
        }

        var movement = outcome == ReservationOutcome.Committed
            ? await ledger
                .CommitAsync(
                    item,
                    reservation.Quantity,
                    reservation.ReferenceType,
                    reservation.ReferenceId,
                    cancellationToken)
                .ConfigureAwait(false)
            : await ledger
                .ReleaseAsync(
                    item,
                    reservation.Quantity,
                    reservation.ReferenceType,
                    reservation.ReferenceId,
                    note: null,
                    cancellationToken)
                .ConfigureAwait(false);

        if (!movement.Applied)
        {
            // A commit can be refused when the units were written off underneath the hold. The hold
            // is released instead: the sale has to be dealt with by Orders, and stranding the row
            // would make the shortfall permanent as well as unexplained.
            CommitRefused(logger, reservation.Id, item.Id, reservation.Quantity);

            await ledger
                .ReleaseAsync(
                    item,
                    reservation.Quantity,
                    reservation.ReferenceType,
                    reservation.ReferenceId,
                    "Commit refused: the units were no longer on hand.",
                    cancellationToken)
                .ConfigureAwait(false);

            return reservation.Settle(ReservationStatus.Released, now);
        }

        return reservation.Settle(
            outcome == ReservationOutcome.Committed ? ReservationStatus.Committed : ReservationStatus.Released,
            now);
    }

    /// <summary>Rolls the rows for one offer into the one answer a caller wants.</summary>
    private static StockAvailability Aggregate(Guid listingId, List<StockItem> items)
    {
        if (items.Count == 0)
        {
            return new StockAvailability(listingId, 0, 0, 0, false, false, null, IsTracked: false);
        }

        var onHand = 0;
        var reserved = 0;
        var available = 0;
        var backorder = false;
        var preorder = false;
        DateTimeOffset? preorderAt = null;

        foreach (var item in items)
        {
            onHand += item.QuantityOnHand;
            reserved += item.QuantityReserved;

            // Summed per row rather than taken from the totals, because on hand less reserved is
            // floored at zero on each shelf: a backordered row in deficit must not eat into what
            // another location genuinely has.
            available += item.QuantityAvailable;

            backorder |= item.AllowBackorder;
            preorder |= item.AllowPreorder;

            // The soonest promise across the locations that make one. A shopper is told the
            // earliest date anybody can meet, not the latest.
            if (item.AllowPreorder
                && item.PreorderAvailableAt is { } at
                && (preorderAt is null || at < preorderAt))
            {
                preorderAt = at;
            }
        }

        return new StockAvailability(
            listingId,
            onHand,
            reserved,
            available,
            backorder,
            preorder,
            preorderAt,
            IsTracked: true);
    }

    [LoggerMessage(
        EventId = 7100,
        Level = LogLevel.Information,
        Message = "No location could hold {Quantity} of listing {ListingId}; {Locations} considered")]
    private static partial void HoldRefused(ILogger logger, Guid listingId, int quantity, int locations);

    [LoggerMessage(
        EventId = 7102,
        Level = LogLevel.Information,
        Message = "A concurrent hold on listing {ListingId} for {ReferenceId}/{LineReferenceId} won the "
                  + "live-hold index; returning the hold that committed")]
    private static partial void HoldCollided(
        ILogger logger,
        Guid listingId,
        Guid referenceId,
        Guid lineReferenceId);

    [LoggerMessage(
        EventId = 7101,
        Level = LogLevel.Warning,
        Message = "Reservation {ReservationId} could not commit {Quantity} from stock item {StockItemId}")]
    private static partial void CommitRefused(
        ILogger logger,
        Guid reservationId,
        Guid stockItemId,
        int quantity);
}
