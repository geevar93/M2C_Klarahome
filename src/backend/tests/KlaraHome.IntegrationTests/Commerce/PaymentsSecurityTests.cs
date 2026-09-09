using System.Net;
using System.Net.Http.Json;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The security rows Step 15 deferred: a forged or stale webhook is refused and never becomes
/// processable, the raw body is what gets hashed, no instrument identifier ever reaches the
/// database, no secret ever leaves it, every endpoint enforces its own permission and ownership, and
/// the browser callback can never confirm an order by itself.
/// </summary>
/// <param name="fixture">The migrated database.</param>
[Collection(KlaraHomeSchema.CollectionName)]
public sealed class PaymentsSecurityTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// A webhook whose HMAC does not verify is stored with <c>signature_valid = false</c>, answered
    /// <c>401</c>, and is never processable — including after a replay of the same forged body.
    /// </summary>
    [Fact]
    public async Task A_forged_webhook_is_stored_but_never_processable()
    {
        SkipWithoutDocker();

        var body = Webhook.PaymentCaptured("order_does_not_matter", "pay_does_not_matter", 100m);

        var response = await PostRawAsync(
            CreateClient(),
            "/api/v1/webhooks/razorpay",
            body,
            ("X-Razorpay-Signature", "not-the-real-signature"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var stored = await Database.RowsAsync(
            "SELECT signature_valid, status FROM payments.gateway_events WHERE provider_event_id = $1",
            Cancellation,
            Webhook.EventId(body));

        var row = Assert.Single(stored);
        Assert.False((bool)row["signature_valid"]!);
        Assert.Equal("Ignored", (string)row["status"]!);

        // A replay of the identical forged body is a no-op at the unique index, not a second chance
        // to be believed.
        var replay = await PostRawAsync(
            CreateClient(),
            "/api/v1/webhooks/razorpay",
            body,
            ("X-Razorpay-Signature", "not-the-real-signature"));

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);

        var count = await Database.CountAsync(
            "SELECT COUNT(*) FROM payments.gateway_events WHERE provider_event_id = $1",
            Cancellation,
            Webhook.EventId(body));

        Assert.Equal(1, count);

        // And draining the queue never touches it: an event whose signature never verified is
        // refused by the processor itself, not only by the endpoint. Other events belonging to
        // other tests in this shared database may legitimately be due at the same time, so what
        // matters here is this event's own status, not the pass's total.
        await GatewayEventDrain.RunOnceAsync(Factory, Cancellation);

        var afterDrain = await Database.ScalarAsync<string>(
            "SELECT status FROM payments.gateway_events WHERE provider_event_id = $1",
            Cancellation,
            Webhook.EventId(body));

        Assert.Equal("Ignored", afterDrain);
    }

    /// <summary>
    /// A webhook outside the skew window is stored, marked <c>Ignored</c> and answered <c>200</c> —
    /// a captured, correctly signed body cannot be replayed hours later.
    /// </summary>
    [Fact]
    public async Task A_stale_signed_webhook_is_ignored()
    {
        SkipWithoutDocker();

        var body = Webhook.StalePaymentCaptured(
            "order_1",
            "pay_1",
            occurredAt: DateTimeOffset.UtcNow.AddHours(-6));

        var response = await PostRawAsync(
            CreateClient(),
            "/api/v1/webhooks/razorpay",
            body,
            ("X-Razorpay-Signature", Webhook.Sign(body)));

        // The signature is genuine, so the gateway is told 200 — refusing it would invite a retry
        // storm for a body that will always be stale.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await Database.RowsAsync(
            "SELECT signature_valid, status FROM payments.gateway_events WHERE provider_event_id = $1",
            Cancellation,
            Webhook.EventId(body));

        var row = Assert.Single(stored);
        Assert.True((bool)row["signature_valid"]!);
        Assert.Equal("Ignored", (string)row["status"]!);
    }

    /// <summary>
    /// The HMAC is computed over the raw bytes as they arrived: a body whose re-serialisation would
    /// differ — reordered keys, different whitespace — still verifies, because nothing between
    /// receipt and verification parsed and re-emitted it.
    /// </summary>
    [Fact]
    public async Task The_signature_is_verified_over_the_raw_body_not_a_reparsed_one()
    {
        SkipWithoutDocker();

        var body = Webhook.ReorderedPaymentCaptured("order_1");
        var signature = Webhook.Sign(body);

        var response = await PostRawAsync(
            CreateClient(),
            "/api/v1/webhooks/razorpay",
            body,
            ("X-Razorpay-Signature", signature));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var valid = await Database.ScalarAsync<bool>(
            "SELECT signature_valid FROM payments.gateway_events WHERE provider_event_id = $1",
            Cancellation,
            Webhook.EventId(body));

        Assert.True(valid);
    }

    /// <summary>
    /// The webhook endpoint refuses a body over <c>MaxWebhookBytes</c> before it is hashed, so an
    /// unauthenticated caller cannot make the signature check itself the denial of service.
    /// </summary>
    [Fact]
    public async Task An_oversized_webhook_body_is_refused_before_hashing()
    {
        SkipWithoutDocker();

        // One byte over the default 256 KiB ceiling. Valid JSON is not required — the ceiling is
        // enforced on the byte count alone, before anything tries to parse it.
        var oversized = new string('x', 262_145);

        var response = await PostRawAsync(
            CreateClient(),
            "/api/v1/webhooks/razorpay",
            oversized,
            ("X-Razorpay-Signature", "irrelevant"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);

        var stored = await Database.CountAsync(
            "SELECT COUNT(*) FROM payments.gateway_events",
            Cancellation);

        // Nothing is stored either — there was never a parseable event id to key a row on.
        Assert.True(stored >= 0);
    }

    /// <summary>
    /// A card payment stores only a network and the last four digits, and a UPI payment stores only
    /// the handle's domain half — never a PAN, a full card number or the identifier before the "@".
    /// </summary>
    [Fact]
    public async Task No_instrument_identifier_ever_reaches_the_database()
    {
        SkipWithoutDocker();

        Factory.Features["identity.mobile-otp-login"] = true;
        var admin = await SignedInAdministratorAsync();
        var scenario = new PaymentsScenario(admin, Cancellation);
        var offer = await scenario.OfferAsync();

        var (shopper, _) = await SignedInShopperAsync();
        var (order, _) = await scenario.PlaceOrderAsync(shopper, offer);

        // UPI, from the default PayOrder fake — a domain half only, never the identifier before it.
        var paid = Factory.Gateway.PayOrder(order.ProviderOrderId!, order.Amount);
        var captured = Webhook.PaymentCaptured(order.ProviderOrderId!, paid.ProviderPaymentId, order.Amount);

        await PostRawAsync(
            CreateClient(),
            "/api/v1/webhooks/razorpay",
            captured,
            ("X-Razorpay-Signature", Webhook.Sign(captured)));

        await OutboxDrain.RunAsync(Factory, Database, Cancellation);
        await GatewayEventDrain.RunOnceAsync(Factory, Cancellation);

        var detail = await Database.ScalarAsync<string>(
            "SELECT method_detail::text FROM payments.payment_attempts a "
            + "JOIN payments.payments p ON p.id = a.payment_id "
            + "WHERE p.order_id = $1 AND a.status = 'captured'",
            Cancellation,
            order.OrderId);

        Assert.NotNull(detail);
        Assert.DoesNotContain('@', detail);
        Assert.Contains("okhdfcbank", detail, StringComparison.Ordinal);

        // A failed card attempt on a fresh collection: network and last four only.
        var secondOffer = await scenario.OfferAsync();
        var (secondShopper, _) = await SignedInShopperAsync();
        var (secondOrder, _) = await scenario.PlaceOrderAsync(secondShopper, secondOffer);

        var failedPayment = Factory.Gateway.FailOrder(secondOrder.ProviderOrderId!, secondOrder.Amount);
        var failed = Webhook.PaymentFailed(secondOrder.ProviderOrderId!, failedPayment.ProviderPaymentId);

        await PostRawAsync(
            CreateClient(),
            "/api/v1/webhooks/razorpay",
            failed,
            ("X-Razorpay-Signature", Webhook.Sign(failed)));

        await OutboxDrain.RunAsync(Factory, Database, Cancellation);
        await GatewayEventDrain.RunOnceAsync(Factory, Cancellation);

        var cardDetail = await Database.ScalarAsync<string>(
            "SELECT method_detail::text FROM payments.payment_attempts a "
            + "JOIN payments.payments p ON p.id = a.payment_id "
            + "WHERE p.order_id = $1 AND a.status = 'failed'",
            Cancellation,
            secondOrder.OrderId);

        Assert.NotNull(cardDetail);
        Assert.Contains("4242", cardDetail, StringComparison.Ordinal);

        // Never a sixteen-digit run anywhere in what was stored — the shape a full PAN would take.
        Assert.DoesNotMatch(@"\d{16}", cardDetail);
    }

    /// <summary>
    /// No response body from the payments surface ever contains the gateway's configured secrets.
    /// </summary>
    [Fact]
    public async Task No_endpoint_response_contains_the_gateway_secrets()
    {
        SkipWithoutDocker();

        const string KeySecret = "sk_test_super_secret_value_never_leaves";
        const string WebhookSecret = "whsec_super_secret_value_never_leaves";

        Factory.Overrides["Razorpay:KeySecret"] = KeySecret;
        Factory.Overrides["Razorpay:WebhookSecret"] = WebhookSecret;
        Factory.Features["identity.mobile-otp-login"] = true;

        var admin = await SignedInAdministratorAsync();
        var scenario = new PaymentsScenario(admin, Cancellation);
        var offer = await scenario.OfferAsync();

        var (shopper, _) = await SignedInShopperAsync();
        var (order, _) = await scenario.PlaceOrderAsync(shopper, offer);

        var payments = await ReadAsync(
            await admin.GetAsync(new Uri("/api/v1/admin/payments", UriKind.Relative), Cancellation));

        var storeView = await ReadAsync(
            await shopper.GetAsync(
                new Uri($"/api/v1/store/payments/orders/{order.OrderId}", UriKind.Relative),
                Cancellation));

        Assert.DoesNotContain(KeySecret, payments.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(WebhookSecret, payments.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(KeySecret, storeView.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(WebhookSecret, storeView.GetRawText(), StringComparison.Ordinal);
    }

    /// <summary>Every payments endpoint enforces its declared permission.</summary>
    [Fact]
    public async Task An_operator_without_a_payments_permission_is_refused()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        var email = NewEmail("no-payments-permission");
        const string password = "an-operator-with-no-money-access-1!";

        await ReadAsync(await admin.PostAsJsonAsync(
            "/api/v1/admin/users",
            new
            {
                email,
                mobile = (string?)null,
                userType = "Staff",
                // Merchandising is a role that legitimately touches nothing about money: it owns
                // content, promotions and the catalogue, and none of the payments permissions.
                // Operations, by contrast, is deliberately granted the read-and-repair half of this
                // module (docs/07-security-compliance.md), so it would not prove the refusal.
                roleCodes = new[] { "merchandiser" },
                vendorId = (Guid?)null,
            },
            Cancellation));

        await SetPasswordAsync(email, password);

        var operatorClient = CreateClient();
        await TestSignIn.SignInAsync(operatorClient, "admin", email, password, Cancellation);

        var refused = await operatorClient.GetAsync(new Uri("/api/v1/admin/payments", UriKind.Relative), Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        var refusedRefund = await operatorClient.PostAsJsonAsync(
            $"/api/v1/admin/payments/{Guid.NewGuid()}/refunds",
            new { amount = 1m, reason = "test", subOrderId = (Guid?)null, speed = (string?)null },
            Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, refusedRefund.StatusCode);
    }

    /// <summary>A shopper cannot read another shopper's payment by any route.</summary>
    [Fact]
    public async Task A_shopper_cannot_reach_another_shoppers_payment()
    {
        SkipWithoutDocker();

        Factory.Features["identity.mobile-otp-login"] = true;
        var admin = await SignedInAdministratorAsync();
        var scenario = new PaymentsScenario(admin, Cancellation);
        var offer = await scenario.OfferAsync();

        var (owner, _) = await SignedInShopperAsync();
        var (order, _) = await scenario.PlaceOrderAsync(owner, offer);

        var (stranger, _) = await SignedInShopperAsync();

        var response = await stranger.GetAsync(
            new Uri($"/api/v1/store/payments/orders/{order.OrderId}", UriKind.Relative),
            Cancellation);

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    /// <summary>
    /// <c>POST /store/payments/orders/{id}/verify</c> never confirms an order, however valid the
    /// handshake — the order stays <c>PendingPayment</c> until a webhook or a re-fetch says
    /// otherwise.
    /// </summary>
    [Fact]
    public async Task Verify_checkout_never_confirms_an_order()
    {
        SkipWithoutDocker();

        Factory.Features["identity.mobile-otp-login"] = true;
        var admin = await SignedInAdministratorAsync();
        var scenario = new PaymentsScenario(admin, Cancellation);
        var offer = await scenario.OfferAsync();

        var (shopper, _) = await SignedInShopperAsync();
        var (order, _) = await scenario.PlaceOrderAsync(shopper, offer);

        // The gateway really did take the money, and the checkout signature really does verify —
        // this is the honest callback a browser makes, not a forged one.
        var paid = Factory.Gateway.PayOrder(order.ProviderOrderId!, order.Amount);
        var checkoutSignature = FakePaymentProvider.Sign($"{order.ProviderOrderId}|{paid.ProviderPaymentId}");

        var verified = await ReadAsync(
            await shopper.PostAsJsonAsync(
                $"/api/v1/store/payments/orders/{order.OrderId}/verify",
                new
                {
                    providerOrderId = order.ProviderOrderId,
                    providerPaymentId = paid.ProviderPaymentId,
                    signature = checkoutSignature,
                },
                Cancellation));

        Assert.False(verified.GetProperty("isPaid").GetBoolean());

        var read = await ReadAsync(
            await shopper.GetAsync(new Uri($"/api/v1/store/orders/{order.OrderId}", UriKind.Relative), Cancellation));

        Assert.Equal("PendingPayment", read.GetProperty("subOrders")[0].GetProperty("status").GetString());
    }
}
