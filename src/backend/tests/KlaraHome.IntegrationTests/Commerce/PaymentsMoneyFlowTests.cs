using System.Net;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Payments.Infrastructure.Processing;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The step's own four stated acceptance criteria (step-15 card, verified here per Step 29):
/// a sandbox payment moves an order to <c>Confirmed</c>, a replayed webhook is a no-op, a refund is
/// recorded and reconciles, and a dropped webhook is recovered by the reconciliation job.
/// </summary>
/// <param name="fixture">The migrated database.</param>
[Collection(KlaraHomeSchema.CollectionName)]
public sealed class PaymentsMoneyFlowTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// A sandbox-style payment — placement opens a collection, the gateway pays it, the webhook
    /// lands — moves the order to <c>Confirmed</c>.
    /// </summary>
    [Fact]
    public async Task A_captured_payment_confirms_the_order()
    {
        SkipWithoutDocker();

        Factory.Features["identity.mobile-otp-login"] = true;
        var admin = await SignedInAdministratorAsync();
        var scenario = new PaymentsScenario(admin, Cancellation);
        var offer = await scenario.OfferAsync(price: 999m);

        var (shopper, _) = await SignedInShopperAsync();
        var (order, _) = await scenario.PlaceOrderAsync(shopper, offer);

        Assert.NotNull(order.ProviderOrderId);

        var paid = Factory.Gateway.PayOrder(order.ProviderOrderId!, order.Amount);

        var body = Webhook.PaymentCaptured(order.ProviderOrderId!, paid.ProviderPaymentId, order.Amount);

        var response = await PostRawAsync(
            CreateClient(),
            "/api/v1/webhooks/razorpay",
            body,
            ("X-Razorpay-Signature", Webhook.Sign(body)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await OutboxDrain.RunAsync(Factory, Database, Cancellation);
        await GatewayEventDrain.RunOnceAsync(Factory, Cancellation);

        var read = await ReadAsync(
            await shopper.GetAsync(new Uri($"/api/v1/store/orders/{order.OrderId}", UriKind.Relative), Cancellation));

        Assert.Equal("Confirmed", read.GetProperty("subOrders")[0].GetProperty("status").GetString());
        Assert.True(read.GetProperty("paymentStatus").GetString() is "Paid" or "PartiallyPaid" or "Captured");
    }

    /// <summary>
    /// The same signed body delivered twice produces one stored event, one attempt and one
    /// transition — the second delivery is a no-op.
    /// </summary>
    [Fact]
    public async Task A_replayed_webhook_is_a_no_op()
    {
        SkipWithoutDocker();

        Factory.Features["identity.mobile-otp-login"] = true;
        var admin = await SignedInAdministratorAsync();
        var scenario = new PaymentsScenario(admin, Cancellation);
        var offer = await scenario.OfferAsync();

        var (shopper, _) = await SignedInShopperAsync();
        var (order, _) = await scenario.PlaceOrderAsync(shopper, offer);

        var paid = Factory.Gateway.PayOrder(order.ProviderOrderId!, order.Amount);

        var body = Webhook.PaymentCaptured(order.ProviderOrderId!, paid.ProviderPaymentId, order.Amount);
        var signature = Webhook.Sign(body);

        var first = await PostRawAsync(
            CreateClient(),
            "/api/v1/webhooks/razorpay",
            body,
            ("X-Razorpay-Signature", signature));

        var second = await PostRawAsync(
            CreateClient(),
            "/api/v1/webhooks/razorpay",
            body,
            ("X-Razorpay-Signature", signature));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var stored = await Database.CountAsync(
            "SELECT COUNT(*) FROM payments.gateway_events WHERE provider_event_id = $1",
            Cancellation,
            Webhook.EventId(body));

        Assert.Equal(1, stored);

        await OutboxDrain.RunAsync(Factory, Database, Cancellation);
        await GatewayEventDrain.RunOnceAsync(Factory, Cancellation);

        var attempts = await Database.CountAsync(
            "SELECT COUNT(*) FROM payments.payment_attempts a "
            + "JOIN payments.payments p ON p.id = a.payment_id "
            + "WHERE p.order_id = $1 AND a.status = 'captured'",
            Cancellation,
            order.OrderId);

        Assert.Equal(1, attempts);
    }

    /// <summary>
    /// A refund is raised, sent, and <c>refund.processed</c> reconciles against the payment's cached
    /// totals: <c>Σ captured − Σ refunded</c> agrees.
    /// </summary>
    [Fact]
    public async Task A_refund_is_recorded_and_reconciles()
    {
        SkipWithoutDocker();

        Factory.Features["identity.mobile-otp-login"] = true;
        var admin = await SignedInAdministratorAsync();
        var scenario = new PaymentsScenario(admin, Cancellation);
        var offer = await scenario.OfferAsync(price: 1200m);

        var (shopper, _) = await SignedInShopperAsync();
        var (order, _) = await scenario.PlaceOrderAsync(shopper, offer);

        var paid = Factory.Gateway.PayOrder(order.ProviderOrderId!, order.Amount);

        var captured = Webhook.PaymentCaptured(order.ProviderOrderId!, paid.ProviderPaymentId, order.Amount);

        await PostRawAsync(
            CreateClient(),
            "/api/v1/webhooks/razorpay",
            captured,
            ("X-Razorpay-Signature", Webhook.Sign(captured)));

        await OutboxDrain.RunAsync(Factory, Database, Cancellation);
        await GatewayEventDrain.RunOnceAsync(Factory, Cancellation);

        var rows = await Database.RowsAsync(
            "SELECT id FROM payments.payments WHERE order_id = $1 AND provider <> 'internal_cod'",
            Cancellation,
            order.OrderId);

        var paymentId = (Guid)rows[0]["id"]!;

        var refunded = await ReadAsync(
            await PostRawAsync(
                admin,
                $"/api/v1/admin/payments/{paymentId}/refunds",
                """{"amount":500,"reason":"Customer changed their mind.","subOrderId":null,"speed":null}""",
                ("Idempotency-Key", $"refund:{paymentId}:1")));

        Assert.Equal("Processed", refunded.GetProperty("status").GetString());

        var totals = await Database.RowsAsync(
            "SELECT amount_captured, amount_refunded FROM payments.payments WHERE id = $1",
            Cancellation,
            paymentId);

        var capturedTotal = (decimal)totals[0]["amount_captured"]!;
        var refundedTotal = (decimal)totals[0]["amount_refunded"]!;

        Assert.Equal(500m, refundedTotal);
        Assert.Equal(order.Amount, capturedTotal);

        var summed = await Database.ScalarAsync<decimal>(
            "SELECT COALESCE(SUM(amount), 0) FROM payments.refunds WHERE payment_id = $1 AND status = 'Processed'",
            Cancellation,
            paymentId);

        Assert.Equal(refundedTotal, summed);
    }

    /// <summary>
    /// A capture with no webhook delivered is found by the reconciliation sweep, confirms the order,
    /// and leaves an attempt whose source is <c>Reconciliation</c>.
    /// </summary>
    [Fact]
    public async Task A_dropped_webhook_is_recovered_by_reconciliation()
    {
        SkipWithoutDocker();

        Factory.Features["identity.mobile-otp-login"] = true;
        var admin = await SignedInAdministratorAsync();
        var scenario = new PaymentsScenario(admin, Cancellation);
        var offer = await scenario.OfferAsync();

        var (shopper, _) = await SignedInShopperAsync();
        var (order, _) = await scenario.PlaceOrderAsync(shopper, offer);

        // The gateway took the money, and — deliberately — no webhook is ever delivered.
        Factory.Gateway.PayOrder(order.ProviderOrderId!, order.Amount);

        // Backdated past the grace period rather than waiting it out: the sweep only examines a
        // collection that has been open longer than `Payments:ReconciliationGraceMinutes`.
        await Database.ExecuteAsync(
            "UPDATE payments.payments SET opened_at = opened_at - interval '1 hour' WHERE order_id = $1",
            Cancellation,
            order.OrderId);

        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PaymentReconciliationService>();

        var swept = await service.SweepAsync(Cancellation);

        Assert.True(swept.IsSuccess);
        Assert.True(swept.Value.Recovered >= 1);

        var read = await ReadAsync(
            await shopper.GetAsync(
                new Uri($"/api/v1/store/orders/{order.OrderId}", UriKind.Relative),
                Cancellation));

        Assert.Equal("Confirmed", read.GetProperty("subOrders")[0].GetProperty("status").GetString());

        var source = await Database.ScalarAsync<string>(
            "SELECT source FROM payments.payment_attempts a JOIN payments.payments p ON p.id = a.payment_id "
            + "WHERE p.order_id = $1 AND a.status = 'captured' ORDER BY a.attempted_at DESC LIMIT 1",
            Cancellation,
            order.OrderId);

        Assert.Equal("Reconciliation", source);
    }
}
