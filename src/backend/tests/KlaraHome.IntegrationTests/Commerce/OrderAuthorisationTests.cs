using System.Net;
using System.Net.Http.Json;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Who may see and do what to an order. A shopper reaches only their own, a seller reaches only
/// their own part, and the refusal is a <c>404</c> everywhere the alternative would confirm that
/// somebody else's order is real.
/// </summary>
/// <remarks>
/// The status code is the assertion, not a detail of it. <c>07-security-compliance.md</c> §2 rules
/// out existence disclosure, and the difference between 403 and 404 on a resource addressed by an
/// opaque id <em>is</em> the disclosure: 403 says "that order exists and is not yours", which is
/// exactly the fact being withheld.
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class OrderAuthorisationTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>The four permissions this module's endpoints declare.</summary>
    private static readonly string[] OrderPermissions =
    [
        "orders.order.read",
        "orders.order.transition",
        "orders.order.cancel",
        "orders.invoice.manage",
    ];

    /// <summary>
    /// A shopper cannot read, follow, cancel or invoice another shopper's order: every storefront
    /// route answers the same 404 an invented id gets.
    /// </summary>
    [Fact]
    public async Task A_customer_reaching_another_shoppers_order_gets_the_same_404_an_invented_id_gets()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new OrderScenario(admin, Cancellation);

        var taxonomy = await scenario.TaxonomyAsync();
        var seller = await scenario.SellerAsync(taxonomy);

        var (ownerClient, _) = await SignedInShopperAsync();
        var owner = await scenario.ShopperAsync(ownerClient);

        await scenario.AddToCartAsync(owner, seller.ListingId);

        var placed = await scenario.PlaceAsync(owner);
        var subOrderId = await scenario.SubOrderIdAsync(placed.OrderId, seller.Vendor.Id);

        var (strangerClient, _) = await SignedInShopperAsync();
        await scenario.ShopperAsync(strangerClient);

        var invented = Guid.NewGuid();

        // Reading it, following it, and listing its invoices.
        foreach (var path in new[]
                 {
                     $"/api/v1/store/orders/{placed.OrderId}",
                     $"/api/v1/store/orders/{placed.OrderId}/timeline",
                     $"/api/v1/store/orders/{placed.OrderId}/invoices",
                 })
        {
            await RefusedAsync(
                await strangerClient.GetAsync(new Uri(path, UriKind.Relative), Cancellation),
                HttpStatusCode.NotFound,
                "ORDER_NOT_FOUND");
        }

        // Cancelling it, in whole and in part.
        await RefusedAsync(
            await strangerClient.PostAsJsonAsync(
                $"/api/v1/store/orders/{placed.OrderId}/cancel",
                new { reason = "Not mine.", lines = (object?)null },
                Cancellation),
            HttpStatusCode.NotFound,
            "ORDER_NOT_FOUND");

        await RefusedAsync(
            await strangerClient.PostAsJsonAsync(
                $"/api/v1/store/sub-orders/{subOrderId}/cancel",
                new { reason = "Still not mine.", lines = (object?)null },
                Cancellation),
            HttpStatusCode.NotFound,
            "ORDER_NOT_FOUND");

        // An id that never existed answers identically, which is what makes the answers above an
        // omission rather than a distinguishable refusal.
        await RefusedAsync(
            await strangerClient.GetAsync(new Uri($"/api/v1/store/orders/{invented}", UriKind.Relative), Cancellation),
            HttpStatusCode.NotFound,
            "ORDER_NOT_FOUND");

        // Their own list does not carry it either, which is the other half of "cannot read".
        var mine = await ReadAsync(await strangerClient.GetAsync(
            new Uri("/api/v1/store/orders", UriKind.Relative),
            Cancellation));

        Assert.DoesNotContain(
            mine.GetProperty("items").EnumerateArray(),
            row => row.GetProperty("id").GetGuid() == placed.OrderId);

        // And the fulfilment surface is closed to a shopper before an order is even looked up: they
        // hold no permission, so the transition route refuses them at the policy rather than
        // resolving an id at all.
        await RefusedAsync(
            await strangerClient.PostAsJsonAsync(
                $"/api/v1/admin/sub-orders/{subOrderId}/transition",
                new { status = "Processing", reason = (string?)null },
                Cancellation),
            HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// A seller sees their own part of a shared basket and nothing else: not the other seller's
    /// sub-order, not the shell of an order they have no part in, and not its timeline.
    /// </summary>
    /// <remarks>
    /// The order <em>shell</em> is the part worth being careful about. The vendor query filter hides
    /// the sub-orders, and a handler that answered with what was left would be handing over the
    /// customer's name, their address and what they paid — for an order this seller has nothing to
    /// do with.
    /// </remarks>
    [Fact]
    public async Task A_seller_sees_only_their_own_part_and_never_the_shell_of_someone_elses_order()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new OrderScenario(admin, Cancellation);

        var taxonomy = await scenario.TaxonomyAsync();
        var mine = await scenario.SellerAsync(taxonomy);
        var theirs = await scenario.SellerAsync(taxonomy);
        var uninvolved = await scenario.SellerAsync(taxonomy);

        var (client, _) = await SignedInShopperAsync();
        var shopper = await scenario.ShopperAsync(client);

        await scenario.AddToCartAsync(shopper, mine.ListingId);
        await scenario.AddToCartAsync(shopper, theirs.ListingId);

        var shared = await scenario.PlaceAsync(shopper);

        // A second order the third seller has no part in at all.
        var (otherClient, _) = await SignedInShopperAsync();
        var otherShopper = await scenario.ShopperAsync(otherClient);

        await scenario.AddToCartAsync(otherShopper, mine.ListingId);

        var elsewhere = await scenario.PlaceAsync(otherShopper);

        var (seller, _) = await SignedInVendorOwnerAsync(admin, uninvolved.Vendor.Id);

        // Neither order is theirs, and both answer the same 404 an invented id would.
        foreach (var orderId in new[] { shared.OrderId, elsewhere.OrderId, Guid.NewGuid() })
        {
            await RefusedAsync(
                await seller.GetAsync(new Uri($"/api/v1/admin/orders/{orderId}", UriKind.Relative), Cancellation),
                HttpStatusCode.NotFound,
                "ORDER_NOT_FOUND");

            await RefusedAsync(
                await seller.GetAsync(
                    new Uri($"/api/v1/admin/orders/{orderId}/invoices", UriKind.Relative),
                    Cancellation),
                HttpStatusCode.NotFound,
                "ORDER_NOT_FOUND");
        }

        // A competitor's sub-order is not a resource that exists for them, so neither reading the
        // worklist nor addressing it directly finds it.
        var otherSubOrderId = await scenario.SubOrderIdAsync(shared.OrderId, theirs.Vendor.Id);

        await RefusedAsync(
            await seller.PostAsJsonAsync(
                $"/api/v1/admin/sub-orders/{otherSubOrderId}/transition",
                new { status = "Processing", reason = (string?)null },
                Cancellation),
            HttpStatusCode.NotFound,
            "ORDER_NOT_FOUND");

        await RefusedAsync(
            await seller.PostAsJsonAsync(
                $"/api/v1/admin/sub-orders/{otherSubOrderId}/cancel",
                new { reason = "Not mine to cancel.", lines = (object?)null },
                Cancellation),
            HttpStatusCode.NotFound,
            "ORDER_NOT_FOUND");

        var worklist = await ReadAsync(await seller.GetAsync(
            new Uri("/api/v1/admin/sub-orders?size=100", UriKind.Relative),
            Cancellation));

        Assert.DoesNotContain(
            worklist.GetProperty("items").EnumerateArray(),
            row => row.GetProperty("id").GetGuid() == otherSubOrderId);

        // The seller who does have a part in the shared order sees their half of it and only their
        // half, which is what makes the refusals above scoping rather than a broken read.
        var (participant, _) = await SignedInVendorOwnerAsync(admin, mine.Vendor.Id);

        var order = await ReadAsync(await participant.GetAsync(
            new Uri($"/api/v1/admin/orders/{shared.OrderId}", UriKind.Relative),
            Cancellation));

        var subOrder = Assert.Single(order.GetProperty("subOrders").EnumerateArray());

        Assert.Equal(mine.Vendor.Id, subOrder.GetProperty("vendorId").GetGuid());

        // A vendor id in the query string buys them nothing: the filter has already decided what
        // exists, so asking for a competitor's queue answers with their own empty page.
        var filtered = await ReadAsync(await participant.GetAsync(
            new Uri($"/api/v1/admin/sub-orders?vendorId={theirs.Vendor.Id}&size=100", UriKind.Relative),
            Cancellation));

        Assert.DoesNotContain(
            filtered.GetProperty("items").EnumerateArray(),
            row => row.GetProperty("vendorId").GetGuid() != mine.Vendor.Id);
    }

    /// <summary>
    /// An invoice download link is minted only after the ownership check, and a link for somebody
    /// else's invoice answers <c>404</c>.
    /// </summary>
    /// <remarks>
    /// The URL carries no authorisation of its own beyond an expiry, so minting one <em>is</em> the
    /// grant. A check performed by the storage layer afterwards would be a check performed after the
    /// thing being protected had already been handed over.
    /// </remarks>
    [Fact]
    public async Task An_invoice_link_is_minted_for_its_owner_and_refused_to_everybody_else()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new OrderScenario(admin, Cancellation);

        var taxonomy = await scenario.TaxonomyAsync();
        var seller = await scenario.SellerAsync(taxonomy);
        var stranger = await scenario.SellerAsync(taxonomy);

        var (client, _) = await SignedInShopperAsync();
        var shopper = await scenario.ShopperAsync(client);

        await scenario.AddToCartAsync(shopper, seller.ListingId);

        var placed = await scenario.PlaceAsync(shopper);
        var subOrderId = await scenario.SubOrderIdAsync(placed.OrderId, seller.Vendor.Id);

        // Packing raises the invoice, which is the only way one comes into existence unasked.
        await scenario.DriveAsync(subOrderId, "Processing", "Packed");

        var invoices = await ReadAsync(await shopper.Client.GetAsync(
            new Uri($"/api/v1/store/orders/{placed.OrderId}/invoices", UriKind.Relative),
            Cancellation));

        var invoice = Assert.Single(invoices.EnumerateArray());
        var invoiceId = invoice.GetProperty("id").GetGuid();

        // The shopper it belongs to gets a link.
        var link = await ReadAsync(await shopper.Client.GetAsync(
            new Uri($"/api/v1/store/invoices/{invoiceId}/download", UriKind.Relative),
            Cancellation));

        Assert.Equal(invoiceId, link.GetProperty("invoiceId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(link.GetProperty("url").GetString()));

        // Another shopper does not, and cannot tell the invoice apart from one that never existed.
        var (otherClient, _) = await SignedInShopperAsync();
        await scenario.ShopperAsync(otherClient);

        await RefusedAsync(
            await otherClient.GetAsync(
                new Uri($"/api/v1/store/invoices/{invoiceId}/download", UriKind.Relative),
                Cancellation),
            HttpStatusCode.NotFound,
            "ORDER_NOT_FOUND");

        await RefusedAsync(
            await otherClient.GetAsync(
                new Uri($"/api/v1/store/invoices/{Guid.NewGuid()}/download", UriKind.Relative),
                Cancellation),
            HttpStatusCode.NotFound,
            "ORDER_NOT_FOUND");

        // Nor does a seller with no part in the order, whose own invoices are their statutory record
        // and whose reach stops there.
        var (competitor, _) = await SignedInVendorOwnerAsync(admin, stranger.Vendor.Id);

        await RefusedAsync(
            await competitor.GetAsync(
                new Uri($"/api/v1/admin/invoices/{invoiceId}/download", UriKind.Relative),
                Cancellation),
            HttpStatusCode.NotFound,
            "ORDER_NOT_FOUND");

        // The seller who raised it does, because it is raised under their GSTIN.
        var (supplier, _) = await SignedInVendorOwnerAsync(admin, seller.Vendor.Id);

        var theirs = await ReadAsync(await supplier.GetAsync(
            new Uri($"/api/v1/admin/invoices/{invoiceId}/download", UriKind.Relative),
            Cancellation));

        Assert.Equal(invoiceId, theirs.GetProperty("invoiceId").GetGuid());
    }

    /// <summary>
    /// Every permission the Orders endpoints declare is in the Identity catalogue, and the system
    /// roles grant exactly what the step card says they grant.
    /// </summary>
    /// <remarks>
    /// The catalogue half keeps a permission from being unreachable: one an endpoint asks for but
    /// the catalogue does not declare cannot be granted by any role, so the endpoint is closed to
    /// everybody. The bundle half is the one a role editor can silently get wrong — a seller given
    /// <c>orders.order.cancel</c> by accident can cancel a dispatched parcel.
    /// </remarks>
    [Fact]
    public async Task The_orders_permissions_are_catalogued_and_the_system_roles_grant_what_the_card_says()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        var catalogue = await ReadAsync(await admin.GetAsync(
            new Uri("/api/v1/admin/permissions", UriKind.Relative),
            Cancellation));

        var declared = catalogue.EnumerateArray()
            .SelectMany(group => group.GetProperty("permissions").EnumerateArray())
            .Select(permission => permission.GetProperty("code").GetString())
            .ToHashSet(StringComparer.Ordinal);

        foreach (var permission in OrderPermissions)
        {
            Assert.Contains(permission, declared, StringComparer.Ordinal);
        }

        var roles = await ReadAsync(await admin.GetAsync(
            new Uri("/api/v1/admin/roles", UriKind.Relative),
            Cancellation));

        // Exactly the bundles Step 14's card names: support reads, operations does everything,
        // finance reads and invoices, a seller's owner does everything within their own scope, and
        // their staff pick and pack without being able to cancel or invoice.
        AssertGrants(roles, "support", "orders.order.read");
        AssertGrants(
            roles,
            "operations",
            "orders.order.read",
            "orders.order.transition",
            "orders.order.cancel",
            "orders.invoice.manage");
        AssertGrants(roles, "finance", "orders.order.read", "orders.invoice.manage");
        AssertGrants(
            roles,
            "vendor-owner",
            "orders.order.read",
            "orders.order.transition",
            "orders.order.cancel",
            "orders.invoice.manage");
        AssertGrants(roles, "vendor-staff", "orders.order.read", "orders.order.transition");

        // A shopper's role carries none of them. Their authority over their own order comes from the
        // token that names them, and a permission here would have to be given to every customer.
        AssertGrants(roles, "customer");
    }

    /// <summary>Asserts one role's Orders permissions are exactly the ones named.</summary>
    /// <param name="roles">The role listing.</param>
    /// <param name="code">The role's stable code.</param>
    /// <param name="expected">Every <c>orders.*</c> permission it should carry, and no others.</param>
    private static void AssertGrants(System.Text.Json.JsonElement roles, string code, params string[] expected)
    {
        var role = Assert.Single(
            roles.EnumerateArray(),
            candidate => candidate.GetProperty("code").GetString() == code);

        var granted = role.GetProperty("permissions").EnumerateArray()
            .Select(permission => permission.GetString()!)
            .Where(permission => permission.StartsWith("orders.", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(expected.ToHashSet(StringComparer.Ordinal), granted);
    }
}
