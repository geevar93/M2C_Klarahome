using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.Infrastructure.Authorization;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Identity.Infrastructure.Seeding;
using KlaraHome.Modules.Inventory.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Who may see and move what: a seller is confined to their own shelves, locations, suppliers and
/// paperwork, and the one rule a query filter cannot express — that they may not write into a
/// platform location they can perfectly well see — is enforced beside it.
/// </summary>
/// <param name="fixture">The migrated database.</param>
public sealed class InventoryAuthorisationTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// A seller sees their own stock, locations, suppliers and documents, and nobody else's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Six tables and four surfaces, because the filter is applied per entity type and a table that
    /// forgot to declare itself vendor-scoped would leak silently — the list would simply have more
    /// rows in it than the caller was entitled to, and nothing about the response would say so.
    /// </para>
    /// <para>
    /// Reading somebody else's row by id answers 404 rather than 403. That is the existence
    /// disclosure <c>docs/07-security-compliance.md</c> §2 rules out: a competitor must not be able
    /// to learn that a warehouse code or a stock row exists by the shape of the refusal.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_vendor_caller_sees_only_their_own_stock_locations_suppliers_and_documents()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var inventory = new InventoryScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();

        var mine = await sellers.ActiveAsync();
        var theirs = await sellers.ActiveAsync();

        var myWarehouse = await inventory.WarehouseAsync(mine.Id);
        var theirWarehouse = await inventory.WarehouseAsync(theirs.Id);

        var myStock = await inventory.StockedAsync(catalogue, taxonomy, mine.Id, 5, myWarehouse);
        var theirStock = await inventory.StockedAsync(catalogue, taxonomy, theirs.Id, 5, theirWarehouse);

        var mySupplier = await inventory.SupplierAsync(mine.Id);
        var theirSupplier = await inventory.SupplierAsync(theirs.Id);

        var (myOrder, _) = await inventory.PurchaseOrderAsync(mySupplier, myWarehouse, [(myStock.ListingId, 5)]);
        var (theirOrder, _) = await inventory.PurchaseOrderAsync(
            theirSupplier,
            theirWarehouse,
            [(theirStock.ListingId, 5)]);

        var myTake = await ReadAsync(await inventory.StockTakeAsync(myWarehouse, [myStock.StockItemId]));
        var theirTake = await ReadAsync(await inventory.StockTakeAsync(theirWarehouse, [theirStock.StockItemId]));

        var seller = await SignedInStockKeeperAsync(admin, mine.Id);

        await AssertScopedAsync(seller, "/api/v1/admin/warehouses", myWarehouse, theirWarehouse);
        await AssertScopedAsync(seller, "/api/v1/admin/stock", myStock.StockItemId, theirStock.StockItemId);
        await AssertScopedAsync(seller, "/api/v1/admin/suppliers", mySupplier, theirSupplier);
        await AssertScopedAsync(seller, "/api/v1/admin/purchase-orders", myOrder, theirOrder);

        await AssertScopedAsync(
            seller,
            "/api/v1/admin/stock-takes",
            myTake.GetProperty("id").GetGuid(),
            theirTake.GetProperty("id").GetGuid());

        // The ledger and the holds hang off a stock row, so the right to read them is the right to
        // read the row — and a competitor's row is not readable, so neither are its movements.
        await RefusedAsync(
            await seller.GetAsync(
                new Uri($"/api/v1/admin/stock/{theirStock.StockItemId}/ledger", UriKind.Relative),
                Cancellation),
            HttpStatusCode.NotFound,
            "INVENTORY_NOT_FOUND");

        // Naming somebody else's seller in a filter does not widen anything: the filter narrows what
        // the caller can already see rather than choosing what that is.
        var forced = await ReadAsync(await seller.GetAsync(
            new Uri($"/api/v1/admin/stock?vendorId={theirs.Id}&size=100", UriKind.Relative),
            Cancellation));

        Assert.Empty(forced.GetProperty("items").EnumerateArray());

        // And a movement against a competitor's shelf is refused as an unknown row, not as a
        // forbidden one.
        await RefusedAsync(
            await inventory.AdjustAsync(theirStock.StockItemId, -1, client: seller),
            HttpStatusCode.NotFound,
            "INVENTORY_NOT_FOUND");
    }

    /// <summary>
    /// A seller may read a platform location and the stock the platform holds for them in it, and
    /// may not write into the location itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two halves are the whole reason <c>InventoryScope.CanWrite</c> exists beside the query
    /// filter. A platform warehouse is shared on purpose — it is where fulfilment-by-platform stock
    /// sits, and a seller who could not see it could not see their own units — but opening stock in
    /// it, renaming it, closing it or counting it are the platform's decisions.
    /// </para>
    /// <para>
    /// The stock row itself is the seller's, because a stock row's owner is the listing's seller and
    /// not the shelf's: the platform holding a seller's goods on their behalf does not make the goods
    /// the platform's. So the seller reads it, and moves it, in a location they may not touch.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_vendor_caller_reads_a_platform_location_but_may_not_write_into_it()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var inventory = new InventoryScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var mine = await sellers.ActiveAsync();

        var platformWarehouse = await inventory.WarehouseAsync(vendorId: null, priority: 3);
        var platformSupplier = await inventory.SupplierAsync(vendorId: null);
        var myWarehouse = await inventory.WarehouseAsync(mine.Id);

        // Fulfilment by platform: the seller's offer, stocked on the platform's shelf by staff.
        var offer = await inventory.StockedAsync(catalogue, taxonomy, mine.Id, 12, platformWarehouse);

        var seller = await SignedInStockKeeperAsync(admin, mine.Id);

        // They can see the location. The endpoint says so, and the whole arrangement depends on it.
        var location = await ReadAsync(await seller.GetAsync(
            new Uri($"/api/v1/admin/warehouses/{platformWarehouse}", UriKind.Relative),
            Cancellation));

        Assert.Equal(JsonValueKind.Null, location.GetProperty("vendorId").ValueKind);

        // And they can see their own units on it, on the list as well as by id. The stock list joins
        // to the location, so a location they could not see would have taken the row with it.
        var listed = await ReadAsync(await seller.GetAsync(
            new Uri("/api/v1/admin/stock?size=100", UriKind.Relative),
            Cancellation));

        Assert.Contains(
            listed.GetProperty("items").EnumerateArray(),
            row => row.GetProperty("id").GetGuid() == offer.StockItemId);

        var row = await inventory.ReadStockAsync(offer.StockItemId, seller);

        Assert.Equal(12, row.GetProperty("quantityOnHand").GetInt32());
        Assert.Equal(platformWarehouse, row.GetProperty("warehouseId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("warehouseCode").GetString()));

        // Their goods are still their goods, so they may write them off.
        await ReadAsync(await inventory.AdjustAsync(offer.StockItemId, -2, "Damage", "Torn.", seller));

        // What they may not do is anything to the shelf. It is a permission refusal about an
        // operation rather than a scope refusal about a resource — the resource is one they can
        // already see, so 404 would be a lie and 403 is what it is.
        await RefusedAsync(
            await inventory.OpenStockAsync(offer.ListingId, platformWarehouse, seller),
            HttpStatusCode.Forbidden,
            "INVENTORY_PLATFORM_ONLY");

        await RefusedAsync(
            await inventory.UpdateWarehouseAsync(platformWarehouse, priority: 0, client: seller),
            HttpStatusCode.Forbidden,
            "INVENTORY_PLATFORM_ONLY");

        await RefusedAsync(
            await seller.DeleteAsync(
                new Uri($"/api/v1/admin/warehouses/{platformWarehouse}", UriKind.Relative),
                Cancellation),
            HttpStatusCode.Forbidden,
            "INVENTORY_PLATFORM_ONLY");

        await RefusedAsync(
            await inventory.StockTakeAsync(platformWarehouse, client: seller),
            HttpStatusCode.Forbidden,
            "INVENTORY_PLATFORM_ONLY");

        // Buying into a platform location is the same refusal, reached through a different document.
        var mySupplier = await inventory.SupplierAsync(mine.Id, seller);

        await RefusedAsync(
            await seller.PostAsJsonAsync(
                "/api/v1/admin/purchase-orders",
                new
                {
                    supplierId = mySupplier,
                    warehouseId = platformWarehouse,
                    expectedAt = (DateTimeOffset?)null,
                    notes = (string?)null,
                    lines = new[]
                    {
                        new
                        {
                            listingId = offer.ListingId,
                            description = "Cushion covers",
                            quantityOrdered = 5,
                            unitCost = 400m,
                            taxRate = 5m,
                        },
                    },
                },
                Cancellation),
            HttpStatusCode.Forbidden,
            "INVENTORY_PLATFORM_ONLY");

        // A supplier is a different matter, and deliberately so: a seller's supplier list and the
        // platform's are private to each of them, so the platform's is not visible at all and the
        // refusal is the ordinary 404 rather than the permission one.
        await RefusedAsync(
            await seller.PutAsJsonAsync(
                $"/api/v1/admin/suppliers/{platformSupplier}",
                new
                {
                    name = "Renamed by a seller",
                    contactName = (string?)null,
                    email = (string?)null,
                    phone = (string?)null,
                    gstin = (string?)null,
                    address = (object?)null,
                    paymentTermsDays = 15,
                    isActive = true,
                },
                Cancellation),
            HttpStatusCode.NotFound,
            "INVENTORY_NOT_FOUND");

        // The same operations against their own location are allowed, so what was refused above was
        // the ownership and not the permission.
        await ReadAsync(await inventory.UpdateWarehouseAsync(myWarehouse, priority: 2, client: seller));
        await ReadAsync(await inventory.StockTakeAsync(myWarehouse, client: seller));
    }

    /// <summary>
    /// Every permission the Inventory endpoints declare is one the module defines and one the
    /// catalogue grants — read off the routes rather than off a list somebody kept in step by hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A module may not reference another, so <c>InventoryPermissions</c> and the Identity catalogue
    /// are two lists of the same facts and nothing but a test keeps them together. The unit test that
    /// did this asserted five hard-coded strings, which cannot notice a sixth endpoint declaring a
    /// permission nobody added to either list — and the failure that produces is a route no role can
    /// ever be granted, which looks exactly like a 403 somebody deserved.
    /// </para>
    /// <para>
    /// Both directions are asserted. Every permission an inventory route asks for is defined and
    /// granted; and every constant the module defines is actually asked for by a route, so a
    /// permission that outlived the endpoint it protected does not sit in the catalogue pretending to
    /// mean something.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_inventory_endpoint_permission_is_defined_by_the_module_and_known_to_the_catalogue()
    {
        var declared = typeof(InventoryPermissions)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(declared);

        var required = Factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<RequiredPermissionMetadata>()
                is { Permission: not null } metadata
                && metadata.Permission.StartsWith("inventory.", StringComparison.Ordinal))
            .Select(endpoint => endpoint.Metadata.GetMetadata<RequiredPermissionMetadata>()!.Permission)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(required);

        // Nothing an inventory route asks for is missing from the module's own list…
        Assert.Empty(required.Except(declared, StringComparer.Ordinal));

        // …nothing the module defines has outlived the routes that used it…
        Assert.Empty(declared.Except(required, StringComparer.Ordinal));

        // …and every one of them can actually be granted to somebody.
        var catalogue = PermissionCatalog.All.Select(permission => permission.Code)
            .ToHashSet(StringComparer.Ordinal);

        var ungrantable = required.Where(permission => !catalogue.Contains(permission)).ToList();

        Assert.True(
            ungrantable.Count == 0,
            "These permissions are required by an Inventory endpoint but are not in the Identity "
            + $"catalogue, so no role can ever grant them: {string.Join(", ", ungrantable)}");
    }

    /// <summary>
    /// Asserts that a caller sees their own row on a listing and by id, and neither for another
    /// seller's.
    /// </summary>
    /// <param name="caller">The vendor client.</param>
    /// <param name="path">The listing route.</param>
    /// <param name="mine">A row the caller owns.</param>
    /// <param name="theirs">A row another seller owns.</param>
    private static async Task AssertScopedAsync(HttpClient caller, string path, Guid mine, Guid theirs)
    {
        var page = await ReadAsync(await caller.GetAsync(new Uri($"{path}?size=100", UriKind.Relative), Cancellation));

        var ids = page.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();

        Assert.Contains(mine, ids);
        Assert.DoesNotContain(theirs, ids);

        await ReadAsync(await caller.GetAsync(new Uri($"{path}/{mine}", UriKind.Relative), Cancellation));

        await RefusedAsync(
            await caller.GetAsync(new Uri($"{path}/{theirs}", UriKind.Relative), Cancellation),
            HttpStatusCode.NotFound,
            "INVENTORY_NOT_FOUND");
    }

    /// <summary>
    /// Signs in as a seller's stock keeper: a vendor-scoped account holding every Inventory
    /// permission.
    /// </summary>
    /// <remarks>
    /// The role is minted through the role-management API rather than taken from the seeded set,
    /// because what is under test here is the scope machinery, not which bundle a deployment
    /// happens to hand out: the account is given exactly the surface the module defines, and it
    /// keeps passing whoever a later deployment decides should hold it. <c>vendor-owner</c> does
    /// now grant all five (2026-09-09, the User's decision — it previously granted none of them,
    /// which left an owner with fewer rights over their own stock than their own staff);
    /// <c>vendor-staff</c> still holds stock and stock takes but not locations or purchasing,
    /// because opening a warehouse and committing the seller's money to a supplier are the owner's.
    /// </remarks>
    /// <param name="admin">A client signed in as platform staff.</param>
    /// <param name="vendorId">The seller the account belongs to.</param>
    private async Task<HttpClient> SignedInStockKeeperAsync(HttpClient admin, Guid vendorId)
    {
        var roleCode = $"stock-keeper-{Guid.NewGuid():N}"[..24];

        await ReadAsync(await admin.PostAsJsonAsync(
            "/api/v1/admin/roles",
            new
            {
                code = roleCode,
                name = "Stock keeper",
                scope = "Vendor",
                description = "Runs this seller's warehouse.",
                permissions = new[]
                {
                    InventoryPermissions.StockRead,
                    InventoryPermissions.StockAdjust,
                    InventoryPermissions.WarehouseManage,
                    InventoryPermissions.PurchasingManage,
                    InventoryPermissions.StockTakeManage,
                },
            },
            Cancellation));

        var email = NewEmail("keeper");
        const string Password = "the-stock-keeper-signs-in-here";

        await ReadAsync(await admin.PostAsJsonAsync(
            "/api/v1/admin/users",
            new
            {
                email,
                mobile = (string?)null,
                userType = "Vendor",
                roleCodes = new[] { roleCode },
                vendorId,
            },
            Cancellation));

        await SetPasswordAsync(email, Password);

        var client = CreateClient();
        await TestSignIn.SignInAsync(client, "admin", email, Password, Cancellation);

        return client;
    }
}
