using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Payments.Application.Payments;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Payments.Endpoints;

/// <summary>The body of the browser's callback after the checkout widget closes.</summary>
/// <param name="ProviderOrderId">The gateway order id the widget was opened with.</param>
/// <param name="ProviderPaymentId">The payment id it handed back.</param>
/// <param name="Signature">The signature it handed back.</param>
internal sealed record VerifyCheckoutBody(
    string ProviderOrderId,
    string ProviderPaymentId,
    string Signature);

/// <summary>
/// What a shopper can see and do about paying for their own order
/// (docs/04-api-specification.md §3.5).
/// </summary>
/// <remarks>
/// <para>
/// Every route requires an account and every handler resolves the order by
/// <em>(order, customer)</em> through the ordering contract. An id belonging to somebody else does
/// not resolve, and the answer is the same 404 a made-up id gets.
/// </para>
/// <para>
/// There is no route here that changes what is owed, and no route that can confirm a payment. The
/// storefront may look, ask for a fresh instruction inside the retry window, and hand back what the
/// widget told it — and that last one records an attempt and nothing more. An order becomes paid on
/// the webhook plus an API re-fetch, and on nothing a browser says
/// (docs/07-security-compliance.md §4).
/// </para>
/// </remarks>
internal static class StorePaymentEndpoints
{
    /// <summary>Maps the payment surface beneath <c>/store/payments</c>.</summary>
    /// <param name="store">The <c>/store</c> group.</param>
    public static IEndpointRouteBuilder MapStorePaymentEndpoints(this IEndpointRouteBuilder store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var group = store
            .MapGroup("/payments")
            .WithTags("Payments")
            .RequireAuthorization();

        group.MapGet("/orders/{orderId:guid}", async (
                Guid orderId,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetMyPaymentQuery(orderId), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeGetOrderPayment")
            .WithSummary("Where the money for one of the caller's own orders stands.")
            .Produces<MyPaymentResponse>();

        group.MapPost("/orders/{orderId:guid}/retry", async (
                Guid orderId,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                // The key comes from the header the API convention already requires on anything that
                // creates a payment (docs/04-api-specification.md §1). Falling back to the order id
                // keeps a retry idempotent even for a client that forgot to send one: two taps then
                // reuse one collection instead of racing the partial unique index.
                var key = context.Request.Headers["Idempotency-Key"].ToString();

                var result = await dispatcher
                    .SendAsync(
                        new RetryPaymentCommand(
                            orderId,
                            string.IsNullOrWhiteSpace(key) ? $"retry:{orderId}" : key),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeRetryPayment")
            .WithSummary("A fresh payment instruction for an order that was not paid for.")
            .RequireRateLimiting(RateLimitPolicies.PlaceOrder)
            .Produces<PaymentInstructionResponse>();

        group.MapPost("/orders/{orderId:guid}/verify", async (
                Guid orderId,
                VerifyCheckoutBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(
                        new VerifyCheckoutCommand(
                            orderId,
                            body.ProviderOrderId,
                            body.ProviderPaymentId,
                            body.Signature),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeVerifyCheckout")
            .WithSummary("Records the browser's callback as an attempt. It confirms nothing.")
            .RequireRateLimiting(RateLimitPolicies.CartWrite)
            .Produces<MyPaymentResponse>();

        return store;
    }
}
