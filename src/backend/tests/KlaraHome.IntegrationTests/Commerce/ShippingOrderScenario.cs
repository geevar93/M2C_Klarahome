using System.Net.Http.Json;
using System.Text.Json;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>A confirmed sub-order, and the ids a Step 16 test needs to dispatch it.</summary>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="SubOrderId">The seller's part — what a shipment is created against.</param>
/// <param name="SubOrderNumber">Its number.</param>
/// <param name="CustomerId">The shopper who placed it.</param>
internal sealed record ShippingPlacedOrder(
    Guid OrderId,
    string OrderNumber,
    Guid SubOrderId,
    string SubOrderNumber,
    Guid CustomerId);

/// <summary>
/// Carries a shopper from an empty basket to a confirmed sub-order, through the API a customer
/// actually uses.
/// </summary>
/// <remarks>
/// <para>
/// Shipping's own acceptance criteria all start from "a confirmed order" and Steps 11-15 own the
/// road that gets one there. This scenario drives that road exactly as a shopper would — add to
/// cart, open checkout, choose an address, choose a delivery service, choose how to pay, place —
/// rather than writing a <c>Confirmed</c> row directly, for the same reason <c>VendorScenario</c>
/// and <c>CatalogScenario</c> do not write rows either: a shipment built against an order the
/// product could never have produced would not be proving anything about Shipping.
/// </para>
/// <para>
/// Cash on delivery only, deliberately. A cash sub-order is confirmed the instant it is placed
/// (<c>OrderPlacementService.ConfirmForCashOnDelivery</c>) — there is no gateway to wait for — which
/// is what makes it the shortest honest path to the state every Step 16 test needs to start from.
/// </para>
/// </remarks>
/// <param name="admin">A client signed in as platform staff.</param>
/// <param name="cancellationToken">Cancellation token.</param>
internal sealed class ShippingOrderScenario(HttpClient admin, CancellationToken cancellationToken)
{
    /// <summary>Saves an address for a shopper and answers its id.</summary>
    /// <param name="shopper">A client signed in as the shopper.</param>
    /// <param name="stateId">The state or union territory.</param>
    /// <param name="pincode">Six-digit PIN code. Hyderabad by default, which is what the courier and
    /// the seller's pickup location both cover.</param>
    public async Task<Guid> AddressAsync(HttpClient shopper, Guid stateId, string pincode = "500034")
    {
        var created = await Rest.ReadAsync(
            await shopper.PostAsJsonAsync(
                "/api/v1/store/me/addresses",
                new
                {
                    label = "Home",
                    recipientName = "Test Shopper",
                    mobile = "9876543210",
                    line1 = "Flat 4B",
                    line2 = "Greenview Apartments",
                    landmark = (string?)null,
                    city = "Hyderabad",
                    stateId,
                    pincode,
                    gstin = (string?)null,
                    type = "Home",
                    isDefaultShipping = true,
                    isDefaultBilling = true,
                },
                cancellationToken),
            cancellationToken);

        return created.GetProperty("id").GetGuid();
    }

    /// <summary>Adds units of an offer to a shopper's basket.</summary>
    /// <param name="shopper">A client signed in as the shopper.</param>
    /// <param name="listingId">The offer.</param>
    /// <param name="quantity">How many units.</param>
    public Task<HttpResponseMessage> AddToCartAsync(HttpClient shopper, Guid listingId, int quantity = 1)
        => shopper.PostAsJsonAsync(
            "/api/v1/store/cart/items",
            new { listingId, quantity },
            cancellationToken);

