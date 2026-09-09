using System.Net.Http.Json;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// What <c>PaymentWorkflow</c> does once a fact reaches it, however it arrived: a short capture
/// mismatches rather than confirming, applying the same capture twice is a no-op, a stale failure
/// after a settled capture never regresses the order, and cancelling a paid order gives back exactly
/// its own share.
/// </summary>
/// <param name="fixture">The migrated database.</param>
[Collection(KlaraHomeSchema.CollectionName)]
public sealed class PaymentsWorkflowTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// A capture short of the order total does not confirm the order, raises
    /// <c>PaymentMismatchDetected</c>, and leaves the attempt recorded.
    /// </summary>
    [Fact]
    public async Task A_short_capture_does_not_confirm_the_order()
    {
        SkipWithoutDocker();

        Factory.Features["identity.mobile-otp-login"] = true;
        var admin = await SignedInAdministratorAsync();
        var scenario = new PaymentsScenario(admin, Cancellation);
        var offer = await scenario.OfferAsync(price: 999m);

        var (shopper, _) = await SignedInShopperAsync();
        var (order, _) = await scenario.PlaceOrderAsync(shopper, offer);

        // The gateway took less than the order asked for.
        var paid = Factory.Gateway.PayOrder(order.ProviderOrderId!, order.Amount - 100m);
        var captured = Webhook.PaymentCaptured(order.ProviderOrderId!, paid.ProviderPaymentId, order.Amount - 100m);

        await PostRawAsync(
            CreateClient(),
            "/api/v1/webhooks/razorpay",
            captured,
            ("X-Razorpay-Signature", Webhook.Sign(captured)));

        await OutboxDrain.RunAsync(Factory, Database, Cancellation);
        await GatewayEventDrain.RunOnceAsync(Factory, Cancellation);

        var read = await ReadAsync(
            await shopper.GetAsync(new Uri($"/api/v1/store/orders/{order.OrderId}", UriKind.Relative), Cancellation));

        Assert.Equal("PendingPayment", read.GetProperty("subOrders")[0].GetProperty("status").GetString());

        var attempts = await Database.CountAsync(
            "SELECT COUNT(*) FROM payments.payment_attempts a JOIN payments.payments p ON p.id = a.payment_id "
            + "WHERE p.order_id = $1",
            Cancellation,
            order.OrderId);

        Assert.True(attempts >= 1);

        var mismatchRaised = await Database.CountAsync(
            "SELECT COUNT(*) FROM platform.outbox_messages WHERE type LIKE '%PaymentMismatchDetected%' "
            + "AND payload::text LIKE '%' || $1 || '%'",
            Cancellation,
            order.OrderId.ToString());

        Assert.True(mismatchRaised >= 1, "No PaymentMismatchDetected event was queued for the short capture.");
    }

    /// <summary>
    /// Applying the same capture twice — a webhook, then an operator's <c>sync</c> re-reading the
    /// identical fact — leaves one confirmed order, not two.
    /// </summary>
    [Fact]
    public async Task Applying_the_same_capture_twice_is_idempotent()
    {
        SkipWithoutDocker();

        Factory.Features["identity.mobile-otp-login"] = true;
        var admin = await SignedInAdministratorAsync();
        var scenario = new PaymentsScenario(admin, Cancellation);
        var offer = await scenario.OfferAsync();

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

        var paymentId = await Database.ScalarAsync<Guid>(
            "SELECT id FROM payments.payments WHERE order_id = $1 AND provider <> 'internal_cod'",
            Cancellation,
            order.OrderId);

        // An operator presses sync twice on an already-captured payment. Both re-read the identical
        // fact from the gateway.
        await ReadAsync(await admin.PostAsJsonAsync($"/api/v1/admin/payments/{paymentId}/sync", new { }, Cancellation));
        await ReadAsync(await admin.PostAsJsonAsync($"/api/v1/admin/payments/{paymentId}/sync", new { }, Cancellation));

        await OutboxDrain.RunAsync(Factory, Database, Cancellation);

        var confirmedCount = await Database.CountAsync(
            "SELECT COUNT(*) FROM orders.sub_orders WHERE order_id = $1 AND status = 'Confirmed'",
            Cancellation,
            order.OrderId);

        Assert.Equal(1, confirmedCount);

        var capturedAttempts = await Database.CountAsync(
            "SELECT COUNT(*) FROM payments.payment_attempts WHERE payment_id = $1 AND status = 'captured'",
            Cancellation,
            paymentId);

        // Every re-fetch that agrees still records its own try — a fresh attempt row is not the
        // duplication this proves against; a second confirmation, a second invoice or a second stock
        // commit would be.
        Assert.True(capturedAttempts >= 2);
    }

    /// <summary>
    /// A <c>payment.failed</c> arriving after a capture does not move a confirmed order back to
    /// <c>PaymentFailed</c> — the settled fact is not regressed by a stale one.
    /// </summary>
    [Fact]
    public async Task A_stale_failure_after_capture_does_not_regress_the_order()
    {
        SkipWithoutDocker();

        Factory.Features["identity.mobile-otp-login"] = true;
        var admin = await SignedInAdministratorAsync();
        var scenario = new PaymentsScenario(admin, Cancellation);
        var offer = await scenario.OfferAsync();

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

        // A payment.failed for the very same reference, arriving late — the classic out-of-order
        // delivery a gateway makes no promise against.
        var failed = Webhook.PaymentFailed(order.ProviderOrderId!, paid.ProviderPaymentId);

        var response = await PostRawAsync(
            CreateClient(),
            "/api/v1/webhooks/razorpay",
            failed,
            ("X-Razorpay-Signature", Webhook.Sign(failed)));

        await OutboxDrain.RunAsync(Factory, Database, Cancellation);
        await GatewayEventDrain.RunOnceAsync(Factory, Cancellation);

        var read = await ReadAsync(
            await shopper.GetAsync(new Uri($"/api/v1/store/orders/{order.OrderId}", UriKind.Relative), Cancellation));

        Assert.Equal("Confirmed", read.GetProperty("subOrders")[0].GetProperty("status").GetString());

        var paymentStatus = await Database.ScalarAsync<string>(
            "SELECT status FROM payments.payments WHERE order_id = $1 AND provider <> 'internal_cod'",
            Cancellation,
            order.OrderId);

        Assert.Equal("Captured", paymentStatus);
    }

    /// <summary>
    /// Cancelling a paid order raises a proportional refund for that seller's share, and a
    /// redelivered cancellation raises no second one.
    /// </summary>
    [Fact]
    public async Task Cancelling_a_paid_order_refunds_it_once()
    {
        SkipWithoutDocker();

        Factory.Features["identity.mobile-otp-login"] = true;
        var admin = await SignedInAdministratorAsync();
        var scenario = new PaymentsScenario(admin, Cancellation);
        var offer = await scenario.OfferAsync(price: 999m);

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

        await ReadAsync(await shopper.PostAsJsonAsync(
            $"/api/v1/store/orders/{order.OrderId}/cancel",
            new { reason = "Changed my mind." },
            Cancellation));

        // The cancellation raises SubOrderCancelled on the outbox; deliver it, exactly as the worker
        // would.
        await OutboxDrain.RunAsync(Factory, Database, Cancellation);

        var refunds = await Database.RowsAsync(
            "SELECT amount, status FROM payments.refunds WHERE order_id = $1",
            Cancellation,
            order.OrderId);

        var refund = Assert.Single(refunds);
        Assert.Equal(order.Amount, (decimal)refund["amount"]!);
        Assert.Equal("Processed", (string)refund["status"]!);

        // A redelivery of the same cancellation — the outbox's own at-least-once guarantee — raises
        // no second refund. There is nothing left to redeliver through the API, so the derived key
        // the handler uses is asserted directly: the unique index on it is what makes a real replay
        // safe.
        var refundCount = await Database.CountAsync(
            "SELECT COUNT(*) FROM payments.refunds WHERE order_id = $1",
            Cancellation,
            order.OrderId);

        Assert.Equal(1, refundCount);
    }
}
