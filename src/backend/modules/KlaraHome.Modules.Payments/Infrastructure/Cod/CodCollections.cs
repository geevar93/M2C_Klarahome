using KlaraHome.Contracts.Payments;
using KlaraHome.Modules.Payments.Application;
using KlaraHome.Modules.Payments.Domain;
using KlaraHome.Modules.Payments.Infrastructure.Events;
using KlaraHome.Modules.Payments.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Payments.Infrastructure.Cod;

/// <summary>
/// What the Shipping module is allowed to do to the cash a courier is carrying
/// (docs/08-integrations.md §2).
/// </summary>
/// <remarks>
/// <para>
/// Payments keeps the record of money, Shipping keeps the parcel, and this is where the two facts
/// meet. Every method here is a fact only Shipping can know — which consignment the cash is on,
/// whether it was taken at the door, whether the parcel came back instead — applied to the aggregate
/// that already owns the arithmetic.
/// </para>
/// <para>
/// It exists rather than a second cash table in the shipping schema because a marketplace with two
/// answers to "what has the courier not yet remitted" will one day settle a seller out of money
/// nobody collected.
/// </para>
/// <para>
/// The query filters are bypassed throughout. A courier webhook has no caller and no tenant, and a
/// filtered read would find nothing at all rather than finding the wrong thing — which is the more
/// dangerous of the two failures here, because it looks exactly like a parcel that carried no cash.
/// </para>
/// </remarks>
/// <param name="context">The Payments data context.</param>
/// <param name="events">Announces cash movements to the outbox.</param>
internal sealed class CodCollections(PaymentsDbContext context, PaymentsEventPublisher events) : ICodCollections
{
    /// <inheritdoc />
    public async Task<CodCollectionView?> FindBySubOrderAsync(
        Guid subOrderId,
        CancellationToken cancellationToken = default)
    {
        var found = await Query()
            .AsNoTracking()
            .FirstOrDefaultAsync(collection => collection.SubOrderId == subOrderId, cancellationToken)
            .ConfigureAwait(false);

        return found is null ? null : Project(found);
    }

    /// <inheritdoc />
    public async Task<Result> AttachShipmentAsync(
        Guid subOrderId,
        Guid shipmentId,
        CancellationToken cancellationToken = default)
    {
        var collection = await Query()
            .FirstOrDefaultAsync(candidate => candidate.SubOrderId == subOrderId, cancellationToken)
            .ConfigureAwait(false);

        // A prepaid parcel has no cash record and never will. Attaching a shipment to nothing is a
        // success rather than a 404: the caller books an AWB for every parcel and should not have to
        // know which of them carry money.
        if (collection is null)
        {
            return Result.Success();
        }

        collection.AttachShipment(shipmentId);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> RecordCollectedAsync(
        Guid subOrderId,
        decimal amount,
        DateTimeOffset collectedAt,
        CancellationToken cancellationToken = default)
    {
        var collection = await Query()
            .FirstOrDefaultAsync(candidate => candidate.SubOrderId == subOrderId, cancellationToken)
            .ConfigureAwait(false);

        if (collection is null)
        {
            return Result.Success();
        }

        // Idempotent. A delivery scan arrives at least once and the polling fallback can reach it
        // independently; cash already recorded as collected is the fact the caller was asserting.
        if (collection.Collect(amount, collectedAt, collectedBy: null))
        {
            events.CashRecorded(collection);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> WaiveAsync(
        Guid subOrderId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var collection = await Query()
            .FirstOrDefaultAsync(candidate => candidate.SubOrderId == subOrderId, cancellationToken)
            .ConfigureAwait(false);

        if (collection is null)
        {
            return Result.Success();
        }

        if (collection.Waive(reason))
        {
            events.CashRecorded(collection);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<int>> RecordRemittanceAsync(
        IReadOnlyCollection<Guid> shipmentIds,
        string reference,
        decimal? amount,
        DateTimeOffset remittedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shipmentIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        if (shipmentIds.Count == 0)
        {
            return Result.Success(0);
        }

        var ids = shipmentIds.Distinct().ToArray();

        var records = await Query()
            .Where(collection => collection.ShipmentId != null && ids.Contains(collection.ShipmentId!.Value))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var open = records
            .Where(collection => collection.Status
                is CodCollectionStatus.Pending
                or CodCollectionStatus.Collected)
            .ToList();

        if (open.Count == 0)
        {
            return Result.Success(0);
        }

        // Apportioned in proportion to what each record was for, so a courier who deducts their fee
        // from the batch leaves every record short by its share rather than leaving the last one in
        // the file short by the whole fee. The same arithmetic the admin remittance screen uses.
        var expected = open.Sum(collection => collection.CollectedAmount ?? collection.Amount);
        var remitted = 0;

        foreach (var collection in open)
        {
            var owed = collection.CollectedAmount ?? collection.Amount;

            var share = amount is { } total && expected > 0m
                ? Math.Round(total * (owed / expected), 4, MidpointRounding.AwayFromZero)
                : owed;

            if (!collection.Remit(share, reference, remittedAt))
            {
                continue;
            }

            events.CashRecorded(collection);
            remitted++;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(remitted);
    }

    private IQueryable<CodCollection> Query() => context.CodCollections.IgnoreQueryFilters();

    private static CodCollectionView Project(CodCollection collection)
        => new(
            collection.Id,
            collection.OrderId,
            collection.SubOrderId,
            collection.ShipmentId,
            collection.Amount,
            collection.CollectedAmount,
            collection.RemittedAmount,
            collection.CurrencyCode,
            collection.Status.ToString());
}
