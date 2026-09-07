using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Errors;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Infrastructure.Storage;
using KlaraHome.Modules.Shipping.Application;
using KlaraHome.Modules.Shipping.Application.Ndr;
using KlaraHome.Modules.Shipping.Application.Operations;
using KlaraHome.Modules.Shipping.Application.Rates;
using KlaraHome.Modules.Shipping.Application.Shipments;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Shipping.Endpoints;

/// <summary>The body of a zone.</summary>
/// <param name="Code">The stable code rate rules refer to. Ignored on an update.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Priority">Lower wins where two zones both match.</param>
/// <param name="States">The states it covers.</param>
/// <param name="PincodeRanges">The PIN-code runs it covers.</param>
/// <param name="IsActive">Whether it is used. Ignored on a create, which is always active.</param>
internal sealed record ZoneBody(
    string? Code,
    string Name,
    int Priority,
    IReadOnlyList<Guid>? States,
    IReadOnlyList<PincodeRangeModel>? PincodeRanges,
    bool IsActive = true);

/// <summary>The body of a rate rule.</summary>
/// <param name="ZoneId">The zone it prices. Ignored on an update.</param>
/// <param name="Method">Standard or express. Ignored on an update.</param>
/// <param name="VendorId">The seller it overrides for. Ignored for a vendor caller.</param>
/// <param name="Terms">What it charges and promises.</param>
/// <param name="IsActive">Whether it is used.</param>
internal sealed record RateBody(
    Guid ZoneId,
    string? Method,
    Guid? VendorId,
    RateTerms Terms,
    bool IsActive = true);

/// <summary>The body of a dispatch.</summary>
/// <param name="Lines">What to pack, or empty for everything still unsent.</param>
/// <param name="Weight">What the scale said, in grams.</param>
/// <param name="Dimensions">The box, in centimetres.</param>
/// <param name="Courier">The service to book: <c>standard</c> or <c>express</c>.</param>
/// <param name="PickupLocationId">The address to collect from, or null for the seller's default.</param>
/// <param name="ManualAwb">An air waybill the operator obtained from a courier themselves.</param>
/// <param name="ManualCourier">Who is carrying it, for a hand-booking.</param>
internal sealed record CreateShipmentBody(
    IReadOnlyList<PackedLine>? Lines,
    int Weight,
    ParcelDimensions? Dimensions,
    string? Courier,
    Guid? PickupLocationId,
    string? ManualAwb,
    string? ManualCourier);

/// <summary>The body of a packing.</summary>
/// <param name="Lines">What to pack, or empty for everything still unsent.</param>
internal sealed record PackBody(IReadOnlyList<PackedLine>? Lines);

/// <summary>The body of a weight capture.</summary>
/// <param name="Weight">What the scale said, in grams.</param>
/// <param name="Dimensions">The box, in centimetres.</param>
internal sealed record WeighBody(int Weight, ParcelDimensions? Dimensions);

/// <summary>The body of a booking.</summary>
/// <param name="Courier">The service to book.</param>
/// <param name="PickupLocationId">The address to collect from.</param>
/// <param name="ManualAwb">An air waybill the operator obtained themselves.</param>
/// <param name="ManualCourier">Who is carrying it.</param>
internal sealed record BookBody(
    string? Courier,
    Guid? PickupLocationId,
    string? ManualAwb,
    string? ManualCourier);

/// <summary>The body of a pickup request.</summary>
/// <param name="PickupAt">When the parcel will be ready, or null for tomorrow.</param>
internal sealed record SchedulePickupBody(DateTimeOffset? PickupAt);

/// <summary>The body of a cancellation.</summary>
/// <param name="Reason">Why.</param>
internal sealed record CancelShipmentBody(string? Reason);

/// <summary>The body of a hand-recorded movement.</summary>
/// <param name="Status">Where the parcel now is.</param>
/// <param name="Remark">What the courier said.</param>
/// <param name="OccurredAt">When, or null for now.</param>
internal sealed record RecordTrackingBody(string Status, string? Remark, DateTimeOffset? OccurredAt);

/// <summary>The body of a decision about a failed delivery.</summary>
/// <param name="Action">What to do.</param>
/// <param name="Remark">What the operator wants recorded.</param>
/// <param name="RescheduledFor">The date the shopper asked for.</param>
internal sealed record NdrActionBody(NdrAction Action, string? Remark, DateTimeOffset? RescheduledFor);

