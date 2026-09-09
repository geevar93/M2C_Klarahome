using KlaraHome.IntegrationTests.Database;
using System.Net;
using System.Net.Http.Json;
using KlaraHome.Contracts.Orders;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The four TEST_DEBT rows about turning a confirmed order into a booked, moving parcel: the step's
/// own full acceptance criterion, the seam Shipping reaches ordering through, the rule that keeps a
/// partial shipment honest, and the aggregator adapter's fallback to a hand-typed waybill.
/// </summary>
/// <param name="fixture">The migrated database.</param>
public sealed class ShippingBookingTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// A confirmed order produces a shipment with an air waybill in the provider sandbox, end to end
    /// through <c>POST /admin/sub-orders/{id}/shipments</c>.
    /// </summary>
    /// <remarks>
    /// "Sandbox" here is <see cref="FakeShippingProvider"/>: the step card's own Known Gaps section
    /// records that no aggregator credentials exist, so this is the honest substitute — the same one
    /// the module itself was built and unit-tested against. It exercises the real
    /// <c>ShipmentBooker</c>, the real rate card and the real <c>IOrderFulfilment</c> seam; only the
    /// network call to a courier is faked.
    /// </remarks>
    [Fact]
    public async Task A_confirmed_order_produces_a_shipment_with_an_awb_in_the_provider_sandbox()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var vendors = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var orders = new OrderScenario(admin, Cancellation);

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

        var response = await ReadAsync(await admin.PostAsJsonAsync(
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

        Assert.Equal("LabelGenerated", response.GetProperty("status").GetString());
        Assert.StartsWith("AWB", response.GetProperty("awb").GetString(), StringComparison.Ordinal);
        Assert.Equal("shiprocket", response.GetProperty("provider").GetString());
        Assert.NotEmpty(response.GetProperty("lines").EnumerateArray());

        // The order's own timeline learned it, through IOrderFulfilment.AdvanceAsync — the booking
        // does not dispatch, so the sub-order is still Confirmed, not yet Shipped.
        var order = await orders.LoadAsync(placed.OrderId);

        Assert.Contains(
            Factory.Courier.Bookings,
            request => request.ShipmentId == response.GetProperty("id").GetGuid());
    }

    /// <summary>
    /// <c>IOrderFulfilment.AdvanceAsync</c> moves a sub-order through the ordering state machine as
    /// <c>System</c>, writes its timeline and raises its events — and is refused for an edge the
    /// machine does not have.
    /// </summary>
    [Fact]
    public async Task AdvanceAsync_moves_the_machine_as_system_and_refuses_an_edge_it_does_not_have()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var vendors = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var orders = new OrderScenario(admin, Cancellation);

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

        // Confirmed -> Shipped has no edge in the ordering machine (docs/02-domain-model.md §5.1):
        // Processing and Packed sit between them. A courier claiming otherwise is a discrepancy.
        var refused = true;

        await RunOnceAsync<IOrderFulfilment>(async (fulfilment, ct) =>
        {
            var result = await fulfilment.AdvanceAsync(placed.SubOrderId, "Shipped", "Courier said so", ct);
            refused = result.IsFailure;
        });

        Assert.True(refused, "The machine has no Confirmed -> Shipped edge; AdvanceAsync should refuse it.");

        // The seller packs it by hand — Confirmed -> Processing -> Packed is Vendor's and
        // Platform's edge, not System's — which is what puts the machine where System's own edge,
        // Packed -> Shipped, actually lives.
        await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/sub-orders/{placed.SubOrderId}/transition",
            new { status = "Processing", reason = (string?)null },
            Cancellation));

        await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/sub-orders/{placed.SubOrderId}/transition",
            new { status = "Packed", reason = (string?)null },
            Cancellation));

        var moved = false;

        await RunOnceAsync<IOrderFulfilment>(async (fulfilment, ct) =>
        {
            var result = await fulfilment.AdvanceAsync(placed.SubOrderId, "Shipped", "Courier collected the parcel", ct);
            moved = result.IsSuccess;
        });

        Assert.True(moved, "The machine has a Packed -> Shipped edge open to System.");

        var reread = await ReadAsync(
            await admin.GetAsync(new Uri($"/api/v1/admin/orders/{placed.OrderId}", UriKind.Relative), Cancellation));

        var subOrder = reread.GetProperty("subOrders").EnumerateArray()
            .First(entry => entry.GetProperty("id").GetGuid() == placed.SubOrderId);

        Assert.Equal("Shipped", subOrder.GetProperty("status").GetString());

        Assert.Contains(
            reread.GetProperty("timeline").EnumerateArray(),
            entry => entry.GetProperty("message").GetString() == "Courier collected the parcel"
                     && entry.GetProperty("actorType").GetString() == "System");
    }

    /// <summary>
    /// Two partial shipments against one sub-order cannot between them pack more units than were
    /// ordered, and the second is refused with <c>SHIPMENT_TOO_MANY_UNITS</c>.
    /// </summary>
    [Fact]
    public async Task Two_partial_shipments_cannot_between_them_pack_more_units_than_were_ordered()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var vendors = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var orders = new OrderScenario(admin, Cancellation);

        var seller = await vendors.ActiveAsync();
        var taxonomy = await catalogue.TaxonomyAsync();
        var product = await catalogue.DraftAsync(taxonomy, seller.Id);
        await catalogue.ActivateVariantAsync(product.VariantId);
        await catalogue.PublishAsync(product.Id);
        var listingId = await catalogue.OfferAsync(seller.Id, product.VariantId, sellingPrice: 999m);
        await catalogue.StockAsync(listingId, 100, seller.Id);

        var (shopper, _) = await SignedInShopperAsync();
        var stateId = await vendors.StateIdAsync();

        // Two units ordered, so the second parcel below is the one that oversteps.
        var placed = await orders.ConfirmedSubOrderAsync(shopper, stateId, seller.Id, listingId, quantity: 2);

        var order = await ReadAsync(
            await admin.GetAsync(new Uri($"/api/v1/admin/orders/{placed.OrderId}", UriKind.Relative), Cancellation));

        var line = order.GetProperty("subOrders").EnumerateArray()
            .First(entry => entry.GetProperty("id").GetGuid() == placed.SubOrderId)
            .GetProperty("lines").EnumerateArray().First();

        var lineId = line.GetProperty("id").GetGuid();

        var first = await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/sub-orders/{placed.SubOrderId}/shipments",
            new
            {
                lines = new[] { new { orderLineId = lineId, quantity = 1 } },
                weight = 300,
                dimensions = (object?)null,
                courier = "standard",
                pickupLocationId = (Guid?)null,
                manualAwb = (string?)null,
                manualCourier = (string?)null,
            },
            Cancellation));

        Assert.Equal("LabelGenerated", first.GetProperty("status").GetString());

        // The second partial shipment asks for both remaining-and-then-some: only one unit is left,
        // and this asks for two.
        var refused = await admin.PostAsJsonAsync(
            $"/api/v1/admin/sub-orders/{placed.SubOrderId}/shipments",
            new
            {
                lines = new[] { new { orderLineId = lineId, quantity = 2 } },
                weight = 300,
                dimensions = (object?)null,
                courier = "standard",
                pickupLocationId = (Guid?)null,
                manualAwb = (string?)null,
                manualCourier = (string?)null,
            },
            Cancellation);

        await RefusedAsync(refused, HttpStatusCode.UnprocessableEntity, "SHIPMENT_TOO_MANY_UNITS");
    }

    /// <summary>
    /// A booking the aggregator refuses falls back to the manual, hand-typed-waybill adapter — the
    /// registry's answer to an unconfigured or unusable deployment, and the reason this module can
    /// dispatch on day one with no aggregator account (docs/08-integrations.md §2).
    /// </summary>
    [Fact]
    public async Task A_failed_aggregator_booking_leaves_the_parcel_bookable_by_the_manual_adapter()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var vendors = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var orders = new OrderScenario(admin, Cancellation);

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

        Factory.Courier.FailBookings = true;

        var failed = await admin.PostAsJsonAsync(
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
            Cancellation);

        // The aggregator refused; the parcel is saved as a packed draft rather than lost, per
        // ShipmentBooker's own contract.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);

        var drafts = await ReadAsync(await admin.GetAsync(
            new Uri($"/api/v1/admin/shipments/pick-list?vendorId={seller.Id}", UriKind.Relative),
            Cancellation));

        Assert.Contains(
            drafts.EnumerateArray(),
            row => row.GetProperty("subOrderNumber").GetString() == placed.SubOrderNumber);

        // A hand-typed waybill goes to the manual adapter whatever else is configured, so the packer
        // is never stuck because the aggregator is down.
        var shipmentId = (await ReadAsync(await admin.GetAsync(
                new Uri($"/api/v1/admin/shipments?subOrderId={placed.SubOrderId}", UriKind.Relative),
                Cancellation)))
            .GetProperty("items").EnumerateArray().First()
            .GetProperty("id").GetGuid();

        var booked = await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/shipments/{shipmentId}/book",
            new { courier = (string?)null, pickupLocationId = (Guid?)null, manualAwb = "MANUAL123456", manualCourier = "Local Courier" },
            Cancellation));

        Assert.Equal("LabelGenerated", booked.GetProperty("status").GetString());
        Assert.Equal("MANUAL123456", booked.GetProperty("awb").GetString());
        Assert.Equal("manual", booked.GetProperty("provider").GetString());
    }
}
