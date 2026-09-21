using System.Net;
using System.Net.Http.Json;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Shipping.Infrastructure.Fulfilment;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Operations cancelling an order after the courier has collected its parcel: the parcel is brought
/// back, and the shopper's refund waits for it.
/// </summary>
/// <remarks>
/// No courier API recalls a parcel in transit; the only instruction one accepts is "return to
/// origin", and only at a failed delivery attempt. So these walk the real sequence — dispatched,
/// cancelled, a failed attempt, the return, the parcel back with the seller — and check what each
/// step does to the parcel and to the money.
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class ShippingReturnAfterCancellationTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// A paid order cancelled after pickup: the parcel is marked to come back, the courier is told at
    /// the next failed attempt, and the refund is sent only once the parcel is back with the seller.
    /// </summary>
    [Fact]
    public async Task A_parcel_cancelled_after_pickup_comes_back_and_only_then_is_refunded()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new ReturnsScenario(Factory, admin, Database, Cancellation);
        var catalogue = await scenario.CatalogueAsync();
        var paid = await scenario.PaidOrderAsync(catalogue);

        var (shipmentId, awb) = await DispatchAsync(admin, paid.SubOrderId);
        Assert.Equal("Shipped", await SubOrderStatusAsync(admin, paid));

        await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/sub-orders/{paid.SubOrderId}/cancel",
            new { reason = "The customer phoned to cancel.", lines = (object?)null },
            Cancellation));

        await scenario.DrainOutboxAsync();

        // The order is off; the parcel is still with the courier, marked to come back, and on the
        // operations queue. Nothing has been asked of the courier yet — there is nothing it would take.
        Assert.Equal("Cancelled", await SubOrderStatusAsync(admin, paid));

        var parcel = await ShipmentAsync(admin, shipmentId);
        Assert.Equal("PickedUp", parcel.GetProperty("status").GetString());
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, parcel.GetProperty("returnRequestedAt").ValueKind);
        Assert.DoesNotContain(awb, Factory.Courier.Returns);
        Assert.Contains(shipmentId, await ReturnQueueAsync(admin));

        // The refund is raised but held: the shopper could otherwise end up with the money and the goods.
        Assert.True(await RefundHeldAsync(paid.SubOrderId));
        Assert.Equal("Requested", await RefundStatusAsync(paid.SubOrderId));
        Assert.False(RefundSent(paid.SubOrderId));

        // The shopper refuses it at the door. That is the one moment the courier takes a return.
        await ScanAsync(admin, shipmentId, "Exception", "Customer refused the delivery.");

        var waiting = await InScopeAsync<CourierReturns, IReadOnlyList<Guid>>(
            (returns, ct) => returns.PendingAsync(500, ct));
        Assert.Contains(shipmentId, waiting);

        var sent = await InScopeAsync<CourierReturns, bool>((returns, ct) => returns.RetryAsync(shipmentId, ct));

        Assert.True(sent);
        Assert.Contains(awb, Factory.Courier.Returns);
        Assert.Equal("RtoInitiated", (await ShipmentAsync(admin, shipmentId)).GetProperty("status").GetString());
        Assert.Equal(
            "ReturnToOrigin",
            await Database.ScalarAsync<string>(
                "SELECT action FROM shipping.ndr_records WHERE shipment_id = $1",
                Cancellation,
                shipmentId));

        // Still no refund while it is on its way back.
        await scenario.DrainOutboxAsync();
        Assert.False(RefundSent(paid.SubOrderId));

        // Back with the seller: now the refund goes.
        await ScanAsync(admin, shipmentId, "RtoDelivered", "Delivered back to the seller.");
        await scenario.DrainOutboxAsync();

        Assert.False(await RefundHeldAsync(paid.SubOrderId));
        Assert.NotEqual("Requested", await RefundStatusAsync(paid.SubOrderId));
        Assert.True(RefundSent(paid.SubOrderId));
        Assert.DoesNotContain(shipmentId, await ReturnQueueAsync(admin));
    }

    /// <summary>A parcel on a van comes back whole, so part of a dispatched order cannot be cancelled.</summary>
    [Fact]
    public async Task Part_of_a_dispatched_order_cannot_be_cancelled()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new ReturnsScenario(Factory, admin, Database, Cancellation);
        var catalogue = await scenario.CatalogueAsync();
        var paid = await scenario.PaidOrderAsync(catalogue, quantity: 2);

        await DispatchAsync(admin, paid.SubOrderId);

        await RefusedAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/sub-orders/{paid.SubOrderId}/cancel",
                new
                {
                    reason = "Only one wanted.",
                    lines = new[] { new { orderLineId = paid.OrderLineId, quantity = 1 } },
                },
                Cancellation),
            HttpStatusCode.UnprocessableEntity,
            "ORDER_PARTIAL_CANCELLATION_AFTER_DISPATCH");

        Assert.Equal("Shipped", await SubOrderStatusAsync(admin, paid));
    }

    /// <summary>Packs, books and hands a paid order to the courier.</summary>
    private static async Task<(Guid ShipmentId, string Awb)> DispatchAsync(HttpClient admin, Guid subOrderId)
    {
        foreach (var status in new[] { "Processing", "Packed" })
        {
            await ReadAsync(await admin.PostAsJsonAsync(
                $"/api/v1/admin/sub-orders/{subOrderId}/transition",
                new { status, reason = (string?)null },
                Cancellation));
        }

        var booked = await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/sub-orders/{subOrderId}/shipments",
            new
            {
                lines = Array.Empty<object>(),
                weight = 500,
                dimensions = (object?)null,
                courier = (string?)null,
                pickupLocationId = (Guid?)null,
                manualAwb = (string?)null,
                manualCourier = (string?)null,
            },
            Cancellation));

        var shipmentId = booked.GetProperty("id").GetGuid();

        await ReadAsync(await admin.PostAsync(
            new Uri($"/api/v1/admin/shipments/{shipmentId}/dispatch", UriKind.Relative),
            content: null,
            Cancellation));

        return (shipmentId, booked.GetProperty("awb").GetString()!);
    }

    private static async Task ScanAsync(HttpClient admin, Guid shipmentId, string status, string remark)
        => await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/shipments/{shipmentId}/tracking",
            new { status, remark, occurredAt = (DateTimeOffset?)null },
            Cancellation));

    private static async Task<System.Text.Json.JsonElement> ShipmentAsync(HttpClient admin, Guid shipmentId)
        => await ReadAsync(
            await admin.GetAsync(new Uri($"/api/v1/admin/shipments/{shipmentId}", UriKind.Relative), Cancellation));

    private static async Task<IReadOnlyList<Guid>> ReturnQueueAsync(HttpClient admin)
    {
        var page = await ReadAsync(await admin.GetAsync(
            new Uri("/api/v1/admin/shipments?returnRequested=true&size=50", UriKind.Relative),
            Cancellation));

        return [.. page.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid())];
    }

    private static async Task<string?> SubOrderStatusAsync(HttpClient admin, PaidOrder paid)
    {
        var order = await ReadAsync(
            await admin.GetAsync(new Uri($"/api/v1/admin/orders/{paid.OrderId}", UriKind.Relative), Cancellation));

        return order.GetProperty("subOrders").EnumerateArray()
            .First(entry => entry.GetProperty("id").GetGuid() == paid.SubOrderId)
            .GetProperty("status").GetString();
    }

    private Task<bool> RefundHeldAsync(Guid subOrderId)
        => Database.ScalarAsync<bool>(
            "SELECT is_held_for_return FROM payments.refunds WHERE sub_order_id = $1",
            Cancellation,
            subOrderId);

    private Task<string?> RefundStatusAsync(Guid subOrderId)
        => Database.ScalarAsync<string>(
            "SELECT status FROM payments.refunds WHERE sub_order_id = $1",
            Cancellation,
            subOrderId);

    /// <summary>Whether the gateway was ever asked for this sub-order's cancellation refund.</summary>
    private bool RefundSent(Guid subOrderId)
        => Factory.Gateway.RefundAttempts.Keys.Any(
            key => key.StartsWith($"cancel:{subOrderId}", StringComparison.Ordinal));
}
