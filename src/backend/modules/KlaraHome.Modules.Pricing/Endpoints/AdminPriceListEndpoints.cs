using KlaraHome.Contracts.Pricing;
using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Errors;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Pricing.Application.PriceLists;
using KlaraHome.Modules.Pricing.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Pricing.Endpoints;

/// <summary>Query-string filters for the price-list listing.</summary>
/// <param name="VendorId">Restrict to one seller's lists.</param>
/// <param name="Type">Restrict to one kind.</param>
/// <param name="ActiveOnly">Hide the ones that are switched off.</param>
/// <param name="Search">A fragment of the code or the name.</param>
/// <param name="Cursor">Opaque token from the previous page.</param>
/// <param name="Size">Page size.</param>
internal sealed record PriceListFilter(
    Guid? VendorId,
    PriceListType? Type,
    bool? ActiveOnly,
    string? Search,
    string? Cursor,
    int? Size);

/// <summary>The body of a new price list.</summary>
/// <param name="VendorId">The seller. Ignored for a vendor caller, who opens their own.</param>
/// <param name="Code">The short code an operator quotes.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Type">Why it exists: base, sale or scheduled.</param>
/// <param name="Priority">Resolution order. Lower wins.</param>
/// <param name="StartsAt">When it starts applying.</param>
/// <param name="EndsAt">When it stops.</param>
internal sealed record CreatePriceListBody(
    Guid? VendorId,
    string Code,
    string Name,
    PriceListType Type,
    int Priority,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt);

/// <summary>The body of a change to a price list.</summary>
/// <param name="Name">What it is called.</param>
/// <param name="Type">Why it exists.</param>
/// <param name="Priority">Resolution order.</param>
/// <param name="StartsAt">When it starts applying.</param>
/// <param name="EndsAt">When it stops.</param>
internal sealed record UpdatePriceListBody(
    string Name,
    PriceListType Type,
    int Priority,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt);

/// <summary>The body of a batch of prices.</summary>
/// <param name="Items">The prices, one row per offer per quantity tier.</param>
internal sealed record UpsertPriceListItemsBody(IReadOnlyList<PriceListItemPayload> Items);

/// <summary>
/// The price-list surface (docs/04-api-specification.md §4).
/// </summary>
/// <remarks>
/// Vendor-scoped by the caller's token: a seller sees their own lists and the platform's, and may
/// write only to their own — and may price only their own offers into them, which is a rule the
/// query filter cannot express because a price-list item carries no seller of its own.
/// </remarks>
internal static class AdminPriceListEndpoints
{
    /// <summary>Maps the price-list surface beneath <c>/admin</c>.</summary>
    /// <param name="admin">The <c>/admin</c> group.</param>
    public static IEndpointRouteBuilder MapAdminPriceListEndpoints(this IEndpointRouteBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var lists = admin.MapGroup("/price-lists").WithTags("Pricing");

        lists.MapGet("/", async ([AsParameters] PriceListFilter filter, IDispatcher dispatcher, HttpContext context) =>
            {
                var query = new ListPriceListsQuery(
                    filter.VendorId,
                    filter.Type,
                    filter.ActiveOnly,
                    filter.Search,
                    filter.Cursor,
                    filter.Size);

                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminPriceListsList")
            .WithSummary("Lists price lists. A vendor caller sees their own and the platform's.")
            .RequirePermission(PricingPermissions.PriceListRead)
            .Produces<PagedResult<PriceListResponse>>();

        lists.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetPriceListQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminPriceListGet")
            .WithSummary("Reads one price list. Answers 404 for a list outside the caller's scope.")
            .RequirePermission(PricingPermissions.PriceListRead)
            .Produces<PriceListResponse>();

        lists.MapPost("/", async (CreatePriceListBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var command = new CreatePriceListCommand(
                    body.VendorId,
                    body.Code,
                    body.Name,
                    body.Type,
                    body.Priority,
                    body.StartsAt,
                    body.EndsAt);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.Match(
                    list => Results.Created($"{context.Request.Path}/{list.Id}", list),
                    error => error.ToProblemResult(context));
            })
            .WithName("adminPriceListCreate")
            .WithSummary("Opens a price list. It is active immediately; its window decides when it applies.")
            .RequirePermission(PricingPermissions.PriceListManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<PriceListResponse>(StatusCodes.Status201Created);

        lists.MapPut("/{id:guid}", async (
                Guid id,
                UpdatePriceListBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdatePriceListCommand(
                    id,
                    body.Name,
                    body.Type,
                    body.Priority,
                    body.StartsAt,
                    body.EndsAt);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminPriceListUpdate")
            .WithSummary("Renames a price list and restates its rank and window.")
            .RequirePermission(PricingPermissions.PriceListManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<PriceListResponse>();

        lists.MapPost("/{id:guid}/activate", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new SetPriceListActiveCommand(id, IsActive: true), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminPriceListActivate")
            .WithSummary("Switches a price list on. Announces the new price of everything in it.")
            .RequirePermission(PricingPermissions.PriceListManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<PriceListResponse>();

        lists.MapPost("/{id:guid}/deactivate", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new SetPriceListActiveCommand(id, IsActive: false), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminPriceListDeactivate")
            .WithSummary("Switches a price list off. Offers in it fall back to the next list that applies.")
            .RequirePermission(PricingPermissions.PriceListManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<PriceListResponse>();

        lists.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new DeletePriceListCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("adminPriceListDelete")
            .WithSummary("Removes a price list and every price in it.")
            .RequirePermission(PricingPermissions.PriceListManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces(StatusCodes.Status204NoContent);

        lists.MapGet("/{id:guid}/items", async (
                Guid id,
                Guid? listingId,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new ListPriceListItemsQuery(id, listingId, cursor, size);
                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminPriceListItems")
            .WithSummary("Lists the prices in a list. Filter by offer to see all of its quantity tiers.")
            .RequirePermission(PricingPermissions.PriceListRead)
            .Produces<PagedResult<PriceListItemResponse>>();

        lists.MapPut("/{id:guid}/items", async (
                Guid id,
                UpsertPriceListItemsBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpsertPriceListItemsCommand(id, body.Items);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminPriceListItemsUpsert")
            .WithSummary("Adds or restates a batch of prices. A quantity tier is a row per minQuantity.")
            .RequirePermission(PricingPermissions.PriceListManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<IReadOnlyList<PriceListItemResponse>>();

        lists.MapDelete("/{id:guid}/items/{itemId:guid}", async (
                Guid id,
                Guid itemId,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new DeletePriceListItemCommand(id, itemId), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("adminPriceListItemDelete")
            .WithSummary("Removes one price. The offer falls back to the next list that applies.")
            .RequirePermission(PricingPermissions.PriceListManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces(StatusCodes.Status204NoContent);

        admin.MapGet("/prices/resolve", async (
                Guid listingId,
                int? quantity,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ResolvePriceQuery(listingId, quantity), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminPriceResolve")
            .WithSummary("Explains what an offer costs and which price list decided it.")
            .WithTags("Pricing")
            .RequirePermission(PricingPermissions.PriceListRead)
            .Produces<EffectivePrice>();

        return admin;
    }
}
