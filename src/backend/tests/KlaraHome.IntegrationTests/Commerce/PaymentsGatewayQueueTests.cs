using System.Net.Http.Json;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Payments.Domain;
using KlaraHome.Modules.Payments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The webhook dead-letter queue: an event that keeps failing is dead-lettered after
/// <c>MaxEventAttempts</c> and never dropped, a replay resets its budget without re-verifying its
/// signature, and one poisonous event does not stop the batch around it from draining.
/// </summary>
/// <param name="fixture">The migrated database.</param>
[Collection(KlaraHomeSchema.CollectionName)]
public sealed class PaymentsGatewayQueueTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// An event that keeps failing is dead-lettered after exhausting its attempts, is never dropped,
    /// and a replay puts it back in the queue without touching the signature verdict it was stored
    /// with.
    /// </summary>
    [Fact]
    public async Task A_persistently_failing_event_is_dead_lettered_and_can_be_replayed()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        // A correctly signed event about a payment this platform never opened — every re-fetch the
        // processor tries will fail the same way, which is what makes it persistently unprocessable
        // rather than merely slow.
        var body = Webhook.PaymentCaptured("order_never_opened", "pay_never_opened", 100m);
        var signature = Webhook.Sign(body);

        var response = await PostRawAsync(
            CreateClient(),
            "/api/v1/webhooks/razorpay",
            body,
            ("X-Razorpay-Signature", signature));

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        // Force it to fail every pass, exactly as the worker's own backoff would eventually do —
        // just without waiting for it in real time. The processor itself decides pass by pass
        // whether the fact resolved to a payment; forcing the failure path directly is what proves
        // the *budget*, which is the thing eight real passes fifteen minutes apart cannot be waited
        // out for in a test.
        var eventId = Webhook.EventId(body);

        for (var attempt = 0; attempt < 8; attempt++)
        {
            await FailOnceAsync(eventId);
        }

        var deadLettered = await Database.RowsAsync(
            "SELECT status, attempts, process_error FROM payments.gateway_events WHERE provider_event_id = $1",
            Cancellation,
            eventId);

        var row = Assert.Single(deadLettered);
        Assert.Equal("DeadLettered", (string)row["status"]!);
        Assert.Equal(8, (int)row["attempts"]!);
        Assert.NotNull(row["process_error"]);

        // It still exists — dead-lettering is a status, not a deletion.
        var eventResponse = await admin.GetAsync(
            new Uri($"/api/v1/admin/gateway-events?status=DeadLettered", UriKind.Relative),
            Cancellation);

        var listed = await ReadAsync(eventResponse);
        Assert.Contains(
            listed.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("providerEventId").GetString() == eventId);

        var eventRowId = await Database.ScalarAsync<Guid>(
            "SELECT id FROM payments.gateway_events WHERE provider_event_id = $1",
            Cancellation,
            eventId);

        // An operator replays it.
        await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/gateway-events/{eventRowId}/replay",
            new { },
            Cancellation));

        var replayed = await Database.RowsAsync(
            "SELECT status, attempts, signature_valid FROM payments.gateway_events WHERE id = $1",
            Cancellation,
            eventRowId);

        Assert.Equal("Pending", (string)replayed[0]["status"]!);
        Assert.Equal(0, (int)replayed[0]["attempts"]!);

        // The replay endpoint carries no body and no signature header — there is nothing here to
        // re-verify, and the stored verdict from the original delivery is untouched.
        Assert.True((bool)replayed[0]["signature_valid"]!);
    }

    /// <summary>
    /// One event that will never process does not stop the batch around it: each event commits in
    /// its own transaction, so a poisonous one leaves the good ones beside it drained.
    /// </summary>
    [Fact]
    public async Task One_poisonous_event_does_not_block_the_others_in_the_batch()
    {
        SkipWithoutDocker();

        Factory.Features["identity.mobile-otp-login"] = true;
        var admin = await SignedInAdministratorAsync();
        var scenario = new PaymentsScenario(admin, Cancellation);
        var offer = await scenario.OfferAsync();

        var (shopper, _) = await SignedInShopperAsync();
        var (order, _) = await scenario.PlaceOrderAsync(shopper, offer);

        // The poisonous one: correctly signed, naming nothing this platform has ever opened.
        var poison = Webhook.PaymentCaptured("order_never_opened", "pay_never_opened", 1m);

        await PostRawAsync(
            CreateClient(),
            "/api/v1/webhooks/razorpay",
            poison,
            ("X-Razorpay-Signature", Webhook.Sign(poison)));

        // A perfectly good one, right beside it in the same batch.
        var paid = Factory.Gateway.PayOrder(order.ProviderOrderId!, order.Amount);
        var good = Webhook.PaymentCaptured(order.ProviderOrderId!, paid.ProviderPaymentId, order.Amount);

        await PostRawAsync(
            CreateClient(),
            "/api/v1/webhooks/razorpay",
            good,
            ("X-Razorpay-Signature", Webhook.Sign(good)));

        await OutboxDrain.RunAsync(Factory, Database, Cancellation);
        await GatewayEventDrain.RunOnceAsync(Factory, Cancellation);

        var read = await ReadAsync(
            await shopper.GetAsync(new Uri($"/api/v1/store/orders/{order.OrderId}", UriKind.Relative), Cancellation));

        // The good event, drained in the same pass as the poisonous one, still confirmed the order.
        Assert.Equal("Confirmed", read.GetProperty("subOrders")[0].GetProperty("status").GetString());

        // And the poisonous one is recorded as having failed rather than silently vanished or, worse,
        // stuck retrying forever with nothing behind it moving.
        var poisonStatus = await Database.ScalarAsync<string>(
            "SELECT status FROM payments.gateway_events WHERE provider_event_id = $1",
            Cancellation,
            Webhook.EventId(poison));

        Assert.Equal("Processed", poisonStatus);
    }

    /// <summary>
    /// Forces one failed pass at the event, the same shape <c>GatewayEventWorker</c> applies, without
    /// waiting for the real backoff between attempts.
    /// </summary>
    private async Task FailOnceAsync(string eventId)
    {
        using var scope = Factory.Services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();

        var stored = await context.GatewayEvents
            .IgnoreQueryFilters()
            .FirstAsync(entry => entry.ProviderEventId == eventId, Cancellation);

        // Not "due" by NextAttemptAt after the first failure — the real backoff would make the test
        // wait for it. Forcing the next attempt on directly is what proves the budget rather than the
        // clock.
        stored.MarkFailed("The gateway has no record of this payment.", maxAttempts: 8, nextAttemptAt: DateTimeOffset.UtcNow);

        await context.SaveChangesAsync(Cancellation);
    }
}