/// <summary>The body of a handover.</summary>
/// <param name="ShipmentIds">The parcels going out, or empty for everything ready.</param>
/// <param name="VendorId">The seller handing over.</param>
/// <param name="PickupLocationId">The address being collected from.</param>
internal sealed record CreateManifestBody(
    IReadOnlyList<Guid>? ShipmentIds,
    Guid? VendorId,
    Guid? PickupLocationId);

/// <summary>The body of a courier's cash remittance.</summary>
/// <param name="Awbs">The air waybills it covers.</param>
/// <param name="Reference">The courier's reference — a UTR or a batch number.</param>
/// <param name="Amount">What arrived in total.</param>
/// <param name="RemittedAt">When, or null for now.</param>
internal sealed record CourierRemittanceBody(
    IReadOnlyList<string> Awbs,
    string Reference,
    decimal? Amount,
    DateTimeOffset? RemittedAt);

/// <summary>
/// What staff and sellers can see and do about parcels (docs/04-api-specification.md §4).
/// </summary>
/// <remarks>
/// <para>
/// Four surfaces, four jobs. A seller packs, weighs, books and hands over. Operations works the
/// failed-delivery queue. Whoever is diagnosing a courier integration reads the webhook log and
/// replays what failed. And the rate card is a commercial surface that belongs to neither.
/// </para>
/// <para>
/// Every route is vendor-scoped by the query filter rather than by a seller id in the path. A seller
/// reaching for another seller's parcel gets the same 404 an invented id gets, and there is no route
/// anywhere that takes a vendor id a seller could change.
/// </para>
/// <para>
/// There is no route that sets a parcel's status directly. Everything that moves one goes through
/// the workflow — a booking, a courier scan, a hand-recorded movement — so an operator's action and
/// a webhook write the same timeline and raise the same events.
/// </para>
/// </remarks>
internal static class AdminShippingEndpoints
{
    /// <summary>
    /// How long a label link stays usable. Long enough to click through and print, short enough
    /// that a URL left in a chat window is not a customer's address.
    /// </summary>
    private static readonly TimeSpan LabelLinkLifetime = TimeSpan.FromMinutes(10);

    /// <summary>Maps the shipping surface beneath <c>/admin</c>.</summary>
    /// <param name="admin">The <c>/admin</c> group.</param>
    public static IEndpointRouteBuilder MapAdminShippingEndpoints(this IEndpointRouteBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        MapRateCard(admin);
        MapShipments(admin);
        MapManifests(admin);
        MapNdr(admin);
        MapCourier(admin);

        return admin;
    }

