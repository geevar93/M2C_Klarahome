using System.Net.Http.Json;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Shipping.Infrastructure.Fulfilment;
using KlaraHome.Modules.Shipping.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// A shopper cancelling an order whose parcel is already booked with the courier: the courier is
/// told, and a courier that cannot be reached is retried rather than forgotten.
/// </summary>
/// <remarks>
/// The window these cover is real and routine. An order only ships when the courier collects it, so
/// a parcel sits labelled with a pickup scheduled while its order is still <c>Packed</c> — and the
/// shopper may cancel up to and including <c>Packed</c>. Before this, the cancellation changed only
/// our own record and the courier still sent a driver.
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class ShippingCancellationTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// Cancelling a packed order whose parcel is booked cancels the waybill with the courier, then
    /// withdraws the parcel.
    /// </summary>
    [Fact]
    public async Task Cancelling_a_packed_order_cancels_its_booked_parcel_with_the_courier()
    {
        SkipWithoutDocker();

        var (admin, shopper, placed) = await PackedOrderAsync();
        var (shipmentId, awb) = await BookAsync(admin, placed.SubOrderId);

        await CancelAsync(shopper, placed.OrderNumber);
        await OutboxDrain.RunAsync(Factory, Database, Cancellation);

        Assert.Contains(awb, Factory.Courier.Cancellations);
        Assert.Equal("Cancelled", await ShipmentStatusAsync(admin, shipmentId));
        Assert.False(await AwaitsCourierAsync(shipmentId));
    }

    /// <summary>
    /// When the courier cannot be reached the order is still cancelled, the parcel stays booked and
    /// marked, and the retry withdraws it once the courier answers.
    /// </summary>
    [Fact]
    public async Task A_courier_that_cannot_be_reached_is_retried_until_it_cancels_the_parcel()
    {
        SkipWithoutDocker();

        var (admin, shopper, placed) = await PackedOrderAsync();
        var (shipmentId, awb) = await BookAsync(admin, placed.SubOrderId);

        Factory.Courier.FailCancellations = true;

        await CancelAsync(shopper, placed.OrderNumber);
        await OutboxDrain.RunAsync(Factory, Database, Cancellation);

        // The order is cancelled regardless: a courier outage must not hold up the cancellation, or
        // the refund that rides the same event.
        Assert.Equal("Cancelled", await SubOrderStatusAsync(admin, placed));

        // The parcel is not: marking it cancelled while the courier still expects it is the bug.
        Assert.DoesNotContain(awb, Factory.Courier.Cancellations);
        Assert.Equal("LabelGenerated", await ShipmentStatusAsync(admin, shipmentId));
        Assert.True(await AwaitsCourierAsync(shipmentId));

        var pending = await InScopeAsync<CourierCancellation, IReadOnlyList<Guid>>(
            (couriers, ct) => couriers.PendingAsync(500, ct));

        Assert.Contains(shipmentId, pending);

        // Still down: the retry changes nothing and keeps the parcel waiting.
        var stillDown = await InScopeAsync<CourierCancellation, CourierWithdrawal>(
            (couriers, ct) => couriers.RetryAsync(shipmentId, ct));

        Assert.Equal(CourierWithdrawal.Pending, stillDown);
        Assert.True(await AwaitsCourierAsync(shipmentId));

        // Back up: the next pass withdraws it.
        Factory.Courier.FailCancellations = false;

        var recovered = await InScopeAsync<CourierCancellation, CourierWithdrawal>(
            (couriers, ct) => couriers.RetryAsync(shipmentId, ct));

        Assert.Equal(CourierWithdrawal.Withdrawn, recovered);
        Assert.Contains(awb, Factory.Courier.Cancellations);
        Assert.Equal("Cancelled", await ShipmentStatusAsync(admin, shipmentId));
        Assert.False(await AwaitsCourierAsync(shipmentId));
    }

    /// <summary>
    /// Cancelling part of a packed cash order cancels its booked parcel — whose waybill carries the
    /// old contents and the old amount to collect — and reopens a draft for what is still owed.
    /// </summary>
    [Fact]
    public async Task A_partial_cancellation_rebooks_the_rest_instead_of_sending_the_old_parcel()
    {
        SkipWithoutDocker();

        var (admin, shopper, placed) = await PackedOrderAsync(quantity: 3);
        var (shipmentId, awb) = await BookAsync(admin, placed.SubOrderId);

        var booked = await ParcelAsync(shipmentId);
        Assert.Equal(3, Assert.Single(booked.Lines).Quantity);

        var order = await ReadAsync(await shopper.GetAsync(
            new Uri($"/api/v1/store/orders/{placed.OrderId}", UriKind.Relative),
            Cancellation));

        var lineId = order.GetProperty("subOrders").EnumerateArray().Single()
            .GetProperty("lines").EnumerateArray().Single()
            .GetProperty("id").GetGuid();

        await ReadAsync(await shopper.PostAsJsonAsync(
            $"/api/v1/store/sub-orders/{placed.SubOrderId}/cancel",
            new { reason = "Only need two.", lines = new[] { new { orderLineId = lineId, quantity = 1 } } },
            Cancellation));

        await OutboxDrain.RunAsync(Factory, Database, Cancellation);

        // The sub-order goes on — two units are still owed — but the parcel that was booked for three
        // is off, with the courier and here.
        Assert.Equal("Packed", await SubOrderStatusAsync(admin, placed));
        Assert.Contains(awb, Factory.Courier.Cancellations);
        Assert.Equal("Cancelled", await ShipmentStatusAsync(admin, shipmentId));

        var reopened = await InScopeAsync<ShippingDbContext, Modules.Shipping.Domain.Shipment>(
            (context, ct) => context.Shipments
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(shipment => shipment.Lines)
                .SingleAsync(
                    shipment => shipment.SubOrderId == placed.SubOrderId
                                && shipment.Status == Modules.Shipping.Domain.ShipmentStatus.Draft,
                    ct));

        Assert.Equal(2, Assert.Single(reopened.Lines).Quantity);
        Assert.False(reopened.IsBooked);

        // The courier will be told to collect what the order now owes, not what it owed at booking.
        Assert.NotNull(reopened.CodAmount);
        Assert.True(
            reopened.CodAmount < booked.CodAmount,
            $"Expected less than {booked.CodAmount} to collect after a unit was cancelled, got {reopened.CodAmount}.");
    }

    /// <summary>A confirmed cash-on-delivery order, moved by staff to <c>Packed</c>.</summary>
    private async Task<(HttpClient Admin, HttpClient Shopper, ShippingPlacedOrder Placed)> PackedOrderAsync(
        int quantity = 1)
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
        // Low enough that three units stay under any cash-on-delivery ceiling an earlier test left set.
        var listingId = await catalogue.OfferAsync(seller.Id, product.VariantId, sellingPrice: 499m);
        await catalogue.StockAsync(listingId, 100, seller.Id);

        var (shopper, _) = await SignedInShopperAsync();
        var stateId = await vendors.StateIdAsync();

        // Drained, so the confirmation's own handlers — the parcel's draft among them — have run.
        var placed = await orders.ConfirmedSubOrderAsync(
            shopper, stateId, seller.Id, listingId, quantity, factory: Factory, database: Database);

        foreach (var status in new[] { "Processing", "Packed" })
        {
            await ReadAsync(await admin.PostAsJsonAsync(
                $"/api/v1/admin/sub-orders/{placed.SubOrderId}/transition",
                new { status, reason = (string?)null },
                Cancellation));
        }

        return (admin, shopper, placed);
    }

    /// <summary>Books the parcel with the courier, which labels it but leaves the order packed.</summary>
    private static async Task<(Guid ShipmentId, string Awb)> BookAsync(HttpClient admin, Guid subOrderId)
    {
        var booked = await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/sub-orders/{subOrderId}/shipments",
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

        Assert.Equal("LabelGenerated", booked.GetProperty("status").GetString());

        return (booked.GetProperty("id").GetGuid(), booked.GetProperty("awb").GetString()!);
    }

    private static async Task CancelAsync(HttpClient shopper, string orderNumber)
        => await ReadAsync(await shopper.PostAsJsonAsync(
            $"/api/v1/store/orders/{orderNumber}/cancel",
            new { reason = "Ordered by mistake.", lines = (object?)null },
            Cancellation));

    private static async Task<string?> ShipmentStatusAsync(HttpClient admin, Guid shipmentId)
    {
        var shipment = await ReadAsync(
            await admin.GetAsync(new Uri($"/api/v1/admin/shipments/{shipmentId}", UriKind.Relative), Cancellation));

        return shipment.GetProperty("status").GetString();
    }

    private static async Task<string?> SubOrderStatusAsync(HttpClient admin, ShippingPlacedOrder placed)
    {
        var order = await ReadAsync(
            await admin.GetAsync(new Uri($"/api/v1/admin/orders/{placed.OrderId}", UriKind.Relative), Cancellation));

        return order.GetProperty("subOrders").EnumerateArray()
            .First(entry => entry.GetProperty("id").GetGuid() == placed.SubOrderId)
            .GetProperty("status").GetString();
    }

    /// <summary>The parcel as stored, lines included.</summary>
    private Task<Modules.Shipping.Domain.Shipment> ParcelAsync(Guid shipmentId)
        => InScopeAsync<ShippingDbContext, Modules.Shipping.Domain.Shipment>((context, ct) => context.Shipments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(shipment => shipment.Lines)
            .SingleAsync(shipment => shipment.Id == shipmentId, ct));

    /// <summary>Whether the parcel is marked as waiting on its courier, read from the row itself.</summary>
    private Task<bool> AwaitsCourierAsync(Guid shipmentId)
        => InScopeAsync<ShippingDbContext, bool>(async (context, ct) =>
        {
            var shipment = await context.Shipments
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == shipmentId, ct);

            return shipment.AwaitsCourierCancellation;
        });
}
