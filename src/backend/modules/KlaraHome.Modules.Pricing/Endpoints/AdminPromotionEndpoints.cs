using KlaraHome.Contracts.Pricing;
using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Errors;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Pricing.Application.Promotions;
using KlaraHome.Modules.Pricing.Application.Quotes;
using KlaraHome.Modules.Pricing.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Pricing.Endpoints;

/// <summary>Query-string filters for the promotion listing.</summary>
/// <param name="Type">Restrict to one kind.</param>
/// <param name="Code">Find the one with this code.</param>
/// <param name="ActiveOnly">Hide the ones that are switched off.</param>
/// <param name="Search">A fragment of the name or the code.</param>
/// <param name="Cursor">Opaque token from the previous page.</param>
/// <param name="Size">Page size.</param>
internal sealed record PromotionFilter(
    PromotionType? Type,
    string? Code,
    bool? ActiveOnly,
    string? Search,
    string? Cursor,
    int? Size);

/// <summary>The body of a new or amended promotion.</summary>
/// <param name="Code">The code a shopper types, or null for an automatic cart rule.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Description">The customer-facing terms.</param>
/// <param name="Type">Percentage, Fixed, FreeShipping, Bogo, Bundle or Tiered.</param>
/// <param name="AppliesTo">Line, Order or Shipping.</param>
/// <param name="Value">The percentage or amount.</param>
/// <param name="Scope">What it applies to.</param>
/// <param name="Conditions">What a basket must satisfy.</param>
/// <param name="Stacking">Exclusive or Stackable.</param>
/// <param name="Priority">Evaluation order. Lower goes first.</param>
/// <param name="StartsAt">When it opens.</param>
/// <param name="EndsAt">When it closes.</param>
/// <param name="UsageLimitTotal">The most times it may ever be redeemed.</param>
/// <param name="UsageLimitPerCustomer">The most times one shopper may redeem it.</param>
/// <param name="MinOrderValue">The smallest basket it applies to.</param>
/// <param name="MaxDiscount">The most it will ever take off.</param>
internal sealed record PromotionBody(
    string? Code,
    string Name,
    string? Description,
    PromotionType Type,
    PromotionApplication AppliesTo,
    decimal Value,
    PromotionScopePayload? Scope,
    PromotionConditionsPayload? Conditions,
    StackingMode Stacking,
    int Priority,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    int? UsageLimitTotal,
    int? UsageLimitPerCustomer,
    decimal MinOrderValue,
    decimal? MaxDiscount);

/// <summary>The body of a promotion simulation.</summary>
/// <param name="Lines">The basket to price.</param>
/// <param name="CustomerId">The shopper to price it for, for a segment or first-order campaign.</param>
/// <param name="StateId">The shipping address's state, which decides the GST split.</param>
/// <param name="CouponCode">A code to try.</param>
/// <param name="PaymentMethod">How it would be paid for. Defaults to prepaid.</param>
/// <param name="IsFirstOrder">Whether to treat it as the shopper's first order.</param>
/// <param name="ShippingAmount">What shipping would cost.</param>
internal sealed record SimulatePromotionBody(
    IReadOnlyList<QuoteLinePayload> Lines,
    Guid? CustomerId,
    Guid? StateId,
    string? CouponCode,
    QuotePaymentMethod? PaymentMethod,
    bool IsFirstOrder,
    decimal ShippingAmount);