    /// <summary>
    /// Carries a shopper's basket all the way to a placed, cash-on-delivery order.
    /// </summary>
    /// <param name="shopper">A client signed in as the shopper, with items already in their basket.</param>
    /// <param name="addressId">Their saved delivery address.</param>
    /// <param name="vendorId">The seller whose delivery service is being chosen.</param>
    /// <param name="method">The service code to choose: <c>standard</c> or <c>express</c>.</param>
    public async Task<(Guid OrderId, string OrderNumber)> PlaceCodOrderAsync(
        HttpClient shopper,
        Guid addressId,
        Guid vendorId,
        string method = "standard")
    {
        var session = await Rest.ReadAsync(
            await shopper.PostAsJsonAsync("/api/v1/store/checkout/", new { }, cancellationToken),
            cancellationToken);

        var sessionId = session.GetProperty("id").GetGuid();

        await Rest.ReadAsync(
            await shopper.PutAsJsonAsync(
                $"/api/v1/store/checkout/{sessionId}/address",
                new { shippingAddressId = addressId, billingAddressId = (Guid?)null, gstin = (string?)null },
                cancellationToken),
            cancellationToken);

        var options = await Rest.ReadAsync(
            await shopper.GetAsync(
                new Uri($"/api/v1/store/checkout/{sessionId}/shipping-options", UriKind.Relative),
                cancellationToken),
            cancellationToken);

        var forVendor = options.EnumerateArray()
            .First(entry => entry.GetProperty("vendorId").GetGuid() == vendorId);

        var chosenCode = forVendor.GetProperty("options").EnumerateArray()
            .Select(entry => entry.GetProperty("code").GetString())
            .FirstOrDefault(code => string.Equals(code, method, StringComparison.OrdinalIgnoreCase))
            ?? forVendor.GetProperty("options").EnumerateArray().First().GetProperty("code").GetString();

        await Rest.ReadAsync(
            await shopper.PutAsJsonAsync(
                $"/api/v1/store/checkout/{sessionId}/shipping",
                new { perVendor = new[] { new { vendorId, optionCode = chosenCode } } },
                cancellationToken),
            cancellationToken);

        await Rest.ReadAsync(
            await shopper.PutAsJsonAsync(
                $"/api/v1/store/checkout/{sessionId}/payment-method",
                new { method = "cod" },
                cancellationToken),
            cancellationToken);

        await Rest.ReadAsync(
            await shopper.GetAsync(
                new Uri($"/api/v1/store/checkout/{sessionId}/review", UriKind.Relative),
                cancellationToken),
            cancellationToken);

        var placed = await Rest.ReadAsync(
            await PlaceAsync(shopper, sessionId, $"order-{Guid.NewGuid():N}"),
            cancellationToken);

        return (placed.GetProperty("orderId").GetGuid(), placed.GetProperty("orderNumber").GetString()!);
    }

    /// <summary>Places an order with a caller-chosen idempotency key, for the tests that reuse one.</summary>
    /// <param name="shopper">A client signed in as the shopper.</param>
    /// <param name="sessionId">The checkout session.</param>
    /// <param name="idempotencyKey">The header value.</param>
    public Task<HttpResponseMessage> PlaceAsync(HttpClient shopper, Guid sessionId, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/store/checkout/{sessionId}/place-order");

        request.Headers.Add("Idempotency-Key", idempotencyKey);

        return shopper.SendAsync(request, cancellationToken);
    }

    /// <summary>Reads an order as staff, for the sub-order id a shipment is created against.</summary>
    /// <param name="orderId">The order.</param>
    public async Task<ShippingPlacedOrder> LoadAsync(Guid orderId)
    {
        var order = await Rest.ReadAsync(
            await admin.GetAsync(new Uri($"/api/v1/admin/orders/{orderId}", UriKind.Relative), cancellationToken),
            cancellationToken);

        var subOrder = order.GetProperty("subOrders").EnumerateArray().First();

        return new ShippingPlacedOrder(
            orderId,
            order.GetProperty("orderNumber").GetString()!,
            subOrder.GetProperty("id").GetGuid(),
            subOrder.GetProperty("subOrderNumber").GetString()!,
            order.GetProperty("customerId").GetGuid());
    }

    /// <summary>
    /// The whole road from an empty basket to a confirmed sub-order, in one call.
    /// </summary>
    /// <remarks>
    /// Also drains the outbox, when a factory and a database are given. A cash-on-delivery
    /// confirmation opens its cash record from <c>SubOrderConfirmed</c>
    /// (<c>Payments.OrderLifecycleHandlers</c>), which the API never dispatches on its own — a test
    /// that goes on to book a parcel and expects <c>payments.cod_collections</c> to exist needs the
    /// worker's half supplied, exactly as <see cref="OutboxDrain"/> documents.
    /// </remarks>
    /// <param name="shopper">A client signed in as the shopper.</param>
    /// <param name="stateId">The destination state.</param>
    /// <param name="vendorId">The seller.</param>
    /// <param name="listingId">The offer to buy.</param>
    /// <param name="quantity">How many units.</param>
    /// <param name="pincode">The delivery PIN code.</param>
    /// <param name="factory">The host, so the cash-on-delivery event handler can be drained.</param>
    /// <param name="database">Direct SQL, for the drain to observe the queue emptying.</param>
    public async Task<ShippingPlacedOrder> ConfirmedSubOrderAsync(
        HttpClient shopper,
        Guid stateId,
        Guid vendorId,
        Guid listingId,
        int quantity = 1,
        string pincode = "500034",
        CommerceApiFactory? factory = null,
        Sql? database = null)
    {
        var addressId = await AddressAsync(shopper, stateId, pincode);

        await Rest.ReadAsync(await AddToCartAsync(shopper, listingId, quantity), cancellationToken);

        var (orderId, _) = await PlaceCodOrderAsync(shopper, addressId, vendorId);

        if (factory is not null && database is not null)
        {
            await OutboxDrain.RunAsync(factory, database, cancellationToken);
        }

        return await LoadAsync(orderId);
    }
}
