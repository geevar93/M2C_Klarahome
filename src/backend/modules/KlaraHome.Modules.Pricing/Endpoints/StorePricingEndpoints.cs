using KlaraHome.Contracts.Pricing;
using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Errors;
using KlaraHome.Infrastructure.Features;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Pricing.Application;
using KlaraHome.Modules.Pricing.Application.Quotes;
using KlaraHome.Modules.Pricing.Application.Wallets;
using KlaraHome.Modules.Pricing.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Pricing.Endpoints;

/// <summary>The body of a stateless storefront quote.</summary>
/// <param name="Lines">The basket to price.</param>
/// <param name="StateId">The shipping address's state, which decides the GST split.</param>
/// <param name="CouponCode">A code to try.</param>
/// <param name="PaymentMethod">How it would be paid for. Prepaid when omitted.</param>
/// <param name="ShippingAmount">What shipping would cost, as the caller knows it.</param>
/// <param name="WalletRedeemRequested">How much store credit to try to apply.</param>
internal sealed record StoreQuoteBody(
    IReadOnlyList<QuoteLinePayload> Lines,
    Guid? StateId,
    string? CouponCode,
    QuotePaymentMethod? PaymentMethod,
    decimal ShippingAmount,
    decimal WalletRedeemRequested);

/// <summary>
/// What a shopper can price and read of their own credit (docs/04-api-specification.md §3.1, §3.3).
/// </summary>
/// <remarks>
/// <para>
/// The quote is anonymous and stateless: a product page pricing one unit, a quantity-tier preview,
/// a coupon tried before a cart exists. It writes nothing — no promotion is redeemed and no credit
/// is debited — which is what makes it safe to leave open, and it is rate-limited as a storefront
/// read because it is not free to serve.
/// </para>
/// <para>
/// A signed-in caller gets more from the same endpoint without asking for it: their own id goes
/// into the request, so per-customer limits and segment-scoped campaigns are evaluated. The
/// customer is never taken from the body — a caller who could name somebody else would be able to
/// spend their store credit.
/// </para>
/// </remarks>
internal static class StorePricingEndpoints
{
    /// <summary>Maps the storefront pricing surface beneath <c>/store</c>.</summary>
    /// <param name="store">The <c>/store</c> group.</param>
    public static IEndpointRouteBuilder MapStorePricingEndpoints(this IEndpointRouteBuilder store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var group = store
            .MapGroup(string.Empty)
            .WithTags("Pricing")
            .RequireRateLimiting(RateLimitPolicies.StorefrontRead);

        group.MapPost("/quote", async (
                StoreQuoteBody body,
                IDispatcher dispatcher,
                PricingScope scope,
                HttpContext context) =>
            {
                var query = new QuoteBasketQuery(
                    body.Lines,
                    scope.CustomerId,
                    body.StateId,
                    body.CouponCode,
                    body.PaymentMethod,
                    IsFirstOrder: false,
                    body.ShippingAmount,
                    body.WalletRedeemRequested);

                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeQuote")
            .WithSummary("Prices a basket the server is not holding, itemised, with the full GST breakdown.")
            .AllowAnonymous()
            .Produces<QuoteResult>();

        var me = store.MapGroup("/me/wallet")
            .WithTags("Pricing")
            .RequireFeature(PricingFeatureFlags.StoreCredit);

        me.MapGet("/", async (IDispatcher dispatcher, PricingScope scope, HttpContext context) =>
            {
                if (scope.CustomerId is not { } customerId)
                {
                    return PricingErrors.WalletDisabled.ToProblemResult(context);
                }

                var result = await dispatcher
                    .QueryAsync(new GetWalletQuery(customerId), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeWallet")
            .WithSummary("The signed-in shopper's own store-credit balance.")
            .RequireAuthorization()
            .Produces<WalletResponse>();

        me.MapGet("/transactions", async (
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                PricingScope scope,
                HttpContext context) =>
            {
                if (scope.CustomerId is not { } customerId)
                {
                    return PricingErrors.WalletDisabled.ToProblemResult(context);
                }

                var query = new ListWalletTransactionsQuery(customerId, cursor, size);
                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeWalletTransactions")
            .WithSummary("The signed-in shopper's own store-credit statement, newest first.")
            .RequireAuthorization()
            .Produces<PagedResult<WalletTransactionResponse>>();

        return store;
    }
}
