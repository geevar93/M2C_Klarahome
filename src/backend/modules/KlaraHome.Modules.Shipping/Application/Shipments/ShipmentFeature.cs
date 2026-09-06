using FluentValidation;
using KlaraHome.Contracts.Orders;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure;
using KlaraHome.Modules.Shipping.Infrastructure.Fulfilment;
using KlaraHome.Modules.Shipping.Infrastructure.Persistence;
using KlaraHome.Modules.Shipping.Infrastructure.Processing;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Shipping.Application.Shipments;

/// <summary>Units of an order line to put in a parcel.</summary>
/// <param name="OrderLineId">The order line.</param>
/// <param name="Quantity">How many units.</param>
internal sealed record PackedLine(Guid OrderLineId, int Quantity);

/// <summary>A parcel's outside dimensions, in centimetres.</summary>
/// <param name="LengthCm">Longest side.</param>
/// <param name="WidthCm">Width.</param>
/// <param name="HeightCm">Height.</param>
internal sealed record ParcelDimensions(decimal LengthCm, decimal WidthCm, decimal HeightCm);

/// <summary>Lists parcels.</summary>
/// <param name="Status">Filter by where the parcel is.</param>
/// <param name="VendorId">Filter to one seller. Ignored for a seller, who sees only their own.</param>
/// <param name="OrderId">Filter to one order.</param>
/// <param name="SubOrderId">Filter to one seller's part.</param>
/// <param name="Awb">Find one by air waybill.</param>
/// <param name="Q">Search an order or sub-order number.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListShipmentsQuery(
    string? Status,
    Guid? VendorId,
    Guid? OrderId,
    Guid? SubOrderId,
    string? Awb,
    string? Q,
    string? Cursor,
    int? Size) : IQuery<PagedResult<ShipmentSummaryResponse>>;

/// <summary>Reads one parcel in full, with everywhere it has been.</summary>
/// <param name="ShipmentId">The consignment.</param>
internal sealed record GetShipmentQuery(Guid ShipmentId) : IQuery<ShipmentResponse>;

/// <summary>
/// The parcels waiting to be packed, line by line.
/// </summary>
/// <remarks>
/// The seller's morning: everything confirmed and not yet in a box, expanded to one row per item so
/// it can be walked as a picking route rather than read as a list of orders.
/// </remarks>
/// <param name="VendorId">Filter to one seller. Ignored for a seller, who sees only their own.</param>
/// <param name="Size">How many lines to return.</param>
internal sealed record GetPickListQuery(Guid? VendorId, int? Size)
    : IQuery<IReadOnlyList<PickListLineResponse>>;

/// <summary>Puts units into a parcel, replacing whatever was in it.</summary>
/// <param name="ShipmentId">The consignment.</param>
/// <param name="Lines">What to pack. Empty means "everything still unsent".</param>
internal sealed record PackShipmentCommand(Guid ShipmentId, IReadOnlyList<PackedLine> Lines)
    : ICommand<ShipmentResponse>;

/// <summary>Records what the packer weighed and measured.</summary>
/// <param name="ShipmentId">The consignment.</param>
/// <param name="WeightGrams">What the scale said.</param>
/// <param name="Dimensions">The box, when it was measured.</param>
internal sealed record CaptureWeightCommand(
    Guid ShipmentId,
    int WeightGrams,
    ParcelDimensions? Dimensions) : ICommand<ShipmentResponse>;

/// <summary>
/// Creates a parcel for a seller's part and books it, in one call.
/// </summary>
/// <remarks>
/// The route docs/04-api-specification.md §4 names, and the one an admin screen actually posts:
/// lines, weight, dimensions and courier together. It reuses the draft that was opened when the
/// sub-order was confirmed, so packing through this route and packing through the step-by-step one
/// produce the same parcel rather than two.
/// </remarks>
/// <param name="SubOrderId">The seller's part being dispatched.</param>
/// <param name="Lines">What to pack. Empty means "everything still unsent".</param>
/// <param name="WeightGrams">What the scale said.</param>
/// <param name="Dimensions">The box.</param>
/// <param name="Method">The service, or null to use whatever the shopper chose at checkout.</param>
/// <param name="PickupLocationId">The address to collect from, or null for the seller's default.</param>
/// <param name="ManualAwb">An air waybill the operator obtained themselves.</param>
/// <param name="ManualCourier">Who is carrying it, for a hand-booking.</param>
internal sealed record CreateShipmentCommand(
    Guid SubOrderId,
    IReadOnlyList<PackedLine> Lines,
    int WeightGrams,
    ParcelDimensions? Dimensions,
    string? Method,
    Guid? PickupLocationId,
    string? ManualAwb,
    string? ManualCourier) : ICommand<ShipmentResponse>;