    private static void MapRateCard(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/shipping").WithTags("Shipping");

        group.MapGet("/zones", async (bool? includeInactive, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListZonesQuery(includeInactive ?? false), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListShippingZones")
            .WithSummary("The delivery map, in the precedence order the rate engine applies.")
            .RequirePermission(ShippingPermissions.RateManage)
            .Produces<IReadOnlyList<ShippingZoneResponse>>();

        group.MapPost("/zones", async (ZoneBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(
                        new CreateZoneCommand(
                            body.Code ?? string.Empty,
                            body.Name,
                            body.Priority,
                            body.States ?? [],
                            body.PincodeRanges ?? []),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCreateShippingZone")
            .WithSummary("Opens a delivery zone.")
            .RequirePermission(ShippingPermissions.RateManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ShippingZoneResponse>();

        group.MapPut("/zones/{id:guid}", async (
                Guid id,
                ZoneBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(
                        new UpdateZoneCommand(
                            id,
                            body.Name,
                            body.Priority,
                            body.States ?? [],
                            body.PincodeRanges ?? [],
                            body.IsActive),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminUpdateShippingZone")
            .WithSummary("Redraws a delivery zone.")
            .RequirePermission(ShippingPermissions.RateManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ShippingZoneResponse>();

        group.MapGet("/rates", async (
                Guid? zoneId,
                Guid? vendorId,
                string? method,
                bool? includeInactive,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(
                        new ListRatesQuery(zoneId, vendorId, method, includeInactive ?? false),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListShippingRates")
            .WithSummary("The rate card. A seller sees the platform's rules and their own overrides.")
            .RequirePermission(ShippingPermissions.RateManage)
            .Produces<IReadOnlyList<ShippingRateResponse>>();

        group.MapPost("/rates", async (RateBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(
                        new CreateRateCommand(body.ZoneId, body.Method ?? "standard", body.VendorId, body.Terms),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCreateShippingRate")
            .WithSummary("Adds a rule to the rate card.")
            .RequirePermission(ShippingPermissions.RateManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ShippingRateResponse>();

        group.MapPut("/rates/{id:guid}", async (
                Guid id,
                RateBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new UpdateRateCommand(id, body.Terms, body.IsActive), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminUpdateShippingRate")
            .WithSummary("Changes what a rule charges and promises.")
            .RequirePermission(ShippingPermissions.RateManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ShippingRateResponse>();

        group.MapGet("/serviceability/{pincode}", async (
                string pincode,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetServiceabilityQuery(pincode), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGetServiceability")
            .WithSummary("What the cache says about a PIN code, and when it last asked.")
            .RequirePermission(ShippingPermissions.CourierManage)
            .Produces<ServiceabilityResponse>();

        group.MapPost("/serviceability/{pincode}/refresh", async (
                string pincode,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new RefreshServiceabilityCommand(pincode), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminRefreshServiceability")
            .WithSummary("Asks the courier about a PIN code now, rather than waiting for the nightly job.")
            .RequirePermission(ShippingPermissions.CourierManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ServiceabilityResponse>();

        // The delivery area is read here and edited through PUT /admin/settings, where every other
        // store policy is edited and where the audit trail and the validator already live (ADR-018).
        // Reading it beside the parcels is what an operator wants; a second write path for one
        // settings row is not.
        group.MapGet("/coverage", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetDeliveryCoverageQuery(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGetDeliveryCoverage")
            .WithSummary("Where this store currently delivers.")
            .RequirePermission(ShippingPermissions.CourierManage)
            .Produces<DeliveryCoverageResponse>();

        group.MapGet("/coverage/test/{pincode}", async (
                string pincode,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new TestDeliveryCoverageQuery(pincode), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminTestDeliveryCoverage")
            .WithSummary("Whether one address would be accepted, and which check refuses it.")
            .RequirePermission(ShippingPermissions.CourierManage)
            .Produces<ServiceabilityResponse>();

        group.MapPost("/cod-remittances", async (
                CourierRemittanceBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(
                        new RecordCourierRemittanceCommand(
                            body.Awbs,
                            body.Reference,
                            body.Amount,
                            body.RemittedAt),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminRecordCourierRemittance")
            .WithSummary("Records a courier's cash remittance against the parcels it covers.")
            .RequirePermission(ShippingPermissions.CourierManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<int>();
    }

    private static void MapShipments(IEndpointRouteBuilder admin)
    {
        // The route docs/04-api-specification.md §4 names, on the sub-order rather than on a parcel:
        // a dispatch is something you do to an order, and the parcel is what it produces.
        admin.MapPost("/sub-orders/{id:guid}/shipments", async (
                Guid id,
                CreateShipmentBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(
                        new CreateShipmentCommand(
                            id,
                            body.Lines ?? [],
                            body.Weight,
                            body.Dimensions,
                            body.Courier,
                            body.PickupLocationId,
                            body.ManualAwb,
                            body.ManualCourier),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithTags("Shipping")
            .WithName("adminCreateShipment")
            .WithSummary("Packs, weighs and books a parcel for one seller's part of an order.")
            .RequirePermission(ShippingPermissions.ShipmentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ShipmentResponse>();

        var group = admin.MapGroup("/shipments").WithTags("Shipping");

        group.MapGet("/", async (
                string? status,
                Guid? vendorId,
                Guid? orderId,
                Guid? subOrderId,
                string? awb,
                string? q,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(
                        new ListShipmentsQuery(status, vendorId, orderId, subOrderId, awb, q, cursor, size),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListShipments")
            .WithSummary("Parcels, newest first, filtered by status, seller, order or air waybill.")
            .RequirePermission(ShippingPermissions.ShipmentRead)
            .Produces<PagedResult<ShipmentSummaryResponse>>();

        group.MapGet("/pick-list", async (
                Guid? vendorId,
                Guid? warehouseId,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetPickListQuery(vendorId, warehouseId, size), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminShipmentPickList")
            .WithSummary("Everything waiting to be packed, one row per item, soonest deadline first. Each "
                         + "row names the stock location it is on, and warehouseId narrows the list to one.")
            .RequirePermission(ShippingPermissions.ShipmentManage)
            .Produces<IReadOnlyList<PickListLineResponse>>();

        group.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetShipmentQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGetShipment")
            .WithSummary("One parcel in full, with what is in it and everywhere it has been.")
            .RequirePermission(ShippingPermissions.ShipmentRead)
            .Produces<ShipmentResponse>();

        group.MapPut("/{id:guid}/contents", async (
                Guid id,
                PackBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new PackShipmentCommand(id, body.Lines ?? []), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminPackShipment")
            .WithSummary("Replaces what is in a parcel. Refused once it has been booked.")
            .RequirePermission(ShippingPermissions.ShipmentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ShipmentResponse>();

        group.MapPost("/{id:guid}/weight", async (
                Guid id,
                WeighBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new CaptureWeightCommand(id, body.Weight, body.Dimensions), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCaptureShipmentWeight")
            .WithSummary("Records what the packer weighed and measured.")
            .RequirePermission(ShippingPermissions.ShipmentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ShipmentResponse>();

        group.MapPost("/{id:guid}/book", async (
                Guid id,
                BookBody? body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(
                        new BookShipmentCommand(
                            id,
                            body?.Courier,
                            body?.PickupLocationId,
                            body?.ManualAwb,
                            body?.ManualCourier),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminBookShipment")
            .WithSummary("Books a packed parcel with a courier, or records an air waybill obtained by hand.")
            .RequirePermission(ShippingPermissions.ShipmentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ShipmentResponse>();

        group.MapGet("/{id:guid}/label", async (Guid id, HttpContext context) =>
                await LabelAsync(id, context).ConfigureAwait(false))
            .WithName("adminGetShipmentLabel")
            .WithSummary("A short-lived link to the label to print: the courier's own where there is one, "
                         + "ours where there is not. Minting the link is the grant.")
            .RequirePermission(ShippingPermissions.ShipmentManage)
            .Produces<ShipmentLabelResponse>();

        group.MapPost("/{id:guid}/schedule-pickup", async (
                Guid id,
                SchedulePickupBody? body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new SchedulePickupCommand(id, body?.PickupAt), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminSchedulePickup")
            .WithSummary("Asks the courier to collect.")
            .RequirePermission(ShippingPermissions.ShipmentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ShipmentResponse>();

        group.MapPost("/{id:guid}/dispatch", async (
                Guid id,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new MarkDispatchedCommand(id, At: null), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminDispatchShipment")
            .WithSummary("Records that the courier has taken the parcel. This is what ships the order.")
            .RequirePermission(ShippingPermissions.ShipmentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ShipmentResponse>();

        group.MapPost("/{id:guid}/cancel", async (
                Guid id,
                CancelShipmentBody? body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new CancelShipmentCommand(id, body?.Reason), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCancelShipment")
            .WithSummary("Calls off a parcel the courier has not yet collected.")
            .RequirePermission(ShippingPermissions.ShipmentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ShipmentResponse>();

        group.MapPost("/{id:guid}/sync", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new SyncTrackingCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminSyncShipmentTracking")
            .WithSummary("Re-reads the parcel from its courier and applies what they say.")
            .RequirePermission(ShippingPermissions.ShipmentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ShipmentResponse>();

        group.MapPost("/{id:guid}/tracking", async (
                Guid id,
                RecordTrackingBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(
                        new RecordTrackingCommand(id, body.Status, body.Remark, body.OccurredAt),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminRecordShipmentTracking")
            .WithSummary("Records a movement learned outside the platform, for a hand-booked parcel.")
            .RequirePermission(ShippingPermissions.ShipmentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ShipmentResponse>();
    }

    private static void MapManifests(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/manifests").WithTags("Shipping");

        group.MapGet("/", async (
                Guid? vendorId,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListManifestsQuery(vendorId, cursor, size), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListManifests")
            .WithSummary("Handover sheets, newest first.")
            .RequirePermission(ShippingPermissions.ShipmentRead)
            .Produces<PagedResult<ManifestResponse>>();

        group.MapPost("/", async (
                CreateManifestBody? body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(
                        new CreateManifestCommand(
                            body?.ShipmentIds ?? [],
                            body?.VendorId,
                            body?.PickupLocationId),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCreateManifest")
            .WithSummary("Produces the handover sheet a courier's driver signs.")
            .RequirePermission(ShippingPermissions.ShipmentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ManifestResponse>();
    }

    private static void MapNdr(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/ndr").WithTags("Shipping");

        group.MapGet("/", async (
                NdrAction? action,
                Guid? vendorId,
                string? reasonCode,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(
                        new ListNdrQuery(action, vendorId, reasonCode, cursor, size),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListNdr")
            .WithSummary("Failed delivery attempts. Unactioned ones by default: this is the queue.")
            .RequirePermission(ShippingPermissions.NdrManage)
            .Produces<PagedResult<NdrResponse>>();

        group.MapPost("/{id:guid}/action", async (
                Guid id,
                NdrActionBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(
                        new ActionNdrCommand(id, body.Action, body.Remark, body.RescheduledFor),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminActionNdr")
            .WithSummary("Reattempt, reschedule, correct the address, or send the parcel back.")
            .RequirePermission(ShippingPermissions.NdrManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<NdrResponse>();
    }

    private static void MapCourier(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/courier-events").WithTags("Shipping");

        group.MapGet("/", async (
                string? status,
                string? awb,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListCourierEventsQuery(status, awb, cursor, size), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListCourierEvents")
            .WithSummary("The courier webhook log and its dead-letter queue.")
            .RequirePermission(ShippingPermissions.CourierManage)
            .Produces<PagedResult<CourierEventResponse>>();

        group.MapPost("/{id:guid}/replay", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new ReplayCourierEventCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminReplayCourierEvent")
            .WithSummary("Puts a failed or dead-lettered event back in the queue.")
            .RequirePermission(ShippingPermissions.CourierManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<CourierEventResponse>();
    }

    /// <summary>
    /// Serves the label a packer prints.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A short-lived signed link rather than the bytes, for both sources. A label carries a
    /// customer's name, address and telephone number, so it lives in the private bucket and is
    /// reached through a URL that expires (docs/07-security-compliance.md §5) — and streaming it
    /// through the API would put a customer's address into the application's own logs and caches.
    /// </para>
    /// <para>
    /// The link is returned <em>in the body</em>, not as a 302. It used to redirect, and a redirect
    /// is only usable by something that already holds a bearer token — which a plain link and a
    /// <c>window.open</c> do not, because the access token lives in memory only. The back office
    /// had to fetch the label as a blob through a bypass of its own generated client to print it.
    /// A body the client can read the URL out of removes that (Step 28B, deliverable 7).
    /// </para>
    /// <para>
    /// The courier's own label wins where there is one, because ours carries no barcode. The
    /// endpoint produces one on demand where neither exists, which is what makes it retryable after
    /// a booking whose label fetch failed.
    /// </para>
    /// </remarks>
    private static async Task<IResult> LabelAsync(Guid shipmentId, HttpContext context)
    {
        var services = context.RequestServices;
        var data = services.GetRequiredService<ShippingDbContext>();

        var shipment = await data.Shipments
            .Include(candidate => candidate.Lines)
            .FirstOrDefaultAsync(candidate => candidate.Id == shipmentId, context.RequestAborted)
            .ConfigureAwait(false);

        if (shipment is null)
        {
            return Results.NotFound();
        }

        if (!shipment.HasLabel)
        {
            var booker = services.GetRequiredService<Infrastructure.Fulfilment.ShipmentBooker>();

            var produced = await booker.EnsureLabelAsync(shipment, context.RequestAborted).ConfigureAwait(false);

            if (produced.IsFailure)
            {
                return produced.Error.ToProblemResult(context);
            }

            await data.SaveChangesAsync(context.RequestAborted).ConfigureAwait(false);
        }

        var fileName = $"label-{shipment.Awb ?? shipment.SubOrderNumber}.pdf";
        var expiresAt = services.GetRequiredService<IClock>().UtcNow.Add(LabelLinkLifetime);

        if (shipment.LabelObjectKey is { Length: > 0 } key)
        {
            var storage = services.GetRequiredService<IFileStorage>();

            var link = storage.GetSignedUrl(key, StorageVisibility.Private, LabelLinkLifetime, fileName);

            if (link is { Length: > 0 })
            {
                return Results.Ok(new ShipmentLabelResponse(shipment.Id, link, expiresAt, fileName));
            }
        }

        if (shipment.LabelFileId is { } fileId)
        {
            var media = services.GetRequiredService<KlaraHome.Contracts.Media.IMediaLibrary>();

            var link = await media.GetSignedUrlAsync(fileId, context.RequestAborted).ConfigureAwait(false);

            if (link is { Length: > 0 })
            {
                return Results.Ok(new ShipmentLabelResponse(shipment.Id, link, expiresAt, fileName));
            }
        }

        return ShippingErrors.LabelMissing.ToProblemResult(context);
    }
}
