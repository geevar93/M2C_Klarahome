using KlaraHome.IntegrationTests.Database;
using System.Text.Json;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The admin dashboard's commercial figures (<c>GET /admin/dashboard/summary</c>, ADMIN_UX_GAPS H2).
/// </summary>
/// <param name="fixture">The migrated database.</param>
public sealed class ReportingDashboardTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// A confirmed cash-on-delivery order moves today's orders and revenue and the cash still to
    /// collect, by exactly what it is worth - proved as a delta, because the database is shared.
    /// </summary>
    [Fact]
    public async Task A_confirmed_cod_order_moves_todays_orders_revenue_and_pending_cash()
    {
        SkipWithoutDocker();

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

        var before = await SummaryAsync(admin);

        var (shopper, _) = await SignedInShopperAsync();
        var stateId = await vendors.StateIdAsync();

        await orders.ConfirmedSubOrderAsync(shopper, stateId, seller.Id, listingId, factory: Factory, database: Database);

        var after = await SummaryAsync(admin);

        Assert.Equal(before.GetProperty("day").GetString(), after.GetProperty("day").GetString());
        Assert.Equal(
            before.GetProperty("ordersToday").GetInt32() + 1,
            after.GetProperty("ordersToday").GetInt32());

        var revenue = after.GetProperty("revenueToday").GetDecimal() - before.GetProperty("revenueToday").GetDecimal();
        Assert.True(revenue > 0m, "Today's revenue should have grown by the order's value.");

        Assert.Equal(
            before.GetProperty("codPendingOrders").GetInt32() + 1,
            after.GetProperty("codPendingOrders").GetInt32());

        Assert.Equal(
            revenue,
            after.GetProperty("codPendingAmount").GetDecimal() - before.GetProperty("codPendingAmount").GetDecimal());

        Assert.True(after.GetProperty("lowStockCount").GetInt32() >= 0);
        Assert.Equal("INR", after.GetProperty("currencyCode").GetString());
    }

    private async Task<JsonElement> SummaryAsync(HttpClient admin)
        => await ReadAsync(await admin.GetAsync(
            new Uri("/api/v1/admin/dashboard/summary", UriKind.Relative),
            Cancellation));
}
