using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Errors;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Catalog.Application.Listings;
using KlaraHome.Modules.Catalog.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Catalog.Endpoints;

/// <summary>Query-string filters for the offer listing.</summary>
/// <param name="Status">Restrict to one life-cycle state.</param>
/// <param name="VendorId">Restrict to one seller.</param>
/// <param name="ProductId">Restrict to one product.</param>
/// <param name="VariantId">Restrict to one variant.</param>
/// <param name="Search">A fragment of a SKU or the product's name.</param>
/// <param name="Cursor">Opaque token from the previous page.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListingListFilter(
    string? Status,
    Guid? VendorId,
    Guid? ProductId,
    Guid? VariantId,
    string? Search,
    string? Cursor,
    int? Size);

/// <summary>The body of a new offer.</summary>
/// <param name="VariantId">The variant being offered.</param>
/// <param name="VendorId">The seller. Ignored for a vendor caller, who offers as themselves.</param>
/// <param name="Mrp">The declared MRP, or null to take the variant's.</param>
/// <param name="SellingPrice">What the seller is asking, inclusive of GST.</param>
/// <param name="VendorSku">The seller's own code.</param>
/// <param name="HandlingTimeHours">How long they need before dispatch.</param>
/// <param name="IsCodAllowed">Whether they accept cash on delivery.</param>
/// <param name="MaxOrderQuantity">The most units one order may take.</param>
internal sealed record CreateListingBody(
    Guid VariantId,
    Guid? VendorId,
    decimal? Mrp,
    decimal SellingPrice,
    string? VendorSku,
    int HandlingTimeHours,
    bool IsCodAllowed,
    int? MaxOrderQuantity);

/// <summary>The body of a change to an offer's terms.</summary>
/// <param name="Mrp">The declared MRP.</param>
/// <param name="SellingPrice">What the seller is asking.</param>
/// <param name="VendorSku">The seller's own code.</param>
/// <param name="HandlingTimeHours">How long they need before dispatch.</param>
/// <param name="IsCodAllowed">Whether they accept cash on delivery.</param>
/// <param name="MaxOrderQuantity">The most units one order may take.</param>
internal sealed record UpdateListingBody(
    decimal Mrp,
    decimal SellingPrice,
    string? VendorSku,
    int HandlingTimeHours,
    bool IsCodAllowed,
    int? MaxOrderQuantity);

/// <summary>The body of a life-cycle move that needs an explanation.</summary>
/// <param name="Reason">Why. Shown to the seller.</param>
internal sealed record ListingStatusBody(string? Reason);

/// <summary>
/// The offers surface (docs/04-api-specification.md §4).
/// </summary>
/// <remarks>
/// Vendor-scoped by the caller's token and never by an id in the path: a seller cannot address
/// another seller's offer at all, and platform staff name the seller in the body when they open one
/// on somebody's behalf.
/// </remarks>
internal static class AdminListingEndpoints
{
    /// <summary>Maps the offers surface beneath <c>/admin</c>.</summary>
    /// <param name="admin">The <c>/admin</c> group.</param>
    public static IEndpointRouteBuilder MapAdminListingEndpoints(this IEndpointRouteBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var listings = admin.MapGroup("/listings").WithTags("Catalog");

        listings.MapGet("/", async (
                [AsParameters] ListingListFilter filter,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new ListListingsQuery(
                    filter.Status,
                    filter.VendorId,
                    filter.ProductId,
                    filter.VariantId,
                    filter.Search,
                    filter.Cursor,
                    filter.Size);

                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListingsList")
            .WithSummary("Lists offers, newest first. A vendor caller sees only their own.")
            .RequirePermission(CatalogPermissions.ListingRead)
            .Produces<PagedResult<ListingResponse>>();

        listings.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetListingQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListingGet")
            .WithSummary("Reads one offer. Answers 404 for an offer outside the caller's scope.")
            .RequirePermission(CatalogPermissions.ListingRead)
            .Produces<ListingResponse>();

        listings.MapPost("/", async (CreateListingBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var command = new CreateListingCommand(
                    body.VariantId,
                    body.VendorId,
                    body.Mrp,
                    body.SellingPrice,
                    body.VendorSku,
                    body.HandlingTimeHours,
                    body.IsCodAllowed,
                    body.MaxOrderQuantity);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.Match(
                    listing => Results.Created($"{context.Request.Path}/{listing.Id}", listing),
                    error => error.ToProblemResult(context));
            })
            .WithName("adminListingCreate")
            .WithSummary("Opens an offer against a variant. One offer per seller per variant.")
            .RequirePermission(CatalogPermissions.ListingManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ListingResponse>(StatusCodes.Status201Created);

        listings.MapPut("/{id:guid}", async (
                Guid id,
                UpdateListingBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdateListingCommand(
                    id,
                    body.Mrp,
                    body.SellingPrice,
                    body.VendorSku,
                    body.HandlingTimeHours,
                    body.IsCodAllowed,
                    body.MaxOrderQuantity);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);
                return result.ToOk(context);
            })
            .WithName("adminListingUpdate")
            .WithSummary("Changes an offer's price and terms. The price may never exceed the MRP.")
            .RequirePermission(CatalogPermissions.ListingManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ListingResponse>();

        MapStatus(listings, "activate", ListingStatus.Active, "adminListingActivate",
            "Puts the offer on the storefront. Refused unless the seller, variant and product are all live.");

        MapStatus(listings, "deactivate", ListingStatus.Inactive, "adminListingDeactivate",
            "Pauses the offer. It stops competing for the buy box immediately.");

        MapStatus(listings, "archive", ListingStatus.Archived, "adminListingArchive",
            "Retires the offer for good. Terminal, because order lines point at it.");

        return admin;
    }

    private static void MapStatus(
        IEndpointRouteBuilder listings,
        string segment,
        ListingStatus status,
        string name,
        string summary)
        => listings.MapPost($"/{{id:guid}}/{segment}", async (
                Guid id,
                ListingStatusBody? body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new ChangeListingStatusCommand(id, status, body?.Reason);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName(name)
            .WithSummary(summary)
            .RequirePermission(CatalogPermissions.ListingManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ListingResponse>();
}
