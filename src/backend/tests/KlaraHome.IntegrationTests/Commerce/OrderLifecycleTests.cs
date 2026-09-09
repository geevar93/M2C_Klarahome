using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Step 14's own acceptance criteria: a basket from two sellers becomes one order and two
/// sub-orders that live independent lives, the money and the stock behind the placement are spent
/// exactly once, a placement that falls over leaves nothing behind, and every lifecycle event
/// reaches the outbox in the transaction that caused it.
/// </summary>
/// <param name="fixture">The migrated database.</param>
public sealed class OrderLifecycleTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// <c>place-order</c> end to end for cash on delivery: the order is created, both sub-orders
    /// reach <c>Confirmed</c>, the cart's stock reservations are committed, and the coupon is spent
    /// exactly once.
    /// </summary>
    /// <remarks>
    /// Everything through the routes a shopper's browser calls, the coupon included. The stock
    /// assertion is made against the reservation rows rather than against an availability number,
    /// because committing and releasing both leave availability where it started and only the row
    /// says which of the two happened.
    /// </remarks>
    [Fact]
    public async Task Cash_on_delivery_places_a_two_vendor_order_confirms_both_parts_and_spends_the_coupon_once()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new OrderScenario(admin, Cancellation);

        var taxonomy = await scenario.TaxonomyAsync();
        var first = await scenario.SellerAsync(taxonomy, sellingPrice: 999m);
        var second = await scenario.SellerAsync(taxonomy, sellingPrice: 1199m);

        var (client, _) = await SignedInShopperAsync();
        var shopper = await scenario.ShopperAsync(client);

        await scenario.AddToCartAsync(shopper, first.ListingId, quantity: 2);
        await scenario.AddToCartAsync(shopper, second.ListingId);

        var (promotionId, code) = await scenario.CouponAsync(percent: 10m);
        var basket = await scenario.ApplyCouponAsync(shopper, code);

        var applied = Assert.Single(
            basket.GetProperty("quote").GetProperty("promotions").EnumerateArray(),
            promotion => promotion.GetProperty("promotionId").GetGuid() == promotionId);

        Assert.True(applied.GetProperty("applied").GetBoolean(), "The coupon was not applied to the basket.");
        Assert.True(applied.GetProperty("discountAmount").GetDecimal() > 0m);

        var placed = await scenario.PlaceAsync(shopper);

        // One order, two sellers, both confirmed: cash on delivery is accepted rather than paid
        // for, so nothing is waiting on a gateway.
        var order = await ReadAsync(await shopper.Client.GetAsync(
            new Uri($"/api/v1/store/orders/{placed.OrderId}", UriKind.Relative),
            Cancellation));

        Assert.Equal("InProgress", order.GetProperty("status").GetString());
        Assert.Equal("CashOnDelivery", order.GetProperty("paymentMethod").GetString());
        Assert.Equal("Pending", order.GetProperty("paymentStatus").GetString());

        var subOrders = order.GetProperty("subOrders").EnumerateArray().ToList();
        Assert.Equal(2, subOrders.Count);
        Assert.All(subOrders, subOrder => Assert.Equal("Confirmed", subOrder.GetProperty("status").GetString()));

        Assert.Contains(subOrders, subOrder => subOrder.GetProperty("vendorId").GetGuid() == first.Vendor.Id);
        Assert.Contains(subOrders, subOrder => subOrder.GetProperty("vendorId").GetGuid() == second.Vendor.Id);

        // The sub-order numbers hang off the order number, so a parcel label and a support call
        // agree about which order a seller's part belongs to.
        Assert.All(
            subOrders,
            subOrder => Assert.StartsWith(
                placed.OrderNumber,
                subOrder.GetProperty("subOrderNumber").GetString(),
                StringComparison.Ordinal));

        // The reservations the checkout took against the cart are committed, not merely settled:
        // the units have left stock for this sale.
        Assert.Equal(0, await ReservationsAsync(placed.CartId, "Held"));
        Assert.Equal(0, await ReservationsAsync(placed.CartId, "Released"));
        Assert.Equal(2, await ReservationsAsync(placed.CartId, "Committed"));

        // Two units of the first seller's offer gone from supply, and nothing still held.
        var (onHand, reserved, _) = await scenario.StockLevelsAsync(first.StockItemId);
        Assert.Equal(48, onHand);
        Assert.Equal(0, reserved);

        // And the coupon is redeemed once, against this order.
        Assert.Equal(1, await scenario.RedemptionCountAsync(promotionId));
    }

    /// <summary>
    /// A two-vendor order's parts transition independently — one delivered while the other is
    /// cancelled — and the parent order's derived status follows §5.2 at every step.
    /// </summary>
    /// <remarks>
    /// The two sellers are moved alternately rather than one after the other, because the failure
    /// this guards against is a derivation that reads only the sub-order in hand: a machine that
    /// took the last mover's status as the order's would pass a sequential test and fail this one.
    /// </remarks>
    [Fact]
    public async Task Two_sub_orders_transition_independently_and_the_order_status_is_derived_from_both()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new OrderScenario(admin, Cancellation);

        var taxonomy = await scenario.TaxonomyAsync();
        var shipping = await scenario.SellerAsync(taxonomy);
        var failing = await scenario.SellerAsync(taxonomy);

        var (client, _) = await SignedInShopperAsync();
        var shopper = await scenario.ShopperAsync(client);

        await scenario.AddToCartAsync(shopper, shipping.ListingId);
        await scenario.AddToCartAsync(shopper, failing.ListingId);

        var placed = await scenario.PlaceAsync(shopper);

        var delivering = await scenario.SubOrderIdAsync(placed.OrderId, shipping.Vendor.Id);
        var cancelling = await scenario.SubOrderIdAsync(placed.OrderId, failing.Vendor.Id);

        // One seller works their parcel while the other stands still. The order is in progress
        // throughout, because neither branch has finished.
        await scenario.TransitionAsync(delivering, "Processing");

        Assert.Equal("InProgress", await scenario.OrderStatusAsync(placed.OrderId));
        Assert.Equal("Confirmed", await scenario.SubOrderStatusAsync(placed.OrderId, failing.Vendor.Id));

        // The other seller cannot fulfil and their part is cancelled. Still in progress: the first
        // seller's parcel is real and still coming.
        await scenario.CancelAsync(cancelling, "Out of stock at the warehouse.");

        Assert.Equal("Cancelled", await scenario.SubOrderStatusAsync(placed.OrderId, failing.Vendor.Id));
        Assert.Equal("InProgress", await scenario.OrderStatusAsync(placed.OrderId));

        // The first seller carries on, untouched by the other's cancellation.
        await scenario.DriveAsync(delivering, "Packed", "Shipped", "OutForDelivery", "Delivered");

        Assert.Equal("InProgress", await scenario.OrderStatusAsync(placed.OrderId));

        await scenario.TransitionAsync(delivering, "Completed");

        // §5.2 with the clarification the card records: one part cancelled beside one part
        // completed is a completed order, because the shopper received something.
        Assert.Equal("Completed", await scenario.OrderStatusAsync(placed.OrderId));

        var order = await scenario.ReadOrderAsync(placed.OrderId);

        Assert.NotEqual(JsonValueKind.Null, order.GetProperty("completedAt").ValueKind);

        // What is still owed fell by the cancelled seller's whole part, and by nothing else.
        var cancelled = Assert.Single(
            order.GetProperty("subOrders").EnumerateArray(),
            subOrder => subOrder.GetProperty("vendorId").GetGuid() == failing.Vendor.Id);

        Assert.Equal(
            order.GetProperty("grandTotal").GetDecimal() - cancelled.GetProperty("total").GetDecimal(),
            order.GetProperty("netTotal").GetDecimal());
    }

    /// <summary>
    /// A placement that fails after the order graph is written rolls the order back and reverses
    /// the promotion redemption, leaving the shopper able to try again with nothing lost.
    /// </summary>
    /// <remarks>
    /// The gateway is made unavailable rather than the database, because that is the failure this
    /// path is arranged around and the only one reachable through the API: the order graph and its
    /// number are written, the coupon is redeemed, and only then does the collection refuse. What
    /// must survive is nothing at all.
    /// </remarks>
    [Fact]
    public async Task A_placement_that_fails_at_the_gateway_writes_no_order_and_gives_the_coupon_back()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new OrderScenario(admin, Cancellation);

        var taxonomy = await scenario.TaxonomyAsync();
        var seller = await scenario.SellerAsync(taxonomy);

        var (client, _) = await SignedInShopperAsync();
        var shopper = await scenario.ShopperAsync(client);

        await scenario.AddToCartAsync(shopper, seller.ListingId);

        var (promotionId, code) = await scenario.CouponAsync(percent: 10m);
        await scenario.ApplyCouponAsync(shopper, code);

        var session = await scenario.CheckoutAsync(shopper, "prepaid");
        var sessionId = session.GetProperty("id").GetGuid();
        var cartId = session.GetProperty("cartId").GetGuid();

        Factory.Gateway.IsConfigured = false;

        try
        {
            // A 503 naming the gateway, not the polite refusal Orders registers for a deployment
            // with no Payments module — Step 15 has landed, so PAYMENTS_UNAVAILABLE is no longer
            // reachable through this route.
            await RefusedAsync(
                await scenario.PlaceRawAsync(shopper, sessionId),
                HttpStatusCode.ServiceUnavailable,
                "PAYMENT_PROVIDER_UNAVAILABLE");

            // No order, and no number taken out of the series either: the counter row is inside the
            // rolled-back transaction, which is the entire reason it is a row rather than a sequence.
            Assert.Equal(
                0,
                await Database.CountAsync(
                    "SELECT COUNT(*) FROM orders.orders WHERE cart_id = $1",
                    Cancellation,
                    cartId));

            // The coupon is back. A shopper whose card was declined must not find their discount
            // spent on an order that does not exist.
            Assert.Equal(0, await scenario.RedemptionCountAsync(promotionId));

            // And the stock the attempt held has been put back rather than left to lapse.
            Assert.Equal(0, await ReservationsAsync(cartId, "Held"));
        }
        finally
        {
            Factory.Gateway.IsConfigured = true;
        }

        // They press pay again, and this time it works — same basket, same coupon.
        var placed = await scenario.PlaceAsync(shopper, "prepaid");

        Assert.NotEqual(Guid.Empty, placed.OrderId);
        Assert.Equal(1, await scenario.RedemptionCountAsync(promotionId));

        // Step 15 has landed, so a prepaid placement is answered with a collection to pay rather
        // than with PAYMENTS_UNAVAILABLE, and the order waits for the money.
        Assert.Equal(JsonValueKind.Object, placed.Payment.ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(placed.Payment.GetProperty("providerOrderId").GetString()));
        Assert.Equal("PendingPayment", placed.Status);
    }

    /// <summary>
    /// A pre-confirmation cancellation of the whole order releases the cart-scoped reservations; a
    /// post-confirmation one does not, and announces the quantities for Inventory to restock
    /// instead.
    /// </summary>
    /// <remarks>
    /// The distinction is the one thing a cancellation has to get right about stock. Before
    /// confirmation the units are still a hold against the cart and releasing it puts them straight
    /// back on sale; after confirmation they have left stock for good, and releasing the hold a
    /// second time would put back units that were never taken.
    /// </remarks>
    [Fact]
    public async Task Cancelling_before_confirmation_releases_the_hold_and_cancelling_after_it_does_not()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new OrderScenario(admin, Cancellation);

        var taxonomy = await scenario.TaxonomyAsync();
        var seller = await scenario.SellerAsync(taxonomy);

        // Before confirmation: a prepaid order nobody has paid for is still holding its units.
        var (unpaidClient, _) = await SignedInShopperAsync();
        var unpaid = await scenario.ShopperAsync(unpaidClient);

        await scenario.AddToCartAsync(unpaid, seller.ListingId);

        var pending = await scenario.PlaceAsync(unpaid, "prepaid");

        Assert.Equal(1, await ReservationsAsync(pending.CartId, "Held"));

        await ReadAsync(await unpaid.Client.PostAsJsonAsync(
            $"/api/v1/store/orders/{pending.OrderId}/cancel",
            new { reason = "Changed my mind.", lines = (object?)null },
            Cancellation));

        Assert.Equal("Cancelled", await scenario.OrderStatusAsync(pending.OrderId));
        Assert.Equal(0, await ReservationsAsync(pending.CartId, "Held"));
        Assert.Equal(1, await ReservationsAsync(pending.CartId, "Released"));
        Assert.Equal(0, await ReservationsAsync(pending.CartId, "Committed"));

        // After confirmation: a cash-on-delivery order's units left stock at placement.
        var (paidClient, _) = await SignedInShopperAsync();
        var confirmed = await scenario.ShopperAsync(paidClient);

        await scenario.AddToCartAsync(confirmed, seller.ListingId, quantity: 3);

        var live = await scenario.PlaceAsync(confirmed);

        Assert.Equal(1, await ReservationsAsync(live.CartId, "Committed"));

        await ReadAsync(await confirmed.Client.PostAsJsonAsync(
            $"/api/v1/store/orders/{live.OrderId}/cancel",
            new { reason = "Ordered by mistake.", lines = (object?)null },
            Cancellation));

        Assert.Equal("Cancelled", await scenario.OrderStatusAsync(live.OrderId));

        // Still committed. Nothing released a hold that had already been spent.
        Assert.Equal(1, await ReservationsAsync(live.CartId, "Committed"));
        Assert.Equal(0, await ReservationsAsync(live.CartId, "Released"));

        // And the event that carries the units back to Inventory says so, with the quantities on it.
        var announced = await CancellationEventAsync(live.OrderId);

        Assert.True(
            announced.GetProperty("WasConfirmed").GetBoolean(),
            "The cancellation did not tell Inventory that the units had already been committed.");

        var fact = Assert.Single(announced.GetProperty("Lines").EnumerateArray());

        Assert.Equal(3, fact.GetProperty("Quantity").GetInt32());
    }

    /// <summary>
    /// A partial cancellation leaves the sub-order in its own state, writes
    /// <c>quantity_cancelled</c> on the named lines only, and takes the pro-rata share of the
    /// discounted line total off what is owed.
    /// </summary>
    /// <remarks>
    /// The pro-rata share is the part worth asserting in money. Charging back
    /// <c>quantity × unit price</c> would refund more than the shopper paid on any line carrying a
    /// share of an order-level discount, which is every line of an order with a coupon on it — so
    /// this order has one.
    /// </remarks>
    [Fact]
    public async Task A_partial_cancellation_touches_only_the_named_line_and_refunds_its_discounted_share()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new OrderScenario(admin, Cancellation);

        var taxonomy = await scenario.TaxonomyAsync();
        var seller = await scenario.SellerAsync(taxonomy, sellingPrice: 1000m);
        var other = await scenario.SellerAsync(taxonomy, sellingPrice: 500m);

        var (client, _) = await SignedInShopperAsync();
        var shopper = await scenario.ShopperAsync(client);

        await scenario.AddToCartAsync(shopper, seller.ListingId, quantity: 4);
        await scenario.AddToCartAsync(shopper, other.ListingId, quantity: 2);

        var (_, code) = await scenario.CouponAsync(percent: 10m);
        await scenario.ApplyCouponAsync(shopper, code);

        var placed = await scenario.PlaceAsync(shopper);

        var order = await ReadAsync(await shopper.Client.GetAsync(
            new Uri($"/api/v1/store/orders/{placed.OrderId}", UriKind.Relative),
            Cancellation));

        var subOrder = Assert.Single(
            order.GetProperty("subOrders").EnumerateArray(),
            candidate => candidate.GetProperty("vendorId").GetGuid() == seller.Vendor.Id);

        var subOrderId = subOrder.GetProperty("id").GetGuid();
        var line = Assert.Single(subOrder.GetProperty("lines").EnumerateArray());
        var lineId = line.GetProperty("id").GetGuid();
        var lineTotal = line.GetProperty("lineTotal").GetDecimal();

        // The discount really did land on the line, so the pro-rata assertion below is about
        // something rather than about nothing.
        Assert.True(
            line.GetProperty("discountAmount").GetDecimal() > 0m,
            "The coupon allocated no discount to the line, so this test would prove nothing.");

        var grandTotal = order.GetProperty("grandTotal").GetDecimal();

        var cancelled = await ReadAsync(await shopper.Client.PostAsJsonAsync(
            $"/api/v1/store/sub-orders/{subOrderId}/cancel",
            new
            {
                reason = "One too many.",
                lines = new[] { new { orderLineId = lineId, quantity = 1 } },
            },
            Cancellation));

        var after = Assert.Single(
            cancelled.GetProperty("subOrders").EnumerateArray(),
            candidate => candidate.GetProperty("id").GetGuid() == subOrderId);

        var cancelledLine = Assert.Single(after.GetProperty("lines").EnumerateArray());

        // The sub-order did not move: it is still being packed, and the line already says which
        // units are gone.
        Assert.Equal("Confirmed", after.GetProperty("status").GetString());
        Assert.Equal("PartiallyCancelled", cancelledLine.GetProperty("status").GetString());
        Assert.Equal(4, cancelledLine.GetProperty("quantity").GetInt32());
        Assert.Equal(1, cancelledLine.GetProperty("quantityCancelled").GetInt32());

        // A quarter of the discounted line total came off, not a quarter of four undiscounted units.
        var expected = Math.Round(lineTotal / 4m, 4, MidpointRounding.AwayFromZero);

        Assert.Equal(expected, after.GetProperty("cancelledTotal").GetDecimal());
        Assert.Equal(grandTotal - expected, cancelled.GetProperty("netTotal").GetDecimal());

        // The frozen totals did not move. An order whose agreed figures changed after the fact is
        // an order nobody can reconcile against the confirmation the shopper was sent.
        Assert.Equal(grandTotal, cancelled.GetProperty("grandTotal").GetDecimal());

        // The other seller's part was not touched, which is the "named lines only" half.
        var untouched = Assert.Single(
            cancelled.GetProperty("subOrders").EnumerateArray(),
            candidate => candidate.GetProperty("vendorId").GetGuid() == other.Vendor.Id);

        Assert.Equal(0m, untouched.GetProperty("cancelledTotal").GetDecimal());
        Assert.All(
            untouched.GetProperty("lines").EnumerateArray(),
            remaining => Assert.Equal(0, remaining.GetProperty("quantityCancelled").GetInt32()));

        // And the database agrees, which is where the quantity actually lives.
        Assert.Equal(
            1,
            await Database.ScalarAsync<int>(
                "SELECT quantity_cancelled FROM orders.order_lines WHERE id = $1",
                Cancellation,
                lineId));
    }

    /// <summary>
    /// Every lifecycle event this module publishes is written to <c>platform.outbox_messages</c> in
    /// the same transaction as the change that caused it, from the keyed per-context outbox.
    /// </summary>
    /// <remarks>
    /// The keyed resolution is the part that cannot be assumed. Enqueuing through the unkeyed
    /// registration would add the row to whichever module's context registered first, this module's
    /// save would not write it, and the event would vanish with no error anywhere — so the honest
    /// assertion is that the rows are there after the Orders module's own commit. The refused
    /// transition in the middle is the other half: a change that did not happen announces nothing.
    /// </remarks>
    [Fact]
    public async Task The_six_order_events_are_written_to_the_outbox_with_the_change_that_caused_them()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new OrderScenario(admin, Cancellation);

        var taxonomy = await scenario.TaxonomyAsync();
        var kept = await scenario.SellerAsync(taxonomy);
        var dropped = await scenario.SellerAsync(taxonomy);

        var (client, _) = await SignedInShopperAsync();
        var shopper = await scenario.ShopperAsync(client);

        await scenario.AddToCartAsync(shopper, kept.ListingId);
        await scenario.AddToCartAsync(shopper, dropped.ListingId);

        var placed = await scenario.PlaceAsync(shopper);

        // Placement: one OrderPlaced, and one SubOrderConfirmed per seller, because cash on
        // delivery confirms both inside the placement transaction.
        Assert.Equal(1, await EventsAbout(placed.OrderId, "OrderPlaced"));
        Assert.Equal(2, await EventsAbout(placed.OrderId, "SubOrderConfirmed"));

        var keeping = await scenario.SubOrderIdAsync(placed.OrderId, kept.Vendor.Id);
        var dropping = await scenario.SubOrderIdAsync(placed.OrderId, dropped.Vendor.Id);

        await scenario.TransitionAsync(keeping, "Processing");

        Assert.Equal(1, await EventsAbout(placed.OrderId, "SubOrderStatusChanged"));

        // Packing raises the tax invoice, and the invoice announces itself.
        await scenario.TransitionAsync(keeping, "Packed");

        Assert.Equal(2, await EventsAbout(placed.OrderId, "SubOrderStatusChanged"));
        Assert.Equal(1, await EventsAbout(placed.OrderId, "InvoiceIssued"));

        // A move the machine does not have announces nothing at all.
        await RefusedAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/sub-orders/{keeping}/transition",
                new { status = "Delivered", reason = (string?)null },
                Cancellation),
            HttpStatusCode.Conflict,
            "ORDER_INVALID_TRANSITION");

        Assert.Equal(2, await EventsAbout(placed.OrderId, "SubOrderStatusChanged"));

        await scenario.CancelAsync(dropping, "Cannot fulfil.");

        Assert.Equal(1, await EventsAbout(placed.OrderId, "SubOrderCancelled"));
        Assert.Equal(3, await EventsAbout(placed.OrderId, "SubOrderStatusChanged"));

        await scenario.DriveAsync(keeping, "Shipped", "OutForDelivery", "Delivered", "Completed");

        Assert.Equal("Completed", await scenario.OrderStatusAsync(placed.OrderId));
        Assert.Equal(1, await EventsAbout(placed.OrderId, "OrderCompleted"));
    }

    /// <summary>
    /// An order whose last outstanding part is <em>cancelled</em> rather than completed still
    /// reaches <c>Completed</c>, and still announces it.
    /// </summary>
    /// <remarks>
    /// The same derivation as the test above by a different route, and the route is the point:
    /// completion can be caused by a cancellation, because a cancelled part beside a completed one
    /// is a completed order. Settlement, loyalty and the "your order is complete" message all hang
    /// off <c>OrderCompleted</c>, so an order that reaches the state without announcing it is one
    /// nobody downstream ever hears about.
    /// </remarks>
    [Fact]
    public async Task An_order_completed_by_cancelling_its_last_open_part_still_announces_completion()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new OrderScenario(admin, Cancellation);

        var taxonomy = await scenario.TaxonomyAsync();
        var delivered = await scenario.SellerAsync(taxonomy);
        var abandoned = await scenario.SellerAsync(taxonomy);

        var (client, _) = await SignedInShopperAsync();
        var shopper = await scenario.ShopperAsync(client);

        await scenario.AddToCartAsync(shopper, delivered.ListingId);
        await scenario.AddToCartAsync(shopper, abandoned.ListingId);

        var placed = await scenario.PlaceAsync(shopper);

        var finished = await scenario.SubOrderIdAsync(placed.OrderId, delivered.Vendor.Id);
        var giving = await scenario.SubOrderIdAsync(placed.OrderId, abandoned.Vendor.Id);

        // The first seller goes all the way to completion. The order is still in progress, because
        // the second seller has not finished.
        await scenario.DriveAsync(
            finished,
            "Processing",
            "Packed",
            "Shipped",
            "OutForDelivery",
            "Delivered",
            "Completed");

        Assert.Equal("InProgress", await scenario.OrderStatusAsync(placed.OrderId));
        Assert.Equal(0, await EventsAbout(placed.OrderId, "OrderCompleted"));

        // The second seller's part is cancelled, which is what finishes the order.
        await scenario.CancelAsync(giving, "Cannot fulfil.");

        Assert.Equal("Completed", await scenario.OrderStatusAsync(placed.OrderId));
        Assert.Equal(1, await EventsAbout(placed.OrderId, "OrderCompleted"));
    }

    /// <summary>
    /// The shopper-visible timeline never carries an entry that is not customer-visible, operator
    /// notes included.
    /// </summary>
    [Fact]
    public async Task The_storefront_timeline_hides_every_entry_that_is_not_customer_visible()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new OrderScenario(admin, Cancellation);

        var taxonomy = await scenario.TaxonomyAsync();
        var seller = await scenario.SellerAsync(taxonomy);

        var (client, _) = await SignedInShopperAsync();
        var shopper = await scenario.ShopperAsync(client);

        await scenario.AddToCartAsync(shopper, seller.ListingId);

        var placed = await scenario.PlaceAsync(shopper);
        var subOrderId = await scenario.SubOrderIdAsync(placed.OrderId, seller.Vendor.Id);

        const string Secret = "Customer looks like a fraud risk; hold the parcel.";

        await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/orders/{placed.OrderId}/notes",
            new { message = Secret, isCustomerVisible = false },
            Cancellation));

        // Confirmed to Processing is a seller's queue rather than an event in the life of the
        // shopper's order, and it is written invisible.
        await scenario.TransitionAsync(subOrderId, "Processing");

        var timeline = await ReadAsync(await shopper.Client.GetAsync(
            new Uri($"/api/v1/store/orders/{placed.OrderId}/timeline", UriKind.Relative),
            Cancellation));

        var entries = timeline.EnumerateArray().ToList();

        Assert.NotEmpty(entries);
        Assert.All(entries, entry => Assert.True(entry.GetProperty("isCustomerVisible").GetBoolean()));
        Assert.DoesNotContain(entries, entry => entry.GetProperty("message").GetString() == Secret);
        Assert.DoesNotContain(entries, entry => entry.GetProperty("toStatus").GetString() == "Processing");

        // The same is true of the order read, which carries a timeline of its own — and of the
        // operator's running note, which the storefront never returns at all.
        var order = await ReadAsync(await shopper.Client.GetAsync(
            new Uri($"/api/v1/store/orders/{placed.OrderId}", UriKind.Relative),
            Cancellation));

        Assert.Equal(JsonValueKind.Null, order.GetProperty("notes").ValueKind);
        Assert.All(
            order.GetProperty("timeline").EnumerateArray(),
            entry => Assert.True(entry.GetProperty("isCustomerVisible").GetBoolean()));

        // Staff see the whole of it, which is what makes the omission above an omission rather than
        // an entry that was never written.
        var staffView = await scenario.ReadOrderAsync(placed.OrderId);

        Assert.Contains(
            staffView.GetProperty("timeline").EnumerateArray(),
            entry => entry.GetProperty("message").GetString() == Secret);
    }

    /// <summary>How many of a cart's stock holds are in a given state.</summary>
    /// <param name="cartId">The basket the holds were taken against.</param>
    /// <param name="status">The reservation state.</param>
    private async Task<long> ReservationsAsync(Guid cartId, string status)
        => await Database.CountAsync(
            "SELECT COUNT(*) FROM inventory.stock_reservations "
            + "WHERE reference_type = 'cart' AND reference_id = $1 AND status = $2",
            Cancellation,
            cartId,
            status);

    /// <summary>
    /// The one <c>SubOrderCancelled</c> the outbox holds about an order, parsed.
    /// </summary>
    /// <remarks>
    /// Parsed rather than matched as a substring: the column is <c>jsonb</c>, so Postgres rewrites
    /// the document — key order and whitespace both — and a test that searched the text would be
    /// asserting the storage format rather than the payload.
    /// </remarks>
    /// <param name="orderId">The order.</param>
    private async Task<JsonElement> CancellationEventAsync(Guid orderId)
    {
        var rows = await Database.RowsAsync(
            "SELECT payload::text AS payload FROM platform.outbox_messages "
            + "WHERE type LIKE '%SubOrderCancelled%' AND payload::text LIKE $1",
            Cancellation,
            $"%{orderId}%");

        var payload = Assert.Single(rows)["payload"] as string;

        Assert.NotNull(payload);

        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    /// <summary>How many events of a kind the outbox holds about one order.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="eventType">The contract type's name.</param>
    private async Task<long> EventsAbout(Guid orderId, string eventType)
        => await Database.CountAsync(
            "SELECT COUNT(*) FROM platform.outbox_messages WHERE type LIKE $1 AND payload::text LIKE $2",
            Cancellation,
            $"%{eventType}%",
            $"%{orderId}%");
}