/// <summary>Books a packed parcel with a courier.</summary>
/// <param name="ShipmentId">The consignment.</param>
/// <param name="Method">The service, or null to use whatever the shopper chose at checkout.</param>
/// <param name="PickupLocationId">The address to collect from, or null for the seller's default.</param>
/// <param name="ManualAwb">An air waybill the operator obtained themselves.</param>
/// <param name="ManualCourier">Who is carrying it, for a hand-booking.</param>
internal sealed record BookShipmentCommand(
    Guid ShipmentId,
    string? Method,
    Guid? PickupLocationId,
    string? ManualAwb,
    string? ManualCourier) : ICommand<ShipmentResponse>;

/// <summary>Asks the courier to collect.</summary>
/// <param name="ShipmentId">The consignment.</param>
/// <param name="PickupAt">When the parcel will be ready, or null for tomorrow.</param>
internal sealed record SchedulePickupCommand(Guid ShipmentId, DateTimeOffset? PickupAt)
    : ICommand<ShipmentResponse>;

/// <summary>Records that the courier has taken the parcel.</summary>
/// <param name="ShipmentId">The consignment.</param>
/// <param name="At">When, or null for now.</param>
internal sealed record MarkDispatchedCommand(Guid ShipmentId, DateTimeOffset? At)
    : ICommand<ShipmentResponse>;

/// <summary>Calls off a parcel the courier has not collected.</summary>
/// <param name="ShipmentId">The consignment.</param>
/// <param name="Reason">Why.</param>
internal sealed record CancelShipmentCommand(Guid ShipmentId, string? Reason) : ICommand<ShipmentResponse>;

/// <summary>Re-reads a parcel from its courier and applies what they say.</summary>
/// <param name="ShipmentId">The consignment.</param>
internal sealed record SyncTrackingCommand(Guid ShipmentId) : ICommand<ShipmentResponse>;

/// <summary>
/// Records a movement an operator learned about outside the platform.
/// </summary>
/// <remarks>
/// The other half of hand-booking: a seller who rang the courier and was told the parcel is out for
/// delivery can say so, and the order timeline, the shopper's tracking and the return window all
/// follow exactly as they would from a webhook. It goes through the same workflow, which is what
/// makes it a movement rather than an edit.
/// </remarks>
/// <param name="ShipmentId">The consignment.</param>
/// <param name="Status">Where the parcel now is.</param>
/// <param name="Remark">What the courier said.</param>
/// <param name="OccurredAt">When, or null for now.</param>
internal sealed record RecordTrackingCommand(
    Guid ShipmentId,
    string Status,
    string? Remark,
    DateTimeOffset? OccurredAt) : ICommand<ShipmentResponse>;

/// <summary>Validates a weight capture.</summary>
internal sealed class CaptureWeightValidator : AbstractValidator<CaptureWeightCommand>
{
    public CaptureWeightValidator()
        => RuleFor(command => command.WeightGrams).InclusiveBetween(1, 500_000);
}

/// <summary>Validates a create-and-book.</summary>
internal sealed class CreateShipmentValidator : AbstractValidator<CreateShipmentCommand>
{
    public CreateShipmentValidator()
    {
        RuleFor(command => command.SubOrderId).NotEmpty();
        RuleFor(command => command.WeightGrams).InclusiveBetween(1, 500_000);
        RuleForEach(command => command.Lines).Must(line => line.Quantity > 0);
    }
}