/// <summary>
/// The promotion surface (docs/04-api-specification.md §4).
/// </summary>
/// <remarks>
/// Platform-wide. A campaign is the marketplace's, not a seller's — a seller who wants to discount
/// their own goods does it with a price list, which is theirs and is scoped to them.
/// </remarks>
internal static class AdminPromotionEndpoints
{
    /// <summary>Maps the promotion surface beneath <c>/admin</c>.</summary>
    /// <param name="admin">The <c>/admin</c> group.</param>
    public static IEndpointRouteBuilder MapAdminPromotionEndpoints(this IEndpointRouteBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var promotions = admin.MapGroup("/promotions").WithTags("Pricing");

        promotions.MapGet("/", async (
                [AsParameters] PromotionFilter filter,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new ListPromotionsQuery(
                    filter.Type,
                    filter.Code,
                    filter.ActiveOnly,
                    filter.Search,
                    filter.Cursor,
                    filter.Size);

                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminPromotionsList")
            .WithSummary("Lists coupon codes and automatic cart rules.")
            .RequirePermission(PricingPermissions.PromotionRead)
            .Produces<PagedResult<PromotionResponse>>();

        promotions.MapPost("/simulate", async (
                SimulatePromotionBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new QuoteBasketQuery(
                    body.Lines,
                    body.CustomerId,
                    body.StateId,
                    body.CouponCode,
                    body.PaymentMethod,
                    body.IsFirstOrder,
                    body.ShippingAmount,
                    WalletRedeemRequested: 0m);

                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminPromotionSimulate")
            .WithSummary(
                "Prices a basket and reports every promotion considered, applied or not, with the reason.")
            .RequirePermission(PricingPermissions.PromotionRead)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<QuoteResult>();

        promotions.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetPromotionQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminPromotionGet")
            .WithSummary("Reads one promotion, with its scope, conditions and usage.")
            .RequirePermission(PricingPermissions.PromotionRead)
            .Produces<PromotionResponse>();

        promotions.MapGet("/{id:guid}/redemptions", async (
                Guid id,
                Guid? customerId,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new ListRedemptionsQuery(id, customerId, cursor, size);
                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminPromotionRedemptions")
            .WithSummary("Lists a promotion's uses, including the ones that were reversed.")
            .RequirePermission(PricingPermissions.PromotionRead)
            .Produces<PagedResult<PromotionRedemptionResponse>>();

        promotions.MapPost("/", async (PromotionBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var command = new CreatePromotionCommand(
                    body.Code,
                    body.Name,
                    body.Description,
                    body.Type,
                    body.AppliesTo,
                    body.Value,
                    body.Scope,
                    body.Conditions,
                    body.Stacking,
                    body.Priority,
                    body.StartsAt,
                    body.EndsAt,
                    body.UsageLimitTotal,
                    body.UsageLimitPerCustomer,
                    body.MinOrderValue,
                    body.MaxDiscount);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.Match(
                    promotion => Results.Created($"{context.Request.Path}/{promotion.Id}", promotion),
                    error => error.ToProblemResult(context));
            })
            .WithName("adminPromotionCreate")
            .WithSummary("Drafts a promotion. It is inactive until it is activated.")
            .RequirePermission(PricingPermissions.PromotionManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<PromotionResponse>(StatusCodes.Status201Created);

        promotions.MapPut("/{id:guid}", async (
                Guid id,
                PromotionBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdatePromotionCommand(
                    id,
                    body.Name,
                    body.Description,
                    body.Type,
                    body.AppliesTo,
                    body.Value,
                    body.Scope,
                    body.Conditions,
                    body.Stacking,
                    body.Priority,
                    body.StartsAt,
                    body.EndsAt,
                    body.UsageLimitTotal,
                    body.UsageLimitPerCustomer,
                    body.MinOrderValue,
                    body.MaxDiscount);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminPromotionUpdate")
            .WithSummary("Restates a promotion. The code is fixed once created — shoppers have already seen it.")
            .RequirePermission(PricingPermissions.PromotionManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<PromotionResponse>();

        promotions.MapPost("/{id:guid}/activate", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new SetPromotionActiveCommand(id, IsActive: true), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminPromotionActivate")
            .WithSummary("Switches a promotion on. Its window still decides when it actually applies.")
            .RequirePermission(PricingPermissions.PromotionManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<PromotionResponse>();

        promotions.MapPost("/{id:guid}/deactivate", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new SetPromotionActiveCommand(id, IsActive: false), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminPromotionDeactivate")
            .WithSummary("Switches a promotion off immediately. The route to stop a leaked code.")
            .RequirePermission(PricingPermissions.PromotionManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<PromotionResponse>();

        promotions.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new DeletePromotionCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("adminPromotionDelete")
            .WithSummary("Removes a promotion that has never been used. A used one is deactivated instead.")
            .RequirePermission(PricingPermissions.PromotionManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces(StatusCodes.Status204NoContent);

        return admin;
    }
}
