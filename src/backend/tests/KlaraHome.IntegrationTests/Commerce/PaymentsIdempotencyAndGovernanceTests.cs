using System.Net;
using System.Net.Http.Json;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The idempotency and maker-checker rows: a replayed placement opens one collection, two racing
/// retries cannot open two open ones, a duplicate refund request moves money once, and a refund
/// above the threshold cannot be sent without a second, different approver.
/// </summary>
/// <param name="fixture">The migrated database.</param>
[Collection(KlaraHomeSchema.CollectionName)]
public sealed class PaymentsIdempotencyAndGovernanceTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// A replayed <c>place-order</c> under one <c>Idempotency-Key</c> opens one gateway order and
    /// returns the same instruction; a genuinely concurrent pair cannot open two open collections
    /// against one order — the partial unique index refuses the second.
    /// </summary>
    [Fact]
    public async Task A_replayed_placement_opens_one_collection()
    {
        SkipWithoutDocker();

        Factory.Features["identity.mobile-otp-login"] = true;
        var admin = await SignedInAdministratorAsync();
        var scenario = new PaymentsScenario(admin, Cancellation);
        var offer = await scenario.OfferAsync();

        var (shopper, _) = await SignedInShopperAsync();

        await scenario.AddressAsync(shopper);

        await ReadAsync(await shopper.PostAsJsonAsync(
            "/api/v1/store/cart/items",
            new { listingId = offer.ListingId, quantity = 1 },
            Cancellation));

        var checkout = await ReadAsync(
            await shopper.PostAsJsonAsync("/api/v1/store/checkout", new { }, Cancellation));

        var checkoutId = checkout.GetProperty("id").GetGuid();

        var addresses = await ReadAsync(
            await shopper.GetAsync(new Uri("/api/v1/store/me/addresses", UriKind.Relative), Cancellation));

        var addressId = addresses.EnumerateArray().First().GetProperty("id").GetGuid();

        await ReadAsync(await shopper.PutAsJsonAsync(
            $"/api/v1/store/checkout/{checkoutId}/address",
            new { shippingAddressId = addressId, billingAddressId = (Guid?)null, gstin = (string?)null },
            Cancellation));

        var options = await ReadAsync(
            await shopper.GetAsync(
                new Uri($"/api/v1/store/checkout/{checkoutId}/shipping-options", UriKind.Relative),
                Cancellation));

        var perVendor = options.EnumerateArray()
            .Select(vendor => new
            {
                vendorId = vendor.GetProperty("vendorId").GetGuid(),
                optionCode = vendor.GetProperty("options").EnumerateArray().First().GetProperty("code").GetString(),
            })
            .ToArray();

        await ReadAsync(await shopper.PutAsJsonAsync(
            $"/api/v1/store/checkout/{checkoutId}/shipping",
            new { perVendor },
            Cancellation));

        await ReadAsync(await shopper.PutAsJsonAsync(
            $"/api/v1/store/checkout/{checkoutId}/payment-method",
            new { method = "prepaid" },
            Cancellation));

        var key = $"place:{Guid.NewGuid():N}";

        // Two genuinely concurrent retries — a client that resent before either had answered — sent
        // together rather than one after the other: a checkout session closes on its first success,
        // so a *sequential* replay is refused at the session rather than proving anything about the
        // idempotency key underneath it. Racing them is what actually exercises the partial unique
        // index.
        var responses = await Task.WhenAll(
            scenario.ReplayPlaceOrderAsync(shopper, checkoutId, key),
            scenario.ReplayPlaceOrderAsync(shopper, checkoutId, key));

        var succeeded = responses.Where(response => response.IsSuccessStatusCode).ToArray();
        Assert.NotEmpty(succeeded);

        var orderId = (await ReadAsync(succeeded[0])).GetProperty("orderId").GetGuid();

        // Exactly one collection exists for the order, however many of the racing requests
        // succeeded — the partial unique index on (tenant, order, open status) is what makes this
        // true regardless of how the race actually interleaved.
        var openCollections = await Database.CountAsync(
            "SELECT COUNT(*) FROM payments.payments WHERE order_id = $1",
            Cancellation,
            orderId);

        Assert.Equal(1, openCollections);

        // Only one gateway order was ever opened against the fake.
        Assert.Single(Factory.Gateway.Intents);
    }

    /// <summary>
    /// A duplicate refund request under one <c>Idempotency-Key</c> returns the original refund and
    /// sends the gateway one refund, not two.
    /// </summary>
    [Fact]
    public async Task A_duplicate_refund_request_moves_money_once()
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

        var paymentId = await Database.ScalarAsync<Guid>(
            "SELECT id FROM payments.payments WHERE order_id = $1 AND provider <> 'internal_cod'",
            Cancellation,
            order.OrderId);

        var key = $"refund:{Guid.NewGuid():N}";
        const string RequestBody =
            """{"amount":300,"reason":"Duplicate submit from the support console.","subOrderId":null,"speed":null}""";

        var first = await ReadAsync(
            await PostRawAsync(
                admin,
                $"/api/v1/admin/payments/{paymentId}/refunds",
                RequestBody,
                ("Idempotency-Key", key)));

        var second = await ReadAsync(
            await PostRawAsync(
                admin,
                $"/api/v1/admin/payments/{paymentId}/refunds",
                RequestBody,
                ("Idempotency-Key", key)));

        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());

        var refundCount = await Database.CountAsync(
            "SELECT COUNT(*) FROM payments.refunds WHERE payment_id = $1",
            Cancellation,
            paymentId);

        Assert.Equal(1, refundCount);

        // The gateway itself was asked for this key exactly once — the second request never
        // reached it.
        Assert.Equal(1, Factory.Gateway.RefundAttempts.GetValueOrDefault(key));

        var refundedTotal = await Database.ScalarAsync<decimal>(
            "SELECT amount_refunded FROM payments.payments WHERE id = $1",
            Cancellation,
            paymentId);

        Assert.Equal(300m, refundedTotal);
    }

    /// <summary>
    /// A refund above the threshold cannot be sent without a second, different approver — refused
    /// by the handler, and independently by the <c>ck_refunds_approver</c> check constraint proven
    /// in <see cref="PaymentsConstraintTests"/>.
    /// </summary>
    [Fact]
    public async Task A_large_refund_waits_for_a_second_different_approver()
    {
        SkipWithoutDocker();

        Factory.Features["identity.mobile-otp-login"] = true;
        var admin = await SignedInAdministratorAsync();
        var scenario = new PaymentsScenario(admin, Cancellation);

        // Above the default 5,000 threshold — the listing's own declared MRP lets the offer clear it.
        var offer = await scenario.OfferAsync(price: 6000m);

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

        var raised = await ReadAsync(
            await PostRawAsync(
                admin,
                $"/api/v1/admin/payments/{paymentId}/refunds",
                """{"amount":6000,"reason":"A large refund needing a second signature.","subOrderId":null,"speed":null}""",
                ("Idempotency-Key", $"refund:{Guid.NewGuid():N}")));

        Assert.Equal("Requested", raised.GetProperty("status").GetString());
        Assert.True(raised.GetProperty("requiresApproval").GetBoolean());

        var refundId = raised.GetProperty("id").GetGuid();

        // The nightly gateway never saw it — it is not approved, so nothing was sent.
        Assert.Empty(Factory.Gateway.RefundAttempts);

        // The same person who raised it cannot approve it.
        var selfApproval = await admin.PostAsJsonAsync(
            $"/api/v1/admin/refunds/{refundId}/approve",
            new { },
            Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, selfApproval.StatusCode);

        var stillRequested = await Database.ScalarAsync<string>(
            "SELECT status FROM payments.refunds WHERE id = $1",
            Cancellation,
            refundId);

        Assert.Equal("Requested", stillRequested);

        // A different platform administrator signs it off.
        var secondEmail = NewEmail("second-approver");
        const string SecondPassword = "a-different-approver-signs-off-1!";

        await ReadAsync(await admin.PostAsJsonAsync(
            "/api/v1/admin/users",
            new
            {
                email = secondEmail,
                mobile = (string?)null,
                userType = "Staff",
                roleCodes = new[] { "platform-admin" },
                vendorId = (Guid?)null,
            },
            Cancellation));

        await SetPasswordAsync(secondEmail, SecondPassword);

        var secondAdmin = CreateClient();

        await TestSignIn.SignInAsync(secondAdmin, "admin", secondEmail, SecondPassword, Cancellation);

        var approved = await ReadAsync(await secondAdmin.PostAsJsonAsync(
            $"/api/v1/admin/refunds/{refundId}/approve",
            new { },
            Cancellation));

        Assert.Equal("Processed", approved.GetProperty("status").GetString());
        Assert.Single(Factory.Gateway.RefundAttempts);
    }
}
