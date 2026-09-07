using FluentValidation;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure;
using KlaraHome.Modules.Shipping.Infrastructure.Courier;
using KlaraHome.Modules.Shipping.Infrastructure.Fulfilment;
using KlaraHome.Modules.Shipping.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Shipping.Application.Ndr;

/// <summary>Lists failed delivery attempts.</summary>
/// <param name="Action">Filter by what was decided. <c>Pending</c> is the queue.</param>
/// <param name="VendorId">Filter to one seller. Ignored for a seller, who sees only their own.</param>
/// <param name="ReasonCode">Filter by why the delivery failed.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListNdrQuery(
    NdrAction? Action,
    Guid? VendorId,
    string? ReasonCode,
    string? Cursor,
    int? Size) : IQuery<PagedResult<NdrResponse>>;

/// <summary>
/// Decides what happens to a parcel that could not be delivered.
/// </summary>
/// <remarks>
/// The four real answers: try again, try again on a date the shopper named, fix the address and try
/// again, or give up and send it back. A reattempt is asked of the courier; a return to origin is
/// asked of the courier and moves the parcel here, because from that moment the goods are coming
/// back and the order has to say so.
/// </remarks>
/// <param name="NdrId">The report.</param>
/// <param name="Action">What to do.</param>
/// <param name="Remark">What the operator wants recorded.</param>
/// <param name="RescheduledFor">The date the shopper asked for, when they asked for one.</param>
internal sealed record ActionNdrCommand(
    Guid NdrId,
    NdrAction Action,
    string? Remark,
    DateTimeOffset? RescheduledFor) : ICommand<NdrResponse>;

/// <summary>Validates a decision.</summary>
internal sealed class ActionNdrValidator : AbstractValidator<ActionNdrCommand>
{
    public ActionNdrValidator()
    {
        RuleFor(command => command.Action).IsInEnum();
        RuleFor(command => command.Remark).MaximumLength(500);
    }
}

/// <summary>Reads the failed-delivery queue.</summary>
/// <param name="context">The Shipping data context.</param>
/// <param name="scope">Confines a seller to their own reports.</param>
/// <param name="options">Supplies the page ceiling.</param>
internal sealed class ListNdrQueryHandler(
    ShippingDbContext context,
    ShippingScope scope,
    IOptions<ShippingOptions> options) : IQueryHandler<ListNdrQuery, PagedResult<NdrResponse>>
{
    public async Task<Result<PagedResult<NdrResponse>>> HandleAsync(
        ListNdrQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Math.Min(Cursor.NormalizeSize(query.Size), options.Value.MaxPageSize);
        var rows = context.NdrRecords.AsNoTracking().AsQueryable();

        // The queue, by default. An operator opening this screen wants the questions nobody has
        // answered, not a history of every failed attempt the platform has ever seen.
        var action = query.Action ?? NdrAction.Pending;

        rows = rows.Where(record => record.Action == action);

        if (!scope.IsVendor && query.VendorId is { } vendorId)
        {
            rows = rows.Where(record => record.VendorId == vendorId);
        }

        if (Enum.TryParse<NdrReasonCode>(query.ReasonCode, ignoreCase: true, out var reason))
        {
            rows = rows.Where(record => record.ReasonCode == reason);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(record => record.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(record => record.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var items = page.Take(size).Select(ShippingProjection.ToNdr).ToArray();
        var next = hasMore && items.Length > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<NdrResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>Works one report.</summary>
/// <param name="context">The Shipping data context.</param>
/// <param name="providers">Tells the courier what was decided.</param>
/// <param name="workflow">Moves the parcel when the decision is to send it back.</param>
/// <param name="scope">Records who decided.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class ActionNdrCommandHandler(
    ShippingDbContext context,
    ShippingProviderRegistry providers,
    ShipmentWorkflow workflow,
    ShippingScope scope,
    IClock clock) : ICommandHandler<ActionNdrCommand, NdrResponse>
{
    public async Task<Result<NdrResponse>> HandleAsync(
        ActionNdrCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var action = command.Action;

        // Pending is the state a report is in before anybody has decided, not a decision. It is an
        // enum in the contract now, so this is the only refusal left on this path.
        if (action == NdrAction.Pending)
        {
            return Result.Failure<NdrResponse>(
                ShippingErrors.InvalidRule("'Pending' is not something that can be done about a failed "
                                           + "delivery: it is what the report says before somebody decides."));
        }

        var record = await context.NdrRecords
            .FirstOrDefaultAsync(candidate => candidate.Id == command.NdrId, cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
        {
            return Result.Failure<NdrResponse>(ShippingErrors.NotFound("delivery report"));
        }

        if (!record.IsOpen)
        {
            return Result.Failure<NdrResponse>(ShippingErrors.NdrNotOpen);
        }

        var shipment = await context.Shipments
            .Include(candidate => candidate.Lines)
            .FirstOrDefaultAsync(candidate => candidate.Id == record.ShipmentId, cancellationToken)
            .ConfigureAwait(false);

        if (shipment is null)
        {
            return Result.Failure<NdrResponse>(ShippingErrors.NotFound("parcel"));
        }

        var now = clock.UtcNow;

        // A return to origin is the one decision that moves the parcel. The others are instructions
        // to the courier, and the parcel's own state does not change until they scan something.
        if (action == NdrAction.ReturnToOrigin)
        {
            var scan = new CourierScan(
                $"ndr:{record.Id:N}:rto",
                ShipmentStatus.RtoInitiated,
                "Return to origin",
                Location: null,
                command.Remark ?? "Returned to the seller after failed delivery attempts.",
                NdrReason: null,
                now,
                Raw: null);

            var applied = await workflow.ApplyScanAsync(shipment, scan, cancellationToken).ConfigureAwait(false);

            if (applied.IsFailure)
            {
                return Result.Failure<NdrResponse>(applied.Error);
            }
        }
        else if (shipment.IsBooked && action is NdrAction.Reattempt or NdrAction.Rescheduled)
        {
            // Told to the courier as a fresh collection date. An aggregator with no reattempt call of
            // its own treats this as a pickup request, which is the closest thing it has.
            await providers
                .For(shipment.Provider)
                .SchedulePickupAsync(
                    shipment.ProviderShipmentId,
                    shipment.Awb!,
                    command.RescheduledFor ?? now.AddDays(1),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        record.Decide(action, command.Remark, command.RescheduledFor, scope.ActorId, now);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ShippingProjection.ToNdr(record));
    }
}
