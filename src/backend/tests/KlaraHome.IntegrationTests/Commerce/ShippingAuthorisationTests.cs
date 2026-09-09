using KlaraHome.IntegrationTests.Database;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The authorisation matrix: a vendor caller cannot read, pack, book, label or cancel another
/// seller's parcel, cannot work another seller's failed deliveries, and cannot write a platform-wide
/// rate rule — every route answers 404, not 403.
/// </summary>
/// <param name="fixture">The migrated database.</param>
public sealed class ShippingAuthorisationTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    [Fact]
    public async Task A_vendor_cannot_read_pack_book_label_or_cancel_another_sellers_parcel()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var vendors = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var orders = new ShippingOrderScenario(admin, Cancellation);
        var stateId = await vendors.StateIdAsync();

        var sellerA = await vendors.ActiveAsync();
        var sellerB = await vendors.ActiveAsync();

        var taxonomy = await catalogue.TaxonomyAsync();
        var product = await catalogue.DraftAsync(taxonomy, sellerA.Id);
        await catalogue.ActivateVariantAsync(product.VariantId);
        await catalogue.PublishAsync(product.Id);
        var listingId = await catalogue.OfferAsync(sellerA.Id, product.VariantId, sellingPrice: 999m);
        await catalogue.StockAsync(listingId, 100, sellerA.Id);

        var (shopper, _) = await SignedInShopperAsync();
        var placed = await orders.ConfirmedSubOrderAsync(
            shopper, stateId, sellerA.Id, listingId, factory: Factory, database: Database);

        var (ownerA, _) = await SignedInVendorOwnerAsync(admin, sellerA.Id);
        var (ownerB, _) = await SignedInVendorOwnerAsync(admin, sellerB.Id);

        var shipment = await ReadAsync(await ownerA.PostAsJsonAsync(
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

        var shipmentId = shipment.GetProperty("id").GetGuid();

        // A read.
        await RefusedAsync(
            await ownerB.GetAsync(new Uri($"/api/v1/admin/shipments/{shipmentId}", UriKind.Relative), Cancellation),
            HttpStatusCode.NotFound);

        // A pack.
        await RefusedAsync(
            await ownerB.PutAsJsonAsync(
                $"/api/v1/admin/shipments/{shipmentId}/contents",
                new { lines = Array.Empty<object>() },
                Cancellation),
            HttpStatusCode.NotFound);

        // A book (this parcel is already booked, but the scope check answers first).
        await RefusedAsync(
            await ownerB.PostAsJsonAsync(
                $"/api/v1/admin/shipments/{shipmentId}/book",
                new { courier = (string?)null, pickupLocationId = (Guid?)null, manualAwb = (string?)null, manualCourier = (string?)null },
                Cancellation),
            HttpStatusCode.NotFound);

        // A label.
        await RefusedAsync(
            await ownerB.GetAsync(new Uri($"/api/v1/admin/shipments/{shipmentId}/label", UriKind.Relative), Cancellation),
            HttpStatusCode.NotFound);

        // A cancellation.
        await RefusedAsync(
            await ownerB.PostAsJsonAsync(
                $"/api/v1/admin/shipments/{shipmentId}/cancel",
                new { reason = "not mine" },
                Cancellation),
            HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_vendor_cannot_work_another_sellers_failed_delivery()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var vendors = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var orders = new ShippingOrderScenario(admin, Cancellation);
        var stateId = await vendors.StateIdAsync();

        var sellerA = await vendors.ActiveAsync();
        var sellerB = await vendors.ActiveAsync();

        var taxonomy = await catalogue.TaxonomyAsync();
        var product = await catalogue.DraftAsync(taxonomy, sellerA.Id);
        await catalogue.ActivateVariantAsync(product.VariantId);
        await catalogue.PublishAsync(product.Id);
        var listingId = await catalogue.OfferAsync(sellerA.Id, product.VariantId, sellingPrice: 999m);
        await catalogue.StockAsync(listingId, 100, sellerA.Id);

        var (shopper, _) = await SignedInShopperAsync();
        var placed = await orders.ConfirmedSubOrderAsync(
            shopper, stateId, sellerA.Id, listingId, factory: Factory, database: Database);

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

        var scan = Factory.Courier.Scan(
            awb,
            Modules.Shipping.Domain.ShipmentStatus.Exception,
            Modules.Shipping.Domain.NdrReasonCode.CustomerUnavailable);

        var body = Factory.Courier.WebhookBody(awb, scan);
        var signature = FakeShippingProvider.Sign(body);

        (await PostRawAsync(
            CreateClient(),
            "/api/v1/webhooks/shipping/shiprocket",
            body,
            ("X-Api-Key", signature))).EnsureSuccessStatusCode();

        // Drained by hand, exactly as the worker would.
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<Modules.Shipping.Infrastructure.Persistence.ShippingDbContext>();
            var processor = scope.ServiceProvider
                .GetRequiredService<Modules.Shipping.Infrastructure.Processing.CourierEventProcessor>();

            var stored = await context.CourierEvents
                .IgnoreQueryFilters()
                .FirstAsync(entry => entry.ProviderEventId == scan.ProviderEventId, Cancellation);

            await processor.ProcessAsync(stored, Cancellation);
            await context.SaveChangesAsync(Cancellation);
        }

        var ndrId = await Database.ScalarAsync<Guid>(
            "SELECT id FROM shipping.ndr_records WHERE shipment_id = $1",
            Cancellation,
            booked.GetProperty("id").GetGuid());

        var (ownerB, _) = await SignedInVendorOwnerAsync(admin, sellerB.Id);

        await RefusedAsync(
            await ownerB.PostAsJsonAsync(
                $"/api/v1/admin/ndr/{ndrId}/action",
                new { action = "Reattempt", remark = "not mine", rescheduledFor = (DateTimeOffset?)null },
                Cancellation),
            HttpStatusCode.NotFound);

        // The seller's own queue shows nothing of it either.
        var ownList = await ReadAsync(await ownerB.GetAsync(new Uri("/api/v1/admin/ndr", UriKind.Relative), Cancellation));

        Assert.DoesNotContain(
            ownList.GetProperty("items").EnumerateArray(),
            row => row.GetProperty("id").GetGuid() == ndrId);
    }

    /// <summary>
    /// A seller writing a rate rule can never land it on the platform's own card: the caller's own
    /// id wins over whatever the body names, whatever the body names.
    /// </summary>
    [Fact]
    public async Task A_vendor_cannot_write_a_platform_wide_rate_rule()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var vendors = Sellers(admin);

        var seller = await vendors.ActiveAsync();
        var (owner, _) = await SignedInVendorOwnerAsync(admin, seller.Id);

        var zones = await ReadAsync(
            await admin.GetAsync(new Uri("/api/v1/admin/shipping/zones", UriKind.Relative), Cancellation));

        var zoneId = zones.EnumerateArray().First().GetProperty("id").GetGuid();

        var created = await ReadAsync(await owner.PostAsJsonAsync(
            "/api/v1/admin/shipping/rates",
            new
            {
                zoneId,
                method = "Standard",
                vendorId = (Guid?)null, // The body asks for the platform's own card.
                terms = new
                {
                    minWeightGrams = 0,
                    maxWeightGrams = 1000,
                    minOrderValue = 0m,
                    maxOrderValue = (decimal?)null,
                    baseRate = 10m,
                    perKgRate = 5m,
                    freeAbove = (decimal?)null,
                    codFee = 5m,
                    isCodAllowed = true,
                    etaMinDays = 1,
                    etaMaxDays = 3,
                },
            },
            Cancellation));

        // Never null: the handler substitutes the caller's own seller id for whatever the body named.
        Assert.Equal(seller.Id, created.GetProperty("vendorId").GetGuid());
    }
}
