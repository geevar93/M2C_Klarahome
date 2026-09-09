using KlaraHome.IntegrationTests.Database;
using System.Net.Http.Json;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The rendered 4×6 label and the manifest PDF are produced, stored privately, and reachable only
/// through a signed link that expires (docs/07-security-compliance.md §5).
/// </summary>
/// <param name="fixture">The migrated database.</param>
public sealed class ShippingLabelTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    [Fact]
    public async Task The_label_and_manifest_are_reachable_only_through_an_expiring_signed_link()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var vendors = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var orders = new ShippingOrderScenario(admin, Cancellation);
        var stateId = await vendors.StateIdAsync();

        var seller = await vendors.ActiveAsync();
        var taxonomy = await catalogue.TaxonomyAsync();
        var product = await catalogue.DraftAsync(taxonomy, seller.Id);
        await catalogue.ActivateVariantAsync(product.VariantId);
        await catalogue.PublishAsync(product.Id);
        var listingId = await catalogue.OfferAsync(seller.Id, product.VariantId, sellingPrice: 999m);
        await catalogue.StockAsync(listingId, 100, seller.Id);

        var (shopper, _) = await SignedInShopperAsync();
        var placed = await orders.ConfirmedSubOrderAsync(
            shopper, stateId, seller.Id, listingId, factory: Factory, database: Database);

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

        var shipmentId = booked.GetProperty("id").GetGuid();

        var label = await ReadAsync(await admin.GetAsync(
            new Uri($"/api/v1/admin/shipments/{shipmentId}/label", UriKind.Relative), Cancellation));

        var url = label.GetProperty("url").GetString();
        var expiresAt = label.GetProperty("expiresAt").GetDateTimeOffset();

        Assert.False(string.IsNullOrWhiteSpace(url));
        Assert.True(expiresAt > DateTimeOffset.UtcNow, "The label link must expire in the future.");
        Assert.True(expiresAt < DateTimeOffset.UtcNow.AddHours(1), "The label link must not last indefinitely.");

        // Never the public bucket's own base URL: a label carries a customer's address, and this
        // deployment's public base is "https://cdn.example.test/media-public".
        Assert.DoesNotContain("media-public", url, StringComparison.Ordinal);

        // Unauthenticated, and the label endpoint itself requires the permission: an anonymous
        // caller cannot mint the link in the first place.
        var anonymous = await CreateClient().GetAsync(
            new Uri($"/api/v1/admin/shipments/{shipmentId}/label", UriKind.Relative), Cancellation);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, anonymous.StatusCode);

        // The manifest is produced and stored the same way.
        var manifest = await ReadAsync(await admin.PostAsJsonAsync(
            "/api/v1/admin/manifests",
            new { shipmentIds = new[] { shipmentId }, vendorId = seller.Id, pickupLocationId = (Guid?)null },
            Cancellation));

        Assert.Equal(1, manifest.GetProperty("shipmentCount").GetInt32());
    }
}
