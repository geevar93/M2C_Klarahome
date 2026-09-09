using System.Net;
using System.Net.Http.Json;
using KlaraHome.Infrastructure.Authorization;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Carts.Endpoints;
using KlaraHome.Modules.Identity.Infrastructure.Seeding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Whose basket is whose, and what the Cart endpoints are allowed to ask for.
/// </summary>
/// <remarks>
/// The module's central security claim is structural rather than procedural: no storefront route
/// takes a cart id, so there is nothing for a caller to change, and every checkout read is keyed on
/// <em>(session, customer)</em>, so an id belonging to somebody else simply does not resolve. Both
/// halves are asserted here — the first against the routes that are actually mapped, the second
/// against a second shopper who tries.
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class CartAuthorisationTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    private const string CartPrefix = "/api/v1/store/cart";
    private const string CheckoutPrefix = "/api/v1/store/checkout";

    /// <summary>
    /// No route on the storefront basket surface takes a parameter of any kind, so there is nothing
    /// on it a caller could point at somebody else's basket.
    /// </summary>
    /// <remarks>
    /// Asserted against the mapped routes rather than against a document. The authorisation for a
    /// basket lives in the shape of the lookup — token or cookie, never a path — and the only way to
    /// keep that true is to notice the day somebody adds <c>/store/cart/{id}</c>.
    /// </remarks>
    [Fact]
    public void No_storefront_cart_route_takes_a_cart_id()
    {
        var named = Routes(CartPrefix)
            .SelectMany(route => route.RoutePattern.Parameters.Select(
                parameter => $"{route.RoutePattern.RawText} takes '{parameter.Name}'"))
            .Where(parameter => !parameter.EndsWith("takes 'lineId'", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            named.Count == 0,
            "Nothing under /store/cart may take an id but a line's own, because an id in a path is an "
            + $"id a caller can change. These do: {string.Join(", ", named)}");

        // The one parameter that is allowed is a line inside the caller's own basket, resolved from
        // that basket rather than looked up on its own.
        Assert.Contains(Routes(CartPrefix), route => route.RoutePattern.Parameters.Count == 1);

        // The checkout surface does take an id, and it is safe there for a different reason: every
        // handler looks a session up by (session, customer), so one belonging to somebody else does
        // not resolve. The test below is what holds that.
        Assert.All(
            Routes(CheckoutPrefix).SelectMany(route => route.RoutePattern.Parameters),
            parameter => Assert.Equal("id", parameter.Name));
    }

    /// <summary>
    /// A shopper cannot read, change, abandon or place another shopper's checkout session. Every
    /// route answers the same 404 an invented id gets.
    /// </summary>
    /// <remarks>
    /// 404 rather than 403 on purpose (docs/07-security-compliance.md §2): the difference between
    /// the two is itself a disclosure — it tells the caller that the id they guessed is real.
    /// </remarks>
    [Fact]
    public async Task A_shopper_cannot_read_change_or_place_another_shoppers_checkout()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var seller = await scenario.SellerAsync();
        var offer = await scenario.OfferAsync(seller, sellingPrice: 199m);

        var (owner, _) = await SignedInShopperAsync();
        var ownerAddress = await scenario.AddressAsync(owner);

        await scenario.AddAsync(owner, offer);
        var sessionId = await scenario.ReadyCheckoutAsync(owner, ownerAddress);

        // A second shopper with an account, an address and a basket of their own.
        var (intruder, _) = await SignedInShopperAsync();
        var intruderAddress = await scenario.AddressAsync(intruder);

        await scenario.AddAsync(intruder, offer);

        await RefusedAsync(
            await intruder.GetAsync(new Uri($"{CheckoutPrefix}/{sessionId}", UriKind.Relative), Cancellation),
            HttpStatusCode.NotFound,
            "CART_NOT_FOUND");

        await RefusedAsync(
            await scenario.ReviewAsync(intruder, sessionId),
            HttpStatusCode.NotFound,
            "CART_NOT_FOUND");

        await RefusedAsync(
            await intruder.GetAsync(
                new Uri($"{CheckoutPrefix}/{sessionId}/shipping-options", UriKind.Relative),
                Cancellation),
            HttpStatusCode.NotFound,
            "CART_NOT_FOUND");

        await RefusedAsync(
            await intruder.GetAsync(
                new Uri($"{CheckoutPrefix}/{sessionId}/payment-methods", UriKind.Relative),
                Cancellation),
            HttpStatusCode.NotFound,
            "CART_NOT_FOUND");

        // Their own address on somebody else's checkout is still somebody else's checkout.
        await RefusedAsync(
            await scenario.SetAddressAsync(intruder, sessionId, intruderAddress.Id),
            HttpStatusCode.NotFound,
            "CART_NOT_FOUND");

        await RefusedAsync(
            await scenario.SetShippingAsync(intruder, sessionId, (seller.Id, "standard")),
            HttpStatusCode.NotFound,
            "CART_NOT_FOUND");

        await RefusedAsync(
            await scenario.SetPaymentMethodAsync(intruder, sessionId, "prepaid"),
            HttpStatusCode.NotFound,
            "CART_NOT_FOUND");

        await RefusedAsync(
            await intruder.PostAsJsonAsync($"{CheckoutPrefix}/{sessionId}/abandon", new { }, Cancellation),
            HttpStatusCode.NotFound,
            "CART_NOT_FOUND");

        await RefusedAsync(
            await scenario.PlaceOrderAsync(intruder, sessionId, CartScenario.NewIdempotencyKey("intruder")),
            HttpStatusCode.NotFound,
            "CART_NOT_FOUND");

        // An id nobody owns answers exactly the same, which is what makes the refusals above
        // disclose nothing.
        await RefusedAsync(
            await intruder.GetAsync(
                new Uri($"{CheckoutPrefix}/{Guid.CreateVersion7()}", UriKind.Relative),
                Cancellation),
            HttpStatusCode.NotFound,
            "CART_NOT_FOUND");

        // The owner's session is untouched by any of it, and still theirs to place.
        var placed = await ReadAsync(await scenario.PlaceOrderAsync(
            owner,
            sessionId,
            CartScenario.NewIdempotencyKey("owner")));

        Assert.False(string.IsNullOrWhiteSpace(placed.GetProperty("orderNumber").GetString()));
    }

    /// <summary>
    /// A read of the basket answers with the caller's own and never with the last caller's, whether
    /// they are signed in or not.
    /// </summary>
    /// <remarks>
    /// The other half of "no route takes a cart id": if the lookup is the authorisation, then two
    /// callers hitting the same URL must get two different baskets, and a caller with none must get
    /// an empty one rather than whatever was there before.
    /// </remarks>
    [Fact]
    public async Task Two_shoppers_reading_the_same_url_get_their_own_baskets()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var seller = await scenario.SellerAsync();
        var offer = await scenario.OfferAsync(seller, sellingPrice: 199m);

        var (first, _) = await SignedInShopperAsync();
        var (second, _) = await SignedInShopperAsync();

        var firstCart = await scenario.AddAsync(first, offer, quantity: 3);
        var secondCart = await scenario.AddAsync(second, offer, quantity: 1);

        Assert.NotEqual(firstCart.GetProperty("id").GetGuid(), secondCart.GetProperty("id").GetGuid());

        Assert.Equal(3, (await scenario.CartAsync(first)).GetProperty("lines").EnumerateArray()
            .First().GetProperty("quantity").GetInt32());

        Assert.Equal(1, (await scenario.CartAsync(second)).GetProperty("lines").EnumerateArray()
            .First().GetProperty("quantity").GetInt32());

        // A shopper who has never added anything gets an empty basket, not the last one served.
        var (third, _) = await SignedInShopperAsync();
        var empty = await scenario.CartAsync(third);

        Assert.Equal(Guid.Empty, empty.GetProperty("id").GetGuid());
        Assert.Empty(empty.GetProperty("lines").EnumerateArray());

        // And checkout requires an account at all: an anonymous caller cannot open one.
        using var browser = CreateClient();
        using var refused = await browser.PostAsJsonAsync("/api/v1/store/checkout", new { }, Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    /// <summary>
    /// Every permission the Cart endpoints declare appears in <c>PermissionCatalog</c>, and the
    /// storefront surface declares none.
    /// </summary>
    /// <remarks>
    /// The two lists are kept apart by the module boundary and were diffed by hand at the Step 13
    /// boundary. A permission an endpoint asks for and the catalogue does not declare cannot be
    /// granted by any role, so the endpoint is unreachable by everybody — and a 403 looks the same
    /// whether the grant is missing or the permission does not exist.
    /// </remarks>
    [Fact]
    public void Every_cart_permission_is_in_the_catalogue_and_the_storefront_asks_for_none()
    {
        var catalogue = PermissionCatalog.All.Select(permission => permission.Code).ToHashSet(StringComparer.Ordinal);

        var declared = Routes("/api/v1/admin/carts")
            .Concat(Routes("/api/v1/admin/checkout-sessions"))
            .Select(route => route.Metadata.GetMetadata<RequiredPermissionMetadata>())
            .Where(metadata => metadata is not null)
            .Select(metadata => metadata!.Permission)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(declared);
        Assert.All(declared, permission => Assert.Contains(permission, catalogue));

        // The two the module declares, and no third one that nothing grants.
        Assert.Equal(new[] { CartsPermissions.CartManage, CartsPermissions.CartRead }, declared);

        // Every operator route asks for one of them: an unpermissioned read of a basket would be a
        // read of what a named person is about to buy.
        Assert.All(
            Routes("/api/v1/admin/carts").Concat(Routes("/api/v1/admin/checkout-sessions")),
            route => Assert.NotNull(route.Metadata.GetMetadata<RequiredPermissionMetadata>()));

        // And nothing on the storefront asks for a permission. A grant there would be one every
        // customer on the marketplace would have to be given.
        var storefront = Routes(CartPrefix)
            .Concat(Routes(CheckoutPrefix))
            .Where(route => route.Metadata.GetMetadata<RequiredPermissionMetadata>() is not null)
            .Select(route => route.RoutePattern.RawText!)
            .ToList();

        Assert.True(storefront.Count == 0, string.Join(", ", storefront));
    }

    /// <summary>Every mapped route under a path prefix, matched on whole segments.</summary>
    private IEnumerable<RouteEndpoint> Routes(string prefix)
        => Factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText is { } route
                               && (string.Equals(route, prefix, StringComparison.OrdinalIgnoreCase)
                                   || route.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)));
}
