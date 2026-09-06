using System.Globalization;
using FluentValidation;
using KlaraHome.Contracts.Media;
using KlaraHome.Contracts.Payments;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure;
using KlaraHome.Modules.Shipping.Infrastructure.Courier;
using KlaraHome.Modules.Shipping.Infrastructure.Documents;
using KlaraHome.Modules.Shipping.Infrastructure.Persistence;
using KlaraHome.Modules.Shipping.Infrastructure.Serviceability;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Shipping.Application.Operations;

/// <summary>Asks what can be delivered to a PIN code.</summary>
/// <param name="Pincode">The six-digit destination.</param>
internal sealed record GetServiceabilityQuery(string Pincode) : IQuery<ServiceabilityResponse>;

/// <summary>Asks a courier about a PIN code and records what they say.</summary>
/// <param name="Pincode">The six-digit destination.</param>
internal sealed record RefreshServiceabilityCommand(string Pincode) : ICommand<ServiceabilityResponse>;

/// <summary>Produces the handover sheet for a batch of parcels.</summary>
/// <param name="ShipmentIds">The parcels going out, or empty for everything ready at this address.</param>
/// <param name="VendorId">The seller handing over. Ignored for a seller, who hands over their own.</param>
/// <param name="PickupLocationId">The address being collected from.</param>
internal sealed record CreateManifestCommand(
    IReadOnlyList<Guid> ShipmentIds,
    Guid? VendorId,
    Guid? PickupLocationId) : ICommand<ManifestResponse>;

/// <summary>Lists handover sheets.</summary>
/// <param name="VendorId">Filter to one seller.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListManifestsQuery(Guid? VendorId, string? Cursor, int? Size)
    : IQuery<PagedResult<ManifestResponse>>;

/// <summary>Lists stored courier webhooks.</summary>
/// <param name="Status">Filter by where processing stands. <c>DeadLettered</c> is the queue.</param>
/// <param name="Awb">Everything about one parcel, however it arrived.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListCourierEventsQuery(string? Status, string? Awb, string? Cursor, int? Size)
    : IQuery<PagedResult<CourierEventResponse>>;

/// <summary>Puts a failed or dead-lettered event back in the queue.</summary>
/// <param name="EventId">The event.</param>
internal sealed record ReplayCourierEventCommand(Guid EventId) : ICommand<CourierEventResponse>;

/// <summary>
/// Records a courier's remittance of the cash they collected.
/// </summary>
/// <remarks>
/// By air waybill, because that is the shape of the file a courier sends: one transfer, and a list
/// of waybills. The money itself is Payments' — this resolves the waybills to parcels and hands the
/// facts across, so there is exactly one ledger of what has been collected and what has not.
/// </remarks>
/// <param name="Awbs">The air waybills the remittance covers.</param>
/// <param name="Reference">The courier's reference — a UTR or a batch number.</param>
/// <param name="Amount">What arrived in total, or null to take each parcel at its face value.</param>
/// <param name="RemittedAt">When it arrived, or null for now.</param>
internal sealed record RecordCourierRemittanceCommand(
    IReadOnlyList<string> Awbs,
    string Reference,
    decimal? Amount,
    DateTimeOffset? RemittedAt) : ICommand<int>;

/// <summary>Validates a remittance.</summary>
internal sealed class RecordCourierRemittanceValidator : AbstractValidator<RecordCourierRemittanceCommand>
{
    public RecordCourierRemittanceValidator()
    {
        RuleFor(command => command.Awbs).NotEmpty();
        RuleFor(command => command.Awbs.Count).LessThanOrEqualTo(500);
        RuleFor(command => command.Reference).NotEmpty().MaximumLength(128);
    }
}

/// <summary>Validates a handover.</summary>
internal sealed class CreateManifestValidator : AbstractValidator<CreateManifestCommand>
{
    public CreateManifestValidator()
        => RuleFor(command => command.ShipmentIds.Count).LessThanOrEqualTo(500);
}

