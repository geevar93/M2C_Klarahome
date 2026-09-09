using KlaraHome.IntegrationTests.Database;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure.Courier;
using KlaraHome.Modules.Shipping.Infrastructure.Persistence;
using KlaraHome.Modules.Shipping.Infrastructure.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The two TEST_DEBT rows about cash a courier is carrying: a delivery scan settling it, and a
/// courier's remittance file being apportioned across the parcels it covers — both through
/// <c>ICodCollections</c>, in the Payments schema, which is the seam Shipping is allowed to touch it
/// through (docs/08-integrations.md §2).
/// </summary>
/// <param name="fixture">The migrated database.</param>
public sealed class ShippingCodTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// A delivery scan on a cash-on-delivery parcel marks the <c>payments.cod_collections</c> row
    /// collected, and a return to origin waives it.
    /// </summary>
    [Fact]
    public async Task A_delivery_scan_collects_cash_and_a_return_to_origin_waives_it()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var vendors = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var orders = new ShippingOrderScenario(admin, Cancellation);
        var stateId = await vendors.StateIdAsync();

        var seller = await vendors.ActiveAsync();
        var taxonomy = await catalogue.TaxonomyAsync();

        // Delivered.
        {
            var product = await catalogue.DraftAsync(taxonomy, seller.Id);
            await catalogue.ActivateVariantAsync(product.VariantId);
            await catalogue.PublishAsync(product.Id);
            var listingId = await catalogue.OfferAsync(seller.Id, product.VariantId, sellingPrice: 999m);
            await catalogue.StockAsync(listingId, 100, seller.Id);

            var (shopper, _) = await SignedInShopperAsync();
            var placed = await orders.ConfirmedSubOrderAsync(shopper, stateId, seller.Id, listingId, factory: Factory, database: Database);

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

            var awb = booked.GetProperty("awb").GetString()!;
            var shipmentId = booked.GetProperty("id").GetGuid();

            Assert.NotEqual(JsonValueKind.Null, booked.GetProperty("codAmount").ValueKind);

            await SendAndDrainAsync(awb, ShipmentStatus.Delivered);

            var row = await CodRowAsync(placed.SubOrderId, "Collected");

            Assert.Equal("Collected", row["status"]);
            Assert.NotNull(row["collected_amount"]);
            Assert.True(Convert.ToDecimal(row["collected_amount"]) > 0m);
        }

        // Returned to origin: never delivered, so nothing is owed.
        {
            var product = await catalogue.DraftAsync(taxonomy, seller.Id);
            await catalogue.ActivateVariantAsync(product.VariantId);
            await catalogue.PublishAsync(product.Id);
            var listingId = await catalogue.OfferAsync(seller.Id, product.VariantId, sellingPrice: 799m);
            await catalogue.StockAsync(listingId, 100, seller.Id);

            var (shopper, _) = await SignedInShopperAsync();
            var placed = await orders.ConfirmedSubOrderAsync(shopper, stateId, seller.Id, listingId, factory: Factory, database: Database);

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

            var awb = booked.GetProperty("awb").GetString()!;

            await SendAndDrainAsync(awb, ShipmentStatus.RtoInitiated);
            await SendAndDrainAsync(awb, ShipmentStatus.RtoDelivered);

            var row = await CodRowAsync(placed.SubOrderId, "Waived");

            Assert.Equal("Waived", row["status"]);
            Assert.Null(row["collected_amount"]);
        }
    }

    /// <summary>
    /// A courier remittance file matched by air waybill apportions a short total across the parcels
    /// it covers and leaves an already-remitted one untouched.
    /// </summary>
    [Fact]
    public async Task A_courier_remittance_apportions_a_short_total_and_leaves_a_remitted_one_untouched()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var vendors = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var orders = new ShippingOrderScenario(admin, Cancellation);
        var stateId = await vendors.StateIdAsync();

        var seller = await vendors.ActiveAsync();
        var taxonomy = await catalogue.TaxonomyAsync();

        var (awbA, subOrderA) = await DeliveredCodParcelAsync(admin, vendors, catalogue, orders, seller.Id, taxonomy, stateId, 999m);
        var (awbB, subOrderB) = await DeliveredCodParcelAsync(admin, vendors, catalogue, orders, seller.Id, taxonomy, stateId, 499m);

        var owedA = Convert.ToDecimal((await CodRowAsync(subOrderA, "Collected"))["collected_amount"]);
        var owedB = Convert.ToDecimal((await CodRowAsync(subOrderB, "Collected"))["collected_amount"]);

        var expectedTotal = owedA + owedB;
        var shortTotal = Math.Round(expectedTotal * 0.9m, 2);

        var remitted = await ReadAsync(await admin.PostAsJsonAsync(
            "/api/v1/admin/shipping/cod-remittances",
            new { awbs = new[] { awbA, awbB }, reference = $"UTR-{Guid.NewGuid():N}"[..20], amount = shortTotal, remittedAt = (DateTimeOffset?)null },
            Cancellation));

        Assert.Equal(2, remitted.GetInt32());

        var afterFirst = await Database.RowsAsync(
            "SELECT sub_order_id, status, remitted_amount FROM payments.cod_collections "
            + "WHERE sub_order_id = ANY($1)",
            Cancellation,
            new[] { subOrderA, subOrderB });

        foreach (var row in afterFirst)
        {
            Assert.Equal("Remitted", row["status"]);
            Assert.NotNull(row["remitted_amount"]);
        }

        // Both apportioned shares sum to the short total the courier actually sent, not to what was
        // owed — a courier that deducts its fee leaves every record short by its proportional share.
        var remittedTotal = afterFirst.Sum(row => Convert.ToDecimal(row["remitted_amount"]));
        Assert.True(Math.Abs(remittedTotal - shortTotal) < 0.05m, $"Expected close to {shortTotal}, got {remittedTotal}.");

        var remittedAAmount = Convert.ToDecimal(
            afterFirst.First(row => (Guid)row["sub_order_id"]! == subOrderA)["remitted_amount"]);

        // A second remittance file naming the same air waybills must not touch a record already
        // remitted — the record's own status excludes it from the next apportionment.
        var secondPass = await ReadAsync(await admin.PostAsJsonAsync(
            "/api/v1/admin/shipping/cod-remittances",
            new { awbs = new[] { awbA, awbB }, reference = $"UTR-{Guid.NewGuid():N}"[..20], amount = (decimal?)null, remittedAt = (DateTimeOffset?)null },
            Cancellation));

        Assert.Equal(0, secondPass.GetInt32());

        var unchanged = Convert.ToDecimal((await Database.RowsAsync(
                "SELECT remitted_amount FROM payments.cod_collections WHERE sub_order_id = $1",
                Cancellation, subOrderA)).Single()["remitted_amount"]);

        Assert.Equal(remittedAAmount, unchanged);
    }

    /// <summary>
    /// Reads a cash record, waiting briefly for the status a scan just triggered.
    /// </summary>
    /// <remarks>
    /// <c>ShipmentWorkflow</c> commits the parcel's own move on the Shipping context and calls
    /// <c>ICodCollections</c> on a separate Payments one — two module schemas, two commits, no
    /// shared transaction, by the same design <c>IOrderPaymentSync</c> uses. A read immediately
    /// after the webhook drain can occasionally observe the first commit and not yet the second;
    /// polling is the same tolerance <see cref="OutboxDrain"/> already applies to a cross-module
    /// effect for exactly that reason.
    /// </remarks>
    /// <param name="subOrderId">The seller's part the cash is owed against.</param>
    /// <param name="expectedStatus">The status this call is waiting to observe.</param>
    private async Task<IReadOnlyDictionary<string, object?>> CodRowAsync(Guid subOrderId, string expectedStatus)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);

        while (true)
        {
            var row = (await Database.RowsAsync(
                    "SELECT status, collected_amount FROM payments.cod_collections WHERE sub_order_id = $1",
                    Cancellation,
                    subOrderId))
                .Single();

            if (Equals(row["status"], expectedStatus) || DateTimeOffset.UtcNow >= deadline)
            {
                return row;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), Cancellation);
        }
    }

    /// <summary>Builds and delivers one cash-on-delivery parcel, and answers its air waybill and sub-order.</summary>
    private async Task<(string Awb, Guid SubOrderId)> DeliveredCodParcelAsync(
        HttpClient admin,
        VendorScenario vendors,
        CatalogScenario catalogue,
        ShippingOrderScenario orders,
        Guid vendorId,
        CatalogTaxonomy taxonomy,
        Guid stateId,
        decimal price)
    {
        var product = await catalogue.DraftAsync(taxonomy, vendorId);
        await catalogue.ActivateVariantAsync(product.VariantId);
        await catalogue.PublishAsync(product.Id);
        var listingId = await catalogue.OfferAsync(vendorId, product.VariantId, sellingPrice: price);
        await catalogue.StockAsync(listingId, 100, vendorId);

        var (shopper, _) = await SignedInShopperAsync();
        var placed = await orders.ConfirmedSubOrderAsync(shopper, stateId, vendorId, listingId, factory: Factory, database: Database);

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

        var awb = booked.GetProperty("awb").GetString()!;

        await SendAndDrainAsync(awb, ShipmentStatus.Delivered);

        return (awb, placed.SubOrderId);
    }

    /// <summary>Posts a signed scan as a webhook and drains it through the real processor.</summary>
    private async Task SendAndDrainAsync(string awb, ShipmentStatus status)
    {
        var scan = Factory.Courier.Scan(awb, status);
        var body = Factory.Courier.WebhookBody(awb, scan);
        var signature = FakeShippingProvider.Sign(body);

        var response = await PostRawAsync(
            CreateClient(),
            "/api/v1/webhooks/shipping/shiprocket",
            body,
            ("X-Api-Key", signature));

        response.EnsureSuccessStatusCode();

        using var scope = Factory.Services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<CourierEventProcessor>();

        var stored = await context.CourierEvents
            .IgnoreQueryFilters()
            .FirstAsync(entry => entry.ProviderEventId == scan.ProviderEventId, Cancellation);

        await processor.ProcessAsync(stored, Cancellation);

        if (stored.Status == CourierEventStatus.Pending)
        {
            stored.MarkIgnored(DateTimeOffset.UtcNow);
        }

        await context.SaveChangesAsync(Cancellation);
    }
}
