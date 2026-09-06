using KlaraHome.Contracts.Inventory;
using KlaraHome.Contracts.Orders;
using KlaraHome.Modules.Returns.Domain;
using KlaraHome.Modules.Returns.Infrastructure.Events;
using KlaraHome.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Returns.Infrastructure.Processing;

/// <summary>What an inspector concluded about one line.</summary>
/// <param name="ReturnLineId">The line inspected.</param>
/// <param name="QuantityAccepted">How many units passed.</param>
/// <param name="Disposition">What becomes of them, or null for the store's default.</param>
/// <param name="Note">What the inspector wrote about this line.</param>
internal sealed record LineVerdict(
    Guid ReturnLineId,
    int QuantityAccepted,
    ReturnDisposition? Disposition,
    string? Note);

/// <summary>
/// Records what quality control decided, and makes it true of the goods and of the sale.
/// </summary>
/// <remarks>
/// <para>
/// Three things follow from an inspection and they must happen together or not at all: the verdict
/// is written on the return, the units are recorded as returned against the frozen order lines, and
/// the stock moves. Doing them in one place is what stops the commonest returns defect there is —
/// goods put back on sale for a return the order still thinks is outstanding, so the same unit can
/// be returned twice.
/// </para>
/// <para>
/// The order is deliberate. The sale is told first, because
/// <c>orders.order_lines.quantity_returned</c> is the platform's only count of what has gone back
/// and is what makes a second return of the same unit impossible; stock moves second, because a unit
/// back on sale that the order has not accounted for is worse than a unit accounted for that is not
/// yet back on sale.
/// </para>
/// <para>
/// Both downstream calls are idempotent on the return's id, so a redelivered event or a retried
/// request records the units once. That is what lets this run without a distributed transaction it
/// could not have anyway.
/// </para>
/// </remarks>
/// <param name="orders">The seam returned units are recorded through.</param>
/// <param name="stock">The seam units go back on supply through.</param>
/// <param name="workflow">Moves the return to its QC state.</param>
/// <param name="events">Announces the verdict.</param>
/// <param name="logger">Reports what moved.</param>
internal sealed partial class ReturnInspectionService(
    IOrderReturns orders,
    IStockRestock stock,
    ReturnWorkflow workflow,
    ReturnsEventPublisher events,
    ILogger<ReturnInspectionService> logger)
{
    /// <summary>
    /// Records the inspection and acts on it.
    /// </summary>
    /// <param name="request">The RMA, which must have been received.</param>
    /// <param name="passed">Whether the goods were as they should have been.</param>
    /// <param name="notes">What the inspector wrote overall.</param>
    /// <param name="verdicts">
    /// What they concluded per line, or empty to accept everything at the store's default
    /// disposition — which is what the receiving bay's one-click "all good" does.
    /// </param>
    /// <param name="defaultDisposition">What becomes of goods the inspector did not decide about.</param>
    /// <param name="actor">Who is asking.</param>
    /// <param name="actorId">The user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> InspectAsync(
        ReturnRequest request,
        bool passed,
        string? notes,
        IReadOnlyCollection<LineVerdict> verdicts,
        ReturnDisposition defaultDisposition,
        ReturnActor actor,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(verdicts);

        var byLine = verdicts.ToDictionary(verdict => verdict.ReturnLineId);

        foreach (var line in request.Lines)
        {
            if (byLine.TryGetValue(line.Id, out var verdict))
            {
                line.Inspect(
                    verdict.QuantityAccepted,
                    verdict.Disposition ?? defaultDisposition,
                    verdict.Note);

                continue;
            }

            // No verdict for this line. A pass accepts everything the shopper sent; a failure accepts
            // nothing, which is what "the parcel was not what it should have been" means when nobody
            // has said which item was wrong.
            line.Inspect(passed ? line.Quantity : 0, passed ? defaultDisposition : ReturnDisposition.Pending, null);
        }

        request.RecordQc(notes);

        var moved = await workflow
            .TransitionAsync(
                request,
                passed ? ReturnStatus.QcPassed : ReturnStatus.QcFailed,
                actor,
                actorId,
                notes,
                cancellationToken)
            .ConfigureAwait(false);

        if (moved.IsFailure)
        {
            return moved;
        }

        await RecordAgainstOrderAsync(request, cancellationToken).ConfigureAwait(false);
        await MoveStockAsync(request, cancellationToken).ConfigureAwait(false);

        events.QcCompleted(request, request.AcceptedValue);

        return Result.Success();
    }

    /// <summary>
    /// Tells the sale which units came back.
    /// </summary>
    /// <remarks>
    /// A failure here is logged and not propagated, and the reasoning is the same as the workflow's
    /// for a timeline entry: the inspection happened, the goods are on a shelf, and refusing to
    /// record it because the ordering module was briefly unavailable would leave the two further
    /// apart rather than closer. The sweep is what finds a return whose units never landed.
    /// </remarks>
    private async Task RecordAgainstOrderAsync(ReturnRequest request, CancellationToken cancellationToken)
    {
        var units = request.Lines
            .Where(line => line.QuantityAccepted > 0)
            .Select(line => new ReturnedUnits(line.OrderLineId, line.QuantityAccepted))
            .ToArray();

        if (units.Length == 0)
        {
            return;
        }

        var recorded = await orders
            .RecordReturnedAsync(
                request.SubOrderId,
                units,
                $"{request.TotalAccepted} unit(s) were returned under {request.ReturnNumber}.",
                cancellationToken)
            .ConfigureAwait(false);

        if (recorded.IsFailure)
        {
            OrderNotUpdated(logger, request.ReturnNumber, recorded.Error.Message);
            return;
        }

        UnitsRecorded(logger, request.ReturnNumber, recorded.Value);
    }

    /// <summary>
    /// Puts what passed back on supply, and writes off what did not.
    /// </summary>
    /// <remarks>
    /// Quarantined lines are handed over too, and deliberately move nothing: the seam records the
    /// decision so a stock take can account for goods that are physically present and commercially
    /// undecided. A platform that simply omitted them would have units nobody could explain.
    /// </remarks>
    private async Task MoveStockAsync(ReturnRequest request, CancellationToken cancellationToken)
    {
        var units = request.Lines
            .Where(line => line.QuantityAccepted > 0 && line.Disposition != ReturnDisposition.Pending)
            .Select(line => new RestockUnits(
                line.ListingId,
                line.QuantityAccepted,
                Map(line.Disposition)))
            .ToArray();

        if (units.Length == 0)
        {
            return;
        }

        var moved = await stock
            .RestockAsync(
                units,
                RestockReferenceTypes.Return,
                request.Id,
                $"Return {request.ReturnNumber}",
                cancellationToken)
            .ConfigureAwait(false);

        StockMoved(logger, request.ReturnNumber, moved);
    }

    /// <summary>Translates this module's word for a disposition into the inventory seam's.</summary>
    private static RestockDisposition Map(ReturnDisposition disposition)
        => disposition switch
        {
            ReturnDisposition.Restock => RestockDisposition.Restock,
            ReturnDisposition.Scrap => RestockDisposition.Scrap,
            _ => RestockDisposition.Quarantine,
        };

    [LoggerMessage(EventId = 1740, Level = LogLevel.Information,
        Message = "Return {ReturnNumber} recorded {Units} returned unit(s) against its order.")]
    private static partial void UnitsRecorded(ILogger logger, string returnNumber, int units);

    [LoggerMessage(EventId = 1741, Level = LogLevel.Information,
        Message = "Return {ReturnNumber} moved {Units} unit(s) of stock.")]
    private static partial void StockMoved(ILogger logger, string returnNumber, int units);

    [LoggerMessage(EventId = 1742, Level = LogLevel.Error,
        Message = "Return {ReturnNumber} was inspected, but its order could not be told: {Detail}")]
    private static partial void OrderNotUpdated(ILogger logger, string returnNumber, string detail);
}