/// <summary>Reads the serviceability cache.</summary>
/// <param name="serviceability">Answers from the cache, never from a courier.</param>
internal sealed class GetServiceabilityQueryHandler(ServiceabilityService serviceability)
    : IQueryHandler<GetServiceabilityQuery, ServiceabilityResponse>
{
    public async Task<Result<ServiceabilityResponse>> HandleAsync(
        GetServiceabilityQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var answer = await serviceability
            .ReadAsync(query.Pincode, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(ShippingProjection.ToServiceability(answer));
    }
}

/// <summary>Asks a courier about a PIN code.</summary>
/// <param name="serviceability">Asks and records.</param>
internal sealed class RefreshServiceabilityCommandHandler(ServiceabilityService serviceability)
    : ICommandHandler<RefreshServiceabilityCommand, ServiceabilityResponse>
{
    public async Task<Result<ServiceabilityResponse>> HandleAsync(
        RefreshServiceabilityCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var answer = await serviceability
            .RefreshAsync(command.Pincode, pickupPincode: null, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(ShippingProjection.ToServiceability(answer));
    }
}

/// <summary>Produces a handover sheet.</summary>
/// <param name="context">The Shipping data context.</param>
/// <param name="providers">Tells the aggregator a batch is being handed over.</param>
/// <param name="pickups">Reads the address being collected from.</param>
/// <param name="vendors">Names the seller on the sheet.</param>
/// <param name="documents">Renders and stores the PDF.</param>
/// <param name="scope">Confines a seller to their own parcels, and records who produced it.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class CreateManifestCommandHandler(
    ShippingDbContext context,
    ShippingProviderRegistry providers,
    IVendorPickupPoints pickups,
    IVendorDirectory vendors,
    IDocumentStore documents,
    ShippingScope scope,
    IClock clock) : ICommandHandler<CreateManifestCommand, ManifestResponse>
{
    public async Task<Result<ManifestResponse>> HandleAsync(
        CreateManifestCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var vendorId = scope.IsVendor ? scope.VendorId : command.VendorId;

        var rows = context.Shipments
            .Include(shipment => shipment.Lines)
            .Where(shipment => shipment.ManifestId == null
                               && shipment.Awb != null
                               && (shipment.Status == ShipmentStatus.Created
                                   || shipment.Status == ShipmentStatus.LabelGenerated
                                   || shipment.Status == ShipmentStatus.PickupScheduled));

        if (command.ShipmentIds.Count > 0)
        {
            var wanted = command.ShipmentIds.Distinct().ToArray();
            rows = rows.Where(shipment => wanted.Contains(shipment.Id));
        }

        if (vendorId is { } seller)
        {
            rows = rows.Where(shipment => shipment.VendorId == seller);
        }

        if (command.PickupLocationId is { } location)
        {
            rows = rows.Where(shipment => shipment.PickupLocationId == location);
        }

        var shipments = await rows.ToListAsync(cancellationToken).ConfigureAwait(false);

        if (shipments.Count == 0)
        {
            return Result.Failure<ManifestResponse>(ShippingErrors.NothingToShip);
        }

        var now = clock.UtcNow;
        var courier = shipments[0].Courier ?? ShippingProviders.Manual;

        // A reference a human can read out over a phone, and unique per tenant by the index. The
        // timestamp is enough: two handovers in the same second at the same address do not happen.
        var reference = string.Create(
            CultureInfo.InvariantCulture,
            $"MF-{now:yyyyMMdd}-{now.ToUnixTimeSeconds() % 100000:D5}");

        var manifest = ShippingManifest.Create(
            reference,
            courier,
            vendorId ?? shipments[0].VendorId,
            command.PickupLocationId ?? shipments[0].PickupLocationId,
            shipments.Count,
            shipments.Sum(shipment => shipment.WeightGrams),
            scope.ActorId,
            now);

        context.Manifests.Add(manifest);

        foreach (var shipment in shipments)
        {
            shipment.AttachManifest(manifest.Id);
        }

        var told = await providers
            .For(courier)
            .GenerateManifestAsync([.. shipments.Select(shipment => shipment.Awb!)], cancellationToken)
            .ConfigureAwait(false);

        // The aggregator's own id where it issued one. A refusal is not fatal: the sheet is this
        // platform's document, and the parcels are handed over on paper whatever an API says.
        if (told.IsSuccess)
        {
            manifest.Attach(fileId: null, told.Value.ProviderManifestId);
        }

        await AttachDocumentAsync(manifest, shipments, cancellationToken).ConfigureAwait(false);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ShippingProjection.ToManifest(manifest));
    }

    private async Task AttachDocumentAsync(
        ShippingManifest manifest,
        IReadOnlyList<Shipment> shipments,
        CancellationToken cancellationToken)
    {
        try
        {
            var pickup = manifest.VendorId is { } vendorId
                ? await pickups
                    .FindAsync(vendorId, manifest.PickupLocationId, cancellationToken)
                    .ConfigureAwait(false)
                : null;

            var seller = manifest.VendorId is { } id
                ? await vendors.FindAsync(id, cancellationToken).ConfigureAwait(false)
                : null;

            var rendered = await documents
                .RenderAsync(
                    ShippingDocumentBuilder.Manifest(manifest, shipments, pickup, seller?.DisplayName),
                    $"manifest-{manifest.Reference}.pdf",
                    "ShippingManifest",
                    manifest.Id,
                    cancellationToken)
                .ConfigureAwait(false);

            manifest.Attach(rendered.Id, providerManifestId: null);
        }
        catch (InvalidOperationException)
        {
            // Rendering is unavailable on this deployment. The manifest row exists and the parcels
            // point at it; the sheet can be produced later from the same data.
        }
    }
}

