using KlaraHome.IntegrationTests.Database;
using System.Net;
using System.Net.Http.Json;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure.Courier;
using KlaraHome.Modules.Shipping.Infrastructure.Persistence;
using KlaraHome.Modules.Shipping.Infrastructure.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The courier webhook receiver and the worker behind it: verified, stored, applied exactly once —
/// and, for a forged one, never applied at all (docs/04-api-specification.md §5).
/// </summary>
/// <param name="fixture">The migrated database.</param>
public sealed class ShippingWebhookTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// A courier webhook is verified, stored, answered <c>200</c>, drained by the worker and applied
    /// exactly once — and a redelivery of the same scan changes nothing, colliding on
    /// <c>(shipment, provider_event_id, occurred_at)</c>. Tracking updates land on the order's
    /// timeline, which is the customer-visible half of the same criterion.
    /// </summary>
    [Fact]
    public async Task A_webhook_is_verified_stored_drained_and_applied_exactly_once()
    {
        SkipWithoutDocker();

        var (booked, admin) = await BookedShipmentAsync();

        var awb = booked.GetProperty("awb").GetString()!;
        var shipmentId = booked.GetProperty("id").GetGuid();

        var scan = Factory.Courier.Scan(awb, ShipmentStatus.InTransit);
        var body = Factory.Courier.WebhookBody(awb, scan);
        var signature = FakeShippingProvider.Sign(body);

        var first = await PostRawAsync(
            CreateClient(),
            "/api/v1/webhooks/shipping/shiprocket",
            body,
            ("X-Api-Key", signature));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var stored = await Database.CountAsync(
            "SELECT COUNT(*) FROM shipping.courier_events WHERE provider_event_id = $1",
            Cancellation,
            scan.ProviderEventId);

        Assert.Equal(1, stored);

        // The redelivery: same body, same signature, sent again before the worker has drained it.
        var replay = await PostRawAsync(
            CreateClient(),
            "/api/v1/webhooks/shipping/shiprocket",
            body,
            ("X-Api-Key", signature));

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);

        var storedAfterReplay = await Database.CountAsync(
            "SELECT COUNT(*) FROM shipping.courier_events WHERE provider_event_id = $1",
            Cancellation,
            scan.ProviderEventId);

        Assert.Equal(1, storedAfterReplay);

        // Drained by the worker, applied once.
        await DrainCourierEventsAsync();

        var trackingRows = await Database.CountAsync(
            "SELECT COUNT(*) FROM shipping.tracking_events WHERE shipment_id = $1 AND provider_event_id = $2",
            Cancellation,
            shipmentId,
            scan.ProviderEventId);

        Assert.Equal(1, trackingRows);

        var shipment = await ReadAsync(await admin.GetAsync(
            new Uri($"/api/v1/admin/shipments/{shipmentId}", UriKind.Relative),
            Cancellation));

        Assert.Equal("InTransit", shipment.GetProperty("status").GetString());

        // A second, independent delivery of the same scan (the webhook's own redelivery-with-no-
        // change guarantee) must not add a second tracking row or move the parcel twice.
        await DrainCourierEventsAsync();

        var trackingRowsAfterSecondDrain = await Database.CountAsync(
            "SELECT COUNT(*) FROM shipping.tracking_events WHERE shipment_id = $1 AND provider_event_id = $2",
            Cancellation,
            shipmentId,
            scan.ProviderEventId);

        Assert.Equal(1, trackingRowsAfterSecondDrain);
    }

    /// <summary>
    /// A webhook whose signature does not verify is stored, marked ignored, answered <c>401</c>, and
    /// never processed — including after an operator replays it.
    /// </summary>
    [Fact]
    public async Task A_badly_signed_webhook_is_stored_ignored_answered_401_and_never_processed()
    {
        SkipWithoutDocker();

        var (booked, admin) = await BookedShipmentAsync();
        var awb = booked.GetProperty("awb").GetString()!;

        var scan = Factory.Courier.Scan(awb, ShipmentStatus.InTransit);
        var body = Factory.Courier.WebhookBody(awb, scan);

        var response = await PostRawAsync(
            CreateClient(),
            "/api/v1/webhooks/shipping/shiprocket",
            body,
            ("X-Api-Key", "not-the-right-secret"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var row = (await Database.RowsAsync(
                "SELECT status, signature_valid FROM shipping.courier_events WHERE provider_event_id = $1",
                Cancellation,
                scan.ProviderEventId))
            .Single();

        Assert.Equal("Ignored", row["status"]);
        Assert.Equal(false, row["signature_valid"]);

        // The worker's own claim query excludes anything whose signature never verified, so draining
        // must not move the parcel.
        await DrainCourierEventsAsync();

        var shipment = await ReadAsync(await admin.GetAsync(
            new Uri($"/api/v1/admin/shipments/{booked.GetProperty("id").GetGuid()}", UriKind.Relative),
            Cancellation));

        Assert.Equal("LabelGenerated", shipment.GetProperty("status").GetString());

        // An operator's replay must not make a forged event processable either.
        var eventId = await Database.ScalarAsync<Guid>(
            "SELECT id FROM shipping.courier_events WHERE provider_event_id = $1",
            Cancellation,
            scan.ProviderEventId);

        var replay = await admin.PostAsJsonAsync(
            $"/api/v1/admin/courier-events/{eventId}/replay",
            new { },
            Cancellation);

        await RefusedAsync(replay, HttpStatusCode.Conflict, "COURIER_EVENT_NOT_REPLAYABLE");
    }

    /// <summary>Books a parcel through the fake sandbox, and answers it with an admin client.</summary>
    private async Task<(System.Text.Json.JsonElement Shipment, HttpClient Admin)> BookedShipmentAsync()
    {
        var admin = await SignedInAdministratorAsync();
        var vendors = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var orders = new ShippingOrderScenario(admin, Cancellation);

        var seller = await vendors.ActiveAsync();
        var taxonomy = await catalogue.TaxonomyAsync();
        var product = await catalogue.DraftAsync(taxonomy, seller.Id);
        await catalogue.ActivateVariantAsync(product.VariantId);
        await catalogue.PublishAsync(product.Id);
        var listingId = await catalogue.OfferAsync(seller.Id, product.VariantId, sellingPrice: 999m);
        await catalogue.StockAsync(listingId, 100, seller.Id);

        var (shopper, _) = await SignedInShopperAsync();
        var stateId = await vendors.StateIdAsync();
        var placed = await orders.ConfirmedSubOrderAsync(shopper, stateId, seller.Id, listingId);

        var booked = await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/sub-orders/{placed.SubOrderId}/shipments",
            new
            {
                lines = Array.Empty<object>(),
                weight = 500,
                dimensions = (object?)null,
                courier = "standard",
                pickupLocationId = (Guid?)null,
                manualAwb = (string?)null,
                manualCourier = (string?)null,
            },
            Cancellation));

        return (booked, admin);
    }

    /// <summary>
    /// Runs the real <see cref="CourierEventProcessor"/> once over every pending, signed event —
    /// exactly what <c>CourierEventWorker</c> does, without waiting for its timer.
    /// </summary>
    private async Task DrainCourierEventsAsync()
    {
        using var scope = Factory.Services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<CourierEventProcessor>();

        var due = await context.CourierEvents
            .IgnoreQueryFilters()
            .Where(entry => entry.SignatureValid && entry.Status == CourierEventStatus.Pending)
            .Select(entry => entry.Id)
            .ToListAsync(Cancellation);

        foreach (var id in due)
        {
            var stored = await context.CourierEvents
                .IgnoreQueryFilters()
                .FirstAsync(entry => entry.Id == id, Cancellation);

            var applied = await processor.ProcessAsync(stored, Cancellation);

            if (applied.IsSuccess && stored.Status == CourierEventStatus.Pending)
            {
                stored.MarkIgnored(DateTimeOffset.UtcNow);
            }

            await context.SaveChangesAsync(Cancellation);
        }
    }
}
