using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Orders.Domain;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The state machine, through the API rather than through the table alone: every pair the machine
/// has no edge for is refused as a conflict, every pair the caller may not take is refused as a
/// forbidden, and the next states the API publishes are exactly the ones it will accept.
/// </summary>
/// <remarks>
/// <para>
/// The transition table is unit-tested as a pure function already. What that cannot show is that the
/// endpoint consults it — a handler that parsed the requested status and assigned it would pass
/// every one of those tests. So this walks a real sub-order through its life and, at each state,
/// asks the API for every state in the enum.
/// </para>
/// <para>
/// The legal moves are not probed at each stop, because taking one would move the sub-order out of
/// the state under test. They are proved a different way: the response's <c>nextStatuses</c> is
/// asserted to be exactly the legal set, and the walk then takes one of them, so every edge the
/// walk uses is an edge the API published and honoured.
/// </para>
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class OrderStateMachineTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// Walking a sub-order from confirmation to completion: at every state the published next
    /// states are the enforced ones, and every other state in the enum answers <c>409</c>.
    /// </summary>
    [Fact]
    public async Task Every_pair_the_machine_does_not_have_is_refused_as_a_conflict_at_every_state()
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

        // The fulfilment chain and the return branch beyond it, which between them cover every state
        // a sub-order reaches without a courier failure.
        SubOrderStatus[] walk =
        [
            SubOrderStatus.Confirmed,
            SubOrderStatus.Processing,
            SubOrderStatus.Packed,
            SubOrderStatus.Shipped,
            SubOrderStatus.OutForDelivery,
            SubOrderStatus.Delivered,
            SubOrderStatus.ReturnRequested,
            SubOrderStatus.ReturnInProgress,
            SubOrderStatus.Returned,
        ];

        for (var step = 0; step < walk.Length; step++)
        {
            var from = walk[step];

            await AssertPublishedNextStatesAsync(scenario, placed.OrderId, seller.Vendor.Id, from);
            await AssertEveryAbsentEdgeIsAConflictAsync(admin, subOrderId, from);

            if (step + 1 < walk.Length)
            {
                await scenario.TransitionAsync(subOrderId, walk[step + 1].ToString());
            }
        }

        // Returned is terminal, so the walk ends where nothing may follow — which the loop above has
        // just proved by refusing all sixteen.
        Assert.True(SubOrderLifecycle.IsTerminal(SubOrderStatus.Returned));
    }

    /// <summary>
    /// The courier branch, which the happy path never reaches: a failed delivery, a re-attempt, and
    /// a parcel written off back to the seller.
    /// </summary>
    [Fact]
    public async Task The_delivery_failure_branch_is_a_loop_and_ends_at_a_returned_parcel()
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

        await scenario.DriveAsync(subOrderId, "Processing", "Packed", "Shipped", "OutForDelivery");

        // An NDR, and then the courier tries again. The loop is what makes a second attempt possible
        // at all; a machine that treated DeliveryFailed as a terminus would strand the parcel.
        await scenario.TransitionAsync(subOrderId, "DeliveryFailed");
        await AssertEveryAbsentEdgeIsAConflictAsync(admin, subOrderId, SubOrderStatus.DeliveryFailed);

        await scenario.TransitionAsync(subOrderId, "OutForDelivery");
        await scenario.TransitionAsync(subOrderId, "DeliveryFailed");

        // Attempts exhausted: the parcel goes back to the seller.
        await scenario.TransitionAsync(subOrderId, "RtoInitiated");
        await AssertEveryAbsentEdgeIsAConflictAsync(admin, subOrderId, SubOrderStatus.RtoInitiated);

        await scenario.TransitionAsync(subOrderId, "RtoDelivered");

        // The order as a whole is finished, because a parcel back with the seller is a terminal
        // success of the machine even though nobody received anything.
        Assert.Equal("Completed", await scenario.OrderStatusAsync(placed.OrderId));
        await AssertEveryAbsentEdgeIsAConflictAsync(admin, subOrderId, SubOrderStatus.RtoDelivered);
    }

    /// <summary>
    /// The states before payment and the two terminal states the other walks do not reach: nothing
    /// leaves a finished sub-order, and a payment can be retried but not skipped.
    /// </summary>
    /// <remarks>
    /// Between this and the two walks above, every one of the sixteen states has been asked for
    /// every one of the sixteen, which is what the debt row means by "every pair absent from the
    /// table". <c>PaymentFailed</c> is reachable here because relaying a declined payment is open to
    /// Operations as well as to the gateway — an operator has to be able to record what a lost
    /// webhook would have.
    /// </remarks>
    [Fact]
    public async Task The_states_before_payment_and_the_terminal_ones_refuse_every_edge_the_table_lacks()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new OrderScenario(admin, Cancellation);

        var taxonomy = await scenario.TaxonomyAsync();
        var seller = await scenario.SellerAsync(taxonomy);

        // A prepaid order nobody has paid for, which is where every prepaid order starts.
        var (client, _) = await SignedInShopperAsync();
        var shopper = await scenario.ShopperAsync(client);

        await scenario.AddToCartAsync(shopper, seller.ListingId);

        var pending = await scenario.PlaceAsync(shopper, "prepaid");
        var pendingSubOrder = await scenario.SubOrderIdAsync(pending.OrderId, seller.Vendor.Id);

        await AssertPublishedNextStatesAsync(scenario, pending.OrderId, seller.Vendor.Id, SubOrderStatus.PendingPayment);
        await AssertEveryAbsentEdgeIsAConflictAsync(admin, pendingSubOrder, SubOrderStatus.PendingPayment);

        // A seller cannot confirm their own order. The edge exists — it is the gateway's word, or an
        // operator's when a webhook was lost — and a seller who could take it would be shipping
        // goods nobody has paid for.
        var (vendor, _) = await SignedInVendorOwnerAsync(admin, seller.Vendor.Id);

        await AssertVendorRefusedAsync(vendor, pendingSubOrder, "Confirmed");

        // The gateway declines, and the shopper is allowed to try again: a declined card is a retry
        // rather than a dead order.
        await scenario.TransitionAsync(pendingSubOrder, "PaymentFailed");
        await AssertEveryAbsentEdgeIsAConflictAsync(admin, pendingSubOrder, SubOrderStatus.PaymentFailed);

        await scenario.TransitionAsync(pendingSubOrder, "PendingPayment");

        // They give up instead. Cancelled is terminal, so nothing at all follows it.
        await ReadAsync(await shopper.Client.PostAsJsonAsync(
            $"/api/v1/store/orders/{pending.OrderId}/cancel",
            new { reason = "Changed my mind.", lines = (object?)null },
            Cancellation));

        Assert.Equal("Cancelled", await scenario.SubOrderStatusAsync(pending.OrderId, seller.Vendor.Id));
        Assert.Empty(Legal(SubOrderStatus.Cancelled, OrderActor.Platform));

        await AssertEveryAbsentEdgeIsAConflictAsync(admin, pendingSubOrder, SubOrderStatus.Cancelled);

        // And the other terminal state, reached the long way round.
        var (buyerClient, _) = await SignedInShopperAsync();
        var buyer = await scenario.ShopperAsync(buyerClient);

        await scenario.AddToCartAsync(buyer, seller.ListingId);

        var completed = await scenario.PlaceAsync(buyer);
        var completedSubOrder = await scenario.SubOrderIdAsync(completed.OrderId, seller.Vendor.Id);

        await scenario.DriveAsync(
            completedSubOrder,
            "Processing",
            "Packed",
            "Shipped",
            "OutForDelivery",
            "Delivered",
            "Completed");

        Assert.Empty(Legal(SubOrderStatus.Completed, OrderActor.Platform));

        await AssertEveryAbsentEdgeIsAConflictAsync(admin, completedSubOrder, SubOrderStatus.Completed);
    }

    /// <summary>
    /// A seller may drive their own fulfilment and nothing else: the edges that are the gateway's
    /// word, the courier's word or Operations' decision answer <c>403</c> rather than moving.
    /// </summary>
    /// <remarks>
    /// The difference between this and the conflict above is the difference between "that cannot
    /// happen" and "that is not yours to do", and collapsing them would make "already packed" and
    /// "not your order" indistinguishable in a log. It matters more here than anywhere: a seller who
    /// could declare their own parcel delivered would start the return window and the settlement
    /// clock on a parcel still in their warehouse.
    /// </remarks>
    [Fact]
    public async Task A_seller_is_refused_the_edges_reserved_for_operations_and_the_platform()
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

        var (vendor, _) = await SignedInVendorOwnerAsync(admin, seller.Vendor.Id);

        // The seller's own work is theirs: accept, pick, pack, hand over.
        await AssertVendorMayAsync(vendor, subOrderId, "Processing");
        await AssertVendorMayAsync(vendor, subOrderId, "Packed");
        await AssertVendorMayAsync(vendor, subOrderId, "Shipped");

        // The courier's word is not theirs. Every one of these is an edge the machine has, taken by
        // somebody who may not take it.
        await AssertVendorRefusedAsync(vendor, subOrderId, "OutForDelivery");

        await scenario.TransitionAsync(subOrderId, "OutForDelivery");

        await AssertVendorRefusedAsync(vendor, subOrderId, "Delivered");
        await AssertVendorRefusedAsync(vendor, subOrderId, "DeliveryFailed");

        await scenario.TransitionAsync(subOrderId, "Delivered");

        // Nor is completing the order, which is when settlement becomes payable.
        await AssertVendorRefusedAsync(vendor, subOrderId, "Completed");

        // And the next states the API offers a seller never include any of them, so a vendor portal
        // that renders the buttons it is given cannot offer one that would be refused.
        var theirs = await ReadAsync(await vendor.GetAsync(
            new Uri($"/api/v1/admin/orders/{placed.OrderId}", UriKind.Relative),
            Cancellation));

        var subOrder = Assert.Single(theirs.GetProperty("subOrders").EnumerateArray());

        Assert.Equal(
            Legal(SubOrderStatus.Delivered, OrderActor.Vendor),
            Published(subOrder));
    }

    /// <summary>
    /// A status that is not one this platform has is refused by name rather than by the model
    /// binder.
    /// </summary>
    /// <remarks>
    /// The route takes the target state as a string on purpose, so an unknown value is a 422 that
    /// says what was wrong instead of a 400 from the binder that does not.
    /// </remarks>
    [Fact]
    public async Task An_unknown_status_is_refused_with_a_named_error_rather_than_a_binder_failure()
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

        await RefusedAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/sub-orders/{subOrderId}/transition",
                new { status = "Teleported", reason = (string?)null },
                Cancellation),
            HttpStatusCode.UnprocessableEntity,
            "ORDER_UNKNOWN_STATUS");

        // Case is not the caller's problem: the status is parsed case-insensitively, so a client
        // sending the wire value in a different shape still moves the order.
        await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/sub-orders/{subOrderId}/transition",
            new { status = "processing", reason = (string?)null },
            Cancellation));

        Assert.Equal("Processing", await scenario.SubOrderStatusAsync(placed.OrderId, seller.Vendor.Id));
    }

    /// <summary>
    /// The shopper's cancellation right ends at <c>Packed</c>: after dispatch the machine still has
    /// the edge but it is not theirs (<c>403</c>), and after delivery it has no edge at all
    /// (<c>409</c>).
    /// </summary>
    /// <remarks>
    /// Both halves matter to the storefront. A 403 means "ring support and they can stop it"; a 409
    /// means nobody can, and the shopper should be offered a return instead.
    /// </remarks>
    [Fact]
    public async Task A_shoppers_cancellation_right_ends_at_packed_and_the_two_refusals_differ()
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

        // Packed is the last state a shopper may stop it in, and the API says so.
        await scenario.DriveAsync(subOrderId, "Processing", "Packed");

        var packed = await scenario.SubOrderOfAsync(placed.OrderId, seller.Vendor.Id);
        Assert.True(packed.GetProperty("isCancellable").GetBoolean());

        await scenario.TransitionAsync(subOrderId, "Shipped");

        var shipped = await scenario.SubOrderOfAsync(placed.OrderId, seller.Vendor.Id);
        Assert.False(shipped.GetProperty("isCancellable").GetBoolean());

        // The parcel is with a courier: only Operations may recall it, so the shopper is forbidden
        // rather than told it cannot be done.
        await RefusedAsync(
            await shopper.Client.PostAsJsonAsync(
                $"/api/v1/store/sub-orders/{subOrderId}/cancel",
                new { reason = "Please stop it.", lines = (object?)null },
                Cancellation),
            HttpStatusCode.Forbidden,
            "ORDER_TRANSITION_NOT_PERMITTED");

        // Operations can, and must say why.
        await RefusedAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/sub-orders/{subOrderId}/cancel",
                new { reason = (string?)null, lines = (object?)null },
                Cancellation),
            HttpStatusCode.UnprocessableEntity,
            "ORDER_CANCELLATION_REASON_REQUIRED");

        await scenario.DriveAsync(subOrderId, "OutForDelivery", "Delivered");

        // Delivered has no cancellation edge for anybody. The shopper's route to redress from here
        // is a return, and the code says so.
        await RefusedAsync(
            await shopper.Client.PostAsJsonAsync(
                $"/api/v1/store/sub-orders/{subOrderId}/cancel",
                new { reason = "Too late.", lines = (object?)null },
                Cancellation),
            HttpStatusCode.Conflict,
            "ORDER_NOT_CANCELLABLE");

        await RefusedAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/sub-orders/{subOrderId}/cancel",
                new { reason = "Operations cannot either.", lines = (object?)null },
                Cancellation),
            HttpStatusCode.Conflict,
            "ORDER_NOT_CANCELLABLE");
    }

    /// <summary>The states an actor may reach from one, as the machine has them.</summary>
    /// <param name="from">The current status.</param>
    /// <param name="actor">Who is asking.</param>
    private static HashSet<string> Legal(SubOrderStatus from, OrderActor actor)
        => [.. SubOrderLifecycle.NextFrom(from, actor).Select(status => status.ToString())];

    /// <summary>The states the API says may be reached, off a projected sub-order.</summary>
    /// <param name="subOrder">A <c>SubOrderResponse</c>.</param>
    private static HashSet<string> Published(JsonElement subOrder)
        => [.. subOrder.GetProperty("nextStatuses").EnumerateArray().Select(status => status.GetString()!)];

    /// <summary>Asserts the next states the API publishes are exactly the ones the table allows.</summary>
    private static async Task AssertPublishedNextStatesAsync(
        OrderScenario scenario,
        Guid orderId,
        Guid vendorId,
        SubOrderStatus from)
    {
        var subOrder = await scenario.SubOrderOfAsync(orderId, vendorId);

        Assert.Equal(from.ToString(), subOrder.GetProperty("status").GetString());

        Assert.Equal(
            Legal(from, OrderActor.Platform),
            Published(subOrder));
    }

    /// <summary>
    /// Asks the API for every state the machine has no edge to from here, and requires a conflict
    /// each time.
    /// </summary>
    /// <remarks>
    /// The legal ones are deliberately not asked for: taking one would move the sub-order out of the
    /// state under test, and the walk takes exactly one of them afterwards.
    /// </remarks>
    private static async Task AssertEveryAbsentEdgeIsAConflictAsync(
        HttpClient admin,
        Guid subOrderId,
        SubOrderStatus from)
    {
        foreach (var to in Enum.GetValues<SubOrderStatus>())
        {
            if (SubOrderLifecycle.IsTransitionAllowed(from, to))
            {
                continue;
            }

            await RefusedAsync(
                await admin.PostAsJsonAsync(
                    $"/api/v1/admin/sub-orders/{subOrderId}/transition",
                    new { status = to.ToString(), reason = (string?)null },
                    Cancellation),
                HttpStatusCode.Conflict,
                "ORDER_INVALID_TRANSITION");
        }
    }

    /// <summary>Asserts a seller may take an edge, by taking it.</summary>
    private static async Task AssertVendorMayAsync(HttpClient vendor, Guid subOrderId, string status)
    {
        var moved = await ReadAsync(await vendor.PostAsJsonAsync(
            $"/api/v1/admin/sub-orders/{subOrderId}/transition",
            new { status, reason = (string?)null },
            Cancellation));

        Assert.Equal(status, moved.GetProperty("status").GetString());
    }

    /// <summary>Asserts a seller is forbidden an edge that exists but is not theirs.</summary>
    private static async Task AssertVendorRefusedAsync(HttpClient vendor, Guid subOrderId, string status)
        => await RefusedAsync(
            await vendor.PostAsJsonAsync(
                $"/api/v1/admin/sub-orders/{subOrderId}/transition",
                new { status, reason = (string?)null },
                Cancellation),
            HttpStatusCode.Forbidden,
            "ORDER_TRANSITION_NOT_PERMITTED");
}