/// <summary>Validates a hand-recorded movement.</summary>
internal sealed class RecordTrackingValidator : AbstractValidator<RecordTrackingCommand>
{
    public RecordTrackingValidator()
    {
        RuleFor(command => command.Status).NotEmpty().MaximumLength(24);
        RuleFor(command => command.Remark).MaximumLength(500);
    }
}

/// <summary>Lists parcels, newest first.</summary>
/// <param name="context">The Shipping data context.</param>
/// <param name="scope">Confines a seller to their own parcels.</param>
/// <param name="options">Supplies the page ceiling.</param>
internal sealed class ListShipmentsQueryHandler(
    ShippingDbContext context,
    ShippingScope scope,
    IOptions<ShippingOptions> options)
    : IQueryHandler<ListShipmentsQuery, PagedResult<ShipmentSummaryResponse>>
{
    public async Task<Result<PagedResult<ShipmentSummaryResponse>>> HandleAsync(
        ListShipmentsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Math.Min(Cursor.NormalizeSize(query.Size), options.Value.MaxPageSize);

        // The vendor query filter already confines a seller. The explicit filter is for platform
        // staff narrowing to one seller, which is why it is ignored when the caller is a seller.
        var rows = context.Shipments.AsNoTracking().AsQueryable();

        if (Enum.TryParse<ShipmentStatus>(query.Status, ignoreCase: true, out var status))
        {
            rows = rows.Where(shipment => shipment.Status == status);
        }

        if (!scope.IsVendor && query.VendorId is { } vendorId)
        {
            rows = rows.Where(shipment => shipment.VendorId == vendorId);
        }

        if (query.OrderId is { } orderId)
        {
            rows = rows.Where(shipment => shipment.OrderId == orderId);
        }

        if (query.SubOrderId is { } subOrderId)
        {
            rows = rows.Where(shipment => shipment.SubOrderId == subOrderId);
        }

        if (!string.IsNullOrWhiteSpace(query.Awb))
        {
            rows = rows.Where(shipment => shipment.Awb == query.Awb.Trim());
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();

            rows = rows.Where(shipment =>
                shipment.OrderNumber == term || shipment.SubOrderNumber == term);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(shipment => shipment.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(shipment => shipment.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var items = page.Take(size).Select(ShippingProjection.ToSummary).ToArray();
        var next = hasMore && items.Length > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<ShipmentSummaryResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>Reads one parcel with its history.</summary>
/// <param name="context">The Shipping data context.</param>
internal sealed class GetShipmentQueryHandler(ShippingDbContext context)
    : IQueryHandler<GetShipmentQuery, ShipmentResponse>
{
    public async Task<Result<ShipmentResponse>> HandleAsync(
        GetShipmentQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var shipment = await context.Shipments
            .AsNoTracking()
            .Include(candidate => candidate.Lines)
            .FirstOrDefaultAsync(candidate => candidate.Id == query.ShipmentId, cancellationToken)
            .ConfigureAwait(false);

        if (shipment is null)
        {
            return Result.Failure<ShipmentResponse>(ShippingErrors.NotFound("parcel"));
        }

        var tracking = await ShipmentLoader
            .TrackingAsync(context, shipment.Id, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(ShippingProjection.ToShipment(shipment, tracking));
    }
}

/// <summary>Builds the picking route.</summary>
/// <param name="context">The Shipping data context.</param>
/// <param name="orders">Supplies the dispatch deadline, which is what the list is sorted by.</param>
/// <param name="scope">Confines a seller to their own parcels.</param>
/// <param name="options">Supplies the page ceiling.</param>
internal sealed class GetPickListQueryHandler(
    ShippingDbContext context,
    IOrderFulfilment orders,
    ShippingScope scope,
    IOptions<ShippingOptions> options)
    : IQueryHandler<GetPickListQuery, IReadOnlyList<PickListLineResponse>>
{
    public async Task<Result<IReadOnlyList<PickListLineResponse>>> HandleAsync(
        GetPickListQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Math.Min(Cursor.NormalizeSize(query.Size), options.Value.MaxPageSize);
        var rows = context.Shipments
            .AsNoTracking()
            .Include(shipment => shipment.Lines)
            .Where(shipment => shipment.Status == ShipmentStatus.Draft);

        if (!scope.IsVendor && query.VendorId is { } vendorId)
        {
            rows = rows.Where(shipment => shipment.VendorId == vendorId);
        }

        var drafts = await rows
            .OrderBy(shipment => shipment.CreatedAt)
            .Take(size)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (drafts.Count == 0)
        {
            return Result.Success<IReadOnlyList<PickListLineResponse>>([]);
        }

        // One read for the whole list rather than one per parcel. The deadline is the ordering
        // module's and is the only thing here this module does not already know.
        var due = await orders
            .GetManyAsync([.. drafts.Select(shipment => shipment.SubOrderId)], cancellationToken)
            .ConfigureAwait(false);

        var deadlines = due.ToDictionary(view => view.SubOrderId, view => view.DispatchDueAt);

        return Result.Success<IReadOnlyList<PickListLineResponse>>(
        [
            .. drafts
                .SelectMany(
                    shipment => shipment.Lines,
                    (shipment, line) => new PickListLineResponse(
                        shipment.Id,
                        shipment.OrderNumber,
                        shipment.SubOrderNumber,
                        line.Sku,
                        line.Name,
                        line.Quantity,
                        shipment.DestinationPincode,
                        deadlines.GetValueOrDefault(shipment.SubOrderId)))

                // Soonest deadline first, then by item, which is what turns a list into a route: a
                // picker walks the shelves once rather than once per order.
                .OrderBy(line => line.DispatchDueAt ?? DateTimeOffset.MaxValue)
                .ThenBy(line => line.Sku),
        ]);
    }
}

/// <summary>Puts units into a parcel.</summary>
/// <param name="context">The Shipping data context.</param>
/// <param name="orders">Supplies what the seller's part actually contains.</param>
internal sealed class PackShipmentCommandHandler(ShippingDbContext context, IOrderFulfilment orders)
    : ICommandHandler<PackShipmentCommand, ShipmentResponse>
{
    public async Task<Result<ShipmentResponse>> HandleAsync(
        PackShipmentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var shipment = await ShipmentLoader
            .ForUpdateAsync(context, command.ShipmentId, cancellationToken)
            .ConfigureAwait(false);

        if (shipment is null)
        {
            return Result.Failure<ShipmentResponse>(ShippingErrors.NotFound("parcel"));
        }

        var packed = await ShipmentPacker
            .PackAsync(context, orders, shipment, command.Lines, cancellationToken)
            .ConfigureAwait(false);

        if (packed.IsFailure)
        {
            return Result.Failure<ShipmentResponse>(packed.Error);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ShippingProjection.ToShipment(shipment, []));
    }
}

/// <summary>Records the weight and the box.</summary>
/// <param name="context">The Shipping data context.</param>
internal sealed class CaptureWeightCommandHandler(ShippingDbContext context)
    : ICommandHandler<CaptureWeightCommand, ShipmentResponse>
{
    public async Task<Result<ShipmentResponse>> HandleAsync(
        CaptureWeightCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var shipment = await ShipmentLoader
            .ForUpdateAsync(context, command.ShipmentId, cancellationToken)
            .ConfigureAwait(false);

        if (shipment is null)
        {
            return Result.Failure<ShipmentResponse>(ShippingErrors.NotFound("parcel"));
        }

        var box = command.Dimensions;

        if (!shipment.CaptureWeight(
                command.WeightGrams,
                box?.LengthCm ?? 0m,
                box?.WidthCm ?? 0m,
                box?.HeightCm ?? 0m))
        {
            return Result.Failure<ShipmentResponse>(ShippingErrors.AlreadyBooked);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ShippingProjection.ToShipment(shipment, []));
    }
}

/// <summary>Packs, weighs and books in one call.</summary>
/// <param name="context">The Shipping data context.</param>
/// <param name="orders">Supplies what the seller's part contains, and its state.</param>
/// <param name="booker">Does the booking.</param>
internal sealed class CreateShipmentCommandHandler(
    ShippingDbContext context,
    IOrderFulfilment orders,
    ShipmentBooker booker) : ICommandHandler<CreateShipmentCommand, ShipmentResponse>
{
    public async Task<Result<ShipmentResponse>> HandleAsync(
        CreateShipmentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var view = await orders.GetAsync(command.SubOrderId, cancellationToken).ConfigureAwait(false);

        if (view.IsFailure)
        {
            return Result.Failure<ShipmentResponse>(ShippingErrors.NotFound("order"));
        }

        var order = view.Value;

        if (!Dispatchable(order.Status))
        {
            return Result.Failure<ShipmentResponse>(ShippingErrors.NotDispatchable(order.Status));
        }

        // The draft opened when the sub-order was confirmed, reused rather than duplicated. A second
        // parcel is only created for a genuine partial shipment, which is the one still unbooked.
        var shipment = await context.Shipments
            .Include(candidate => candidate.Lines)
            .Where(candidate => candidate.SubOrderId == command.SubOrderId
                                && !candidate.IsReturn
                                && candidate.Status == ShipmentStatus.Draft)
            .OrderBy(candidate => candidate.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (shipment is null)
        {
            shipment = Shipment.Draft(
                order.OrderId,
                order.OrderNumber,
                order.SubOrderId,
                order.SubOrderNumber,
                order.VendorId,
                order.CustomerId);

            shipment.Address(
                order.Destination.Pincode,
                order.Destination.StateId,
                order.IsCod ? order.AmountDueAtDelivery : null,
                order.DeclaredValue,
                freightCharged: 0m,
                order.CurrencyCode);

            context.Shipments.Add(shipment);
        }

        var packed = await ShipmentPacker
            .PackAsync(context, orders, shipment, command.Lines, cancellationToken)
            .ConfigureAwait(false);

        if (packed.IsFailure)
        {
            return Result.Failure<ShipmentResponse>(packed.Error);
        }

        var box = command.Dimensions;

        shipment.CaptureWeight(
            command.WeightGrams,
            box?.LengthCm ?? 0m,
            box?.WidthCm ?? 0m,
            box?.HeightCm ?? 0m);

        var booked = await booker
            .BookAsync(
                shipment,
                new ShipmentBooker.BookingIntent(
                    ShipmentLoader.MethodOrNull(command.Method),
                    command.PickupLocationId,
                    command.ManualAwb,
                    command.ManualCourier),
                cancellationToken)
            .ConfigureAwait(false);

        if (booked.IsFailure)
        {
            // The parcel is saved as a draft even though the booking failed: the packing is real work
            // and losing it would make an aggregator outage into an afternoon repacking boxes.
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result.Failure<ShipmentResponse>(booked.Error);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ShippingProjection.ToShipment(shipment, []));
    }

    /// <summary>Whether a seller's part is at a point where a parcel may be booked for it.</summary>
    /// <remarks>
    /// Read from the ordering module's own vocabulary rather than duplicated as an enum here. A
    /// confirmed order can be packed; anything earlier has not been paid for and anything later has
    /// already gone.
    /// </remarks>
    private static bool Dispatchable(string status)
        => status is "Confirmed" or "Processing" or "Packed";
}

/// <summary>Books a packed parcel.</summary>
/// <param name="context">The Shipping data context.</param>
/// <param name="booker">Does the booking.</param>
internal sealed class BookShipmentCommandHandler(ShippingDbContext context, ShipmentBooker booker)
    : ICommandHandler<BookShipmentCommand, ShipmentResponse>
{
    public async Task<Result<ShipmentResponse>> HandleAsync(
        BookShipmentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var shipment = await ShipmentLoader
            .ForUpdateAsync(context, command.ShipmentId, cancellationToken)
            .ConfigureAwait(false);

        if (shipment is null)
        {
            return Result.Failure<ShipmentResponse>(ShippingErrors.NotFound("parcel"));
        }

        var booked = await booker
            .BookAsync(
                shipment,
                new ShipmentBooker.BookingIntent(
                    ShipmentLoader.MethodOrNull(command.Method),
                    command.PickupLocationId,
                    command.ManualAwb,
                    command.ManualCourier),
                cancellationToken)
            .ConfigureAwait(false);

        if (booked.IsFailure)
        {
            return Result.Failure<ShipmentResponse>(booked.Error);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ShippingProjection.ToShipment(shipment, []));
    }
}

/// <summary>Asks the courier to collect.</summary>
/// <param name="context">The Shipping data context.</param>
/// <param name="booker">Talks to the courier.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class SchedulePickupCommandHandler(
    ShippingDbContext context,
    ShipmentBooker booker,
    IClock clock) : ICommandHandler<SchedulePickupCommand, ShipmentResponse>
{
    public async Task<Result<ShipmentResponse>> HandleAsync(
        SchedulePickupCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var shipment = await ShipmentLoader
            .ForUpdateAsync(context, command.ShipmentId, cancellationToken)
            .ConfigureAwait(false);

        if (shipment is null)
        {
            return Result.Failure<ShipmentResponse>(ShippingErrors.NotFound("parcel"));
        }

        var scheduled = await booker
            .SchedulePickupAsync(shipment, command.PickupAt ?? clock.UtcNow.AddDays(1), cancellationToken)
            .ConfigureAwait(false);

        if (scheduled.IsFailure)
        {
            return Result.Failure<ShipmentResponse>(scheduled.Error);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ShippingProjection.ToShipment(shipment, []));
    }
}

/// <summary>Records that the courier has taken the parcel.</summary>
/// <param name="context">The Shipping data context.</param>
/// <param name="workflow">Tells the order it has shipped.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class MarkDispatchedCommandHandler(
    ShippingDbContext context,
    ShipmentWorkflow workflow,
    IClock clock) : ICommandHandler<MarkDispatchedCommand, ShipmentResponse>
{
    public async Task<Result<ShipmentResponse>> HandleAsync(
        MarkDispatchedCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var shipment = await ShipmentLoader
            .ForUpdateAsync(context, command.ShipmentId, cancellationToken)
            .ConfigureAwait(false);

        if (shipment is null)
        {
            return Result.Failure<ShipmentResponse>(ShippingErrors.NotFound("parcel"));
        }

        var dispatched = await workflow
            .RecordDispatchAsync(shipment, command.At ?? clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        if (dispatched.IsFailure)
        {
            return Result.Failure<ShipmentResponse>(dispatched.Error);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ShippingProjection.ToShipment(shipment, []));
    }
}

/// <summary>Calls off a parcel.</summary>
/// <param name="context">The Shipping data context.</param>
/// <param name="providers">Tells the courier, where one was told about it.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class CancelShipmentCommandHandler(
    ShippingDbContext context,
    Infrastructure.Courier.ShippingProviderRegistry providers,
    IClock clock) : ICommandHandler<CancelShipmentCommand, ShipmentResponse>
{
    public async Task<Result<ShipmentResponse>> HandleAsync(
        CancelShipmentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var shipment = await ShipmentLoader
            .ForUpdateAsync(context, command.ShipmentId, cancellationToken)
            .ConfigureAwait(false);

        if (shipment is null)
        {
            return Result.Failure<ShipmentResponse>(ShippingErrors.NotFound("parcel"));
        }

        // The courier is told first. Cancelling our record of a consignment a courier still expects
        // is how a driver arrives for a parcel nobody has packed.
        if (shipment.IsBooked)
        {
            var told = await providers
                .For(shipment.Provider)
                .CancelShipmentAsync(shipment.ProviderShipmentId, shipment.Awb!, cancellationToken)
                .ConfigureAwait(false);

            if (told.IsFailure)
            {
                return Result.Failure<ShipmentResponse>(told.Error);
            }
        }

        if (!shipment.Advance(ShipmentStatus.Cancelled, clock.UtcNow, command.Reason))
        {
            return Result.Failure<ShipmentResponse>(
                ShippingErrors.InvalidTransition(shipment.Status, ShipmentStatus.Cancelled));
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ShippingProjection.ToShipment(shipment, []));
    }
}

/// <summary>Re-reads a parcel from its courier.</summary>
/// <param name="context">The Shipping data context.</param>
/// <param name="synchroniser">Asks the courier and applies what they say.</param>
internal sealed class SyncTrackingCommandHandler(
    ShippingDbContext context,
    TrackingSynchroniser synchroniser) : ICommandHandler<SyncTrackingCommand, ShipmentResponse>
{
    public async Task<Result<ShipmentResponse>> HandleAsync(
        SyncTrackingCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var shipment = await ShipmentLoader
            .ForUpdateAsync(context, command.ShipmentId, cancellationToken)
            .ConfigureAwait(false);

        if (shipment is null)
        {
            return Result.Failure<ShipmentResponse>(ShippingErrors.NotFound("parcel"));
        }

        var synced = await synchroniser.SyncAsync(shipment, cancellationToken).ConfigureAwait(false);

        if (synced.IsFailure)
        {
            return Result.Failure<ShipmentResponse>(synced.Error);
        }

        var tracking = await ShipmentLoader
            .TrackingAsync(context, shipment.Id, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(ShippingProjection.ToShipment(shipment, tracking));
    }
}

/// <summary>Records a movement an operator learned about outside the platform.</summary>
/// <param name="context">The Shipping data context.</param>
/// <param name="workflow">The single place a parcel moves.</param>
/// <param name="scope">Attributes the entry to whoever recorded it.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class RecordTrackingCommandHandler(
    ShippingDbContext context,
    ShipmentWorkflow workflow,
    ShippingScope scope,
    IClock clock) : ICommandHandler<RecordTrackingCommand, ShipmentResponse>
{
    public async Task<Result<ShipmentResponse>> HandleAsync(
        RecordTrackingCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!Enum.TryParse<ShipmentStatus>(command.Status, ignoreCase: true, out var status))
        {
            return Result.Failure<ShipmentResponse>(
                ShippingErrors.InvalidRule($"'{command.Status}' is not a parcel status."));
        }

        var shipment = await ShipmentLoader
            .ForUpdateAsync(context, command.ShipmentId, cancellationToken)
            .ConfigureAwait(false);

        if (shipment is null)
        {
            return Result.Failure<ShipmentResponse>(ShippingErrors.NotFound("parcel"));
        }

        var at = command.OccurredAt ?? clock.UtcNow;
        var wasAt = shipment.Status;

        // A synthetic id built from who recorded it, so two operators entering the same update do
        // not produce two timeline entries — and so a later courier scan of the same movement is
        // still recognised as its own event rather than swallowed.
        var scan = new Infrastructure.Courier.CourierScan(
            $"manual:{scope.ActorId:N}:{TrackingEvent.SyntheticId(command.Status, at)}",
            status,
            command.Status,
            Location: null,
            command.Remark,
            status == ShipmentStatus.Exception ? NdrReasonCode.Other : null,
            at,
            Raw: null);

        var applied = await workflow.ApplyScanAsync(shipment, scan, cancellationToken).ConfigureAwait(false);

        if (applied.IsFailure)
        {
            return Result.Failure<ShipmentResponse>(applied.Error);
        }

        if (shipment.Status == wasAt && wasAt != status)
        {
            return Result.Failure<ShipmentResponse>(ShippingErrors.InvalidTransition(wasAt, status));
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var tracking = await ShipmentLoader
            .TrackingAsync(context, shipment.Id, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(ShippingProjection.ToShipment(shipment, tracking));
    }
}

/// <summary>The loads and lookups every parcel handler needs, written once.</summary>
internal static class ShipmentLoader
{
    /// <summary>Loads a parcel for update, with its lines.</summary>
    /// <remarks>
    /// Tracked, and through the query filters: a seller reaching for another seller's parcel gets
    /// nothing, and the handler answers the same 404 an invented id gets.
    /// </remarks>
    /// <param name="context">The Shipping data context.</param>
    /// <param name="shipmentId">The consignment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task<Shipment?> ForUpdateAsync(
        ShippingDbContext context,
        Guid shipmentId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Shipments
            .Include(shipment => shipment.Lines)
            .FirstOrDefaultAsync(shipment => shipment.Id == shipmentId, cancellationToken);
    }

    /// <summary>The scans on a parcel, newest first.</summary>
    /// <param name="context">The Shipping data context.</param>
    /// <param name="shipmentId">The consignment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<IReadOnlyList<TrackingEvent>> TrackingAsync(
        ShippingDbContext context,
        Guid shipmentId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        return await context.TrackingEvents
            .AsNoTracking()
            .Where(entry => entry.ShipmentId == shipmentId)
            .OrderByDescending(entry => entry.OccurredAt)
            .Take(100)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The service a caller named, or null to fall back to the shopper's own choice.</summary>
    /// <param name="method">The service name, if the caller gave one.</param>
    public static ShippingMethod? MethodOrNull(string? method)
        => Enum.TryParse<ShippingMethod>(method, ignoreCase: true, out var parsed) ? parsed : null;
}

/// <summary>
/// Decides what actually goes in a box.
/// </summary>
/// <remarks>
/// <para>
/// The rule this enforces is the one that makes a partial shipment safe: <b>no more units may be
/// packed than the order has left to send</b>. What is left is the sub-order's live quantity less
/// everything already packed into other parcels, which is why it counts across the whole schema
/// rather than looking at this parcel alone.
/// </para>
/// <para>
/// An empty list means "everything still unsent", which is what a packer means nine times out of
/// ten and what the confirmation-time draft is already filled with.
/// </para>
/// </remarks>
internal static class ShipmentPacker
{
    /// <summary>Replaces a parcel's contents.</summary>
    /// <param name="context">The Shipping data context.</param>
    /// <param name="orders">Supplies what the seller's part contains.</param>
    /// <param name="shipment">The consignment, loaded for update.</param>
    /// <param name="lines">What to pack, or empty for everything still unsent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<Result> PackAsync(
        ShippingDbContext context,
        IOrderFulfilment orders,
        Shipment shipment,
        IReadOnlyList<PackedLine> lines,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(shipment);
        ArgumentNullException.ThrowIfNull(lines);

        if (!shipment.Unpack())
        {
            return Result.Failure(ShippingErrors.AlreadyBooked);
        }

        var view = await orders.GetAsync(shipment.SubOrderId, cancellationToken).ConfigureAwait(false);

        if (view.IsFailure)
        {
            return Result.Failure(ShippingErrors.OrderUnavailable(view.Error.Message));
        }

        var order = view.Value;

        // What other parcels have already taken, so two partial shipments cannot between them send
        // more than was ordered. This parcel's own lines are excluded because they have just been
        // cleared and are about to be replaced.
        var alreadyPacked = await context.ShipmentLines
            .AsNoTracking()
            .Where(line => line.ShipmentId != shipment.Id)
            .Join(
                context.Shipments
                    .AsNoTracking()
                    .Where(other => other.SubOrderId == shipment.SubOrderId
                                    && other.Status != ShipmentStatus.Cancelled),
                line => line.ShipmentId,
                other => other.Id,
                (line, _) => line)
            .GroupBy(line => line.OrderLineId)
            .Select(group => new { OrderLineId = group.Key, Quantity = group.Sum(line => line.Quantity) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var packed = alreadyPacked.ToDictionary(row => row.OrderLineId, row => row.Quantity);
        var wanted = lines.ToDictionary(line => line.OrderLineId, line => line.Quantity);

        foreach (var line in order.Lines)
        {
            var remaining = line.Quantity - packed.GetValueOrDefault(line.OrderLineId);

            if (remaining <= 0)
            {
                continue;
            }

            var quantity = wanted.Count == 0 ? remaining : wanted.GetValueOrDefault(line.OrderLineId);

            if (quantity <= 0)
            {
                continue;
            }

            if (quantity > remaining)
            {
                return Result.Failure(ShippingErrors.TooManyUnits(line.Sku, remaining));
            }

            // The value is apportioned to the units in this box, so a partial shipment declares what
            // it actually carries rather than the whole line's worth.
            var value = line.Quantity > 0
                ? Math.Round(line.LineTotal / line.Quantity * quantity, 2, MidpointRounding.AwayFromZero)
                : 0m;

            shipment.Pack(ShipmentLine.Pack(
                shipment.Id,
                line.OrderLineId,
                line.Sku,
                line.Name,
                quantity,
                line.UnitWeightGrams,
                value));
        }

        return shipment.Lines.Count == 0
            ? Result.Failure(ShippingErrors.NothingToShip)
            : Result.Success();
    }
}