/// <summary>Lists handover sheets, newest first.</summary>
/// <param name="context">The Shipping data context.</param>
/// <param name="scope">Confines a seller to their own sheets.</param>
/// <param name="options">Supplies the page ceiling.</param>
internal sealed class ListManifestsQueryHandler(
    ShippingDbContext context,
    ShippingScope scope,
    IOptions<ShippingOptions> options) : IQueryHandler<ListManifestsQuery, PagedResult<ManifestResponse>>
{
    public async Task<Result<PagedResult<ManifestResponse>>> HandleAsync(
        ListManifestsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Math.Min(Cursor.NormalizeSize(query.Size), options.Value.MaxPageSize);
        var rows = context.Manifests.AsNoTracking().AsQueryable();

        if (!scope.IsVendor && query.VendorId is { } vendorId)
        {
            rows = rows.Where(manifest => manifest.VendorId == vendorId);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(manifest => manifest.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(manifest => manifest.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var items = page.Take(size).Select(ShippingProjection.ToManifest).ToArray();
        var next = hasMore && items.Length > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<ManifestResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>Reads the courier webhook log.</summary>
/// <param name="context">The Shipping data context.</param>
/// <param name="options">Supplies the page ceiling.</param>
internal sealed class ListCourierEventsQueryHandler(
    ShippingDbContext context,
    IOptions<ShippingOptions> options)
    : IQueryHandler<ListCourierEventsQuery, PagedResult<CourierEventResponse>>
{
    public async Task<Result<PagedResult<CourierEventResponse>>> HandleAsync(
        ListCourierEventsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Math.Min(Cursor.NormalizeSize(query.Size), options.Value.MaxPageSize);
        var rows = context.CourierEvents.AsNoTracking().AsQueryable();

        if (Enum.TryParse<CourierEventStatus>(query.Status, ignoreCase: true, out var status))
        {
            rows = rows.Where(entry => entry.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Awb))
        {
            rows = rows.Where(entry => entry.Awb == query.Awb.Trim());
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(entry => entry.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(entry => entry.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var items = page.Take(size).Select(ShippingProjection.ToCourierEvent).ToArray();
        var next = hasMore && items.Length > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<CourierEventResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>Re-queues a webhook that failed.</summary>
/// <param name="context">The Shipping data context.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class ReplayCourierEventCommandHandler(ShippingDbContext context, IClock clock)
    : ICommandHandler<ReplayCourierEventCommand, CourierEventResponse>
{
    public async Task<Result<CourierEventResponse>> HandleAsync(
        ReplayCourierEventCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var stored = await context.CourierEvents
            .FirstOrDefaultAsync(entry => entry.Id == command.EventId, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            return Result.Failure<CourierEventResponse>(ShippingErrors.NotFound("courier event"));
        }

        // A replay never re-verifies. An event whose signature failed must not become processable by
        // being asked for a second time.
        if (!stored.SignatureValid || !stored.Replay(clock.UtcNow))
        {
            return Result.Failure<CourierEventResponse>(ShippingErrors.EventNotReplayable);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ShippingProjection.ToCourierEvent(stored));
    }
}

/// <summary>Records a courier's remittance against the parcels it covers.</summary>
/// <param name="context">The Shipping data context.</param>
/// <param name="cash">The payments seam, which owns the ledger.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class RecordCourierRemittanceCommandHandler(
    ShippingDbContext context,
    ICodCollections cash,
    IClock clock) : ICommandHandler<RecordCourierRemittanceCommand, int>
{
    public async Task<Result<int>> HandleAsync(
        RecordCourierRemittanceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var awbs = command.Awbs
            .Where(awb => !string.IsNullOrWhiteSpace(awb))
            .Select(awb => awb.Trim())
            .Distinct()
            .ToArray();

        var shipmentIds = await context.Shipments
            .AsNoTracking()
            .Where(shipment => shipment.Awb != null && awbs.Contains(shipment.Awb))
            .Select(shipment => shipment.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (shipmentIds.Count == 0)
        {
            return Result.Failure<int>(ShippingErrors.NotFound("parcel"));
        }

        return await cash
            .RecordRemittanceAsync(
                shipmentIds,
                command.Reference,
                command.Amount,
                command.RemittedAt ?? clock.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
