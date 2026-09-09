using System.Globalization;
using System.Net;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The two rate-limit policies the Cart module opts into: <c>cart-write</c> on every basket
/// mutation, and <c>place-order</c> on the one request that reserves stock and creates an order.
/// </summary>
/// <remarks>
/// <para>
/// The limiter is off in every other test host, because a fixed window is shared state and leaving
/// it on would make every test depend on what the last one did. Here it is on, with the two buckets
/// under test turned down to a handful and every other bucket turned up out of the way, so what the
/// assertions see is the policy on the route rather than an allowance somebody else spent.
/// </para>
/// <para>
/// The refusals are provoked on requests that do nothing — a coupon removal against a basket that
/// does not exist, a placement against a session that does not exist — because the limiter runs in
/// front of the endpoint and a permit is spent whatever the handler would have answered. That keeps
/// each test's partition to exactly the requests it makes.
/// </para>
/// </remarks>
public sealed class CartRateLimitTests : CommerceTestBase
{
    private const int CartWrites = 3;
    private const int Placements = 2;

    /// <param name="fixture">The migrated database.</param>
    public CartRateLimitTests(KlaraHomeSchemaFixture fixture)
        : base(fixture)
    {
        Factory.Overrides["RateLimiting:Enabled"] = "true";

        Factory.Overrides["RateLimiting:CartWrite:PermitLimit"] = Text(CartWrites);
        Factory.Overrides["RateLimiting:CartWrite:WindowSeconds"] = "60";

        Factory.Overrides["RateLimiting:PlaceOrder:PermitLimit"] = Text(Placements);
        Factory.Overrides["RateLimiting:PlaceOrder:WindowSeconds"] = "60";

        // Everything the sign-in and the fixture need, out of the way. These have their own tests.
        Factory.Overrides["RateLimiting:Global:PermitLimit"] = "100000";
        Factory.Overrides["RateLimiting:StorefrontRead:PermitLimit"] = "100000";
        Factory.Overrides["RateLimiting:AdminWrite:PermitLimit"] = "100000";
        Factory.Overrides["RateLimiting:Auth:PermitLimit"] = "100000";
        Factory.Overrides["RateLimiting:Otp:PermitLimit"] = "100000";
    }

    /// <summary>
    /// The storefront basket writes are limited under their own policy, partitioned by the caller's
    /// cart session.
    /// </summary>
    /// <remarks>
    /// Adding to a basket prices it, and pricing reads the catalogue, the price lists and every live
    /// promotion. Without a limit the add endpoint is the cheapest way to make the site slow for
    /// everybody — which is why the limit is on the writes and not on the reads beside them.
    /// </remarks>
    [Fact]
    public async Task Storefront_cart_writes_are_limited_under_the_cart_write_policy()
    {
        SkipWithoutDocker();

        using var browser = CreateClient();

        // Its own partition, so this test spends nobody else's allowance and nobody spends its.
        browser.DefaultRequestHeaders.Add("X-Cart-Session", Guid.CreateVersion7().ToString());

        for (var attempt = 1; attempt <= CartWrites; attempt++)
        {
            using var within = await RemoveCouponAsync(browser);

            Assert.True(
                within.StatusCode != HttpStatusCode.TooManyRequests,
                $"write {attempt} is inside an allowance of {CartWrites} and was refused");
        }

        using var refused = await RemoveCouponAsync(browser);

        var problem = await RefusedAsync(refused, HttpStatusCode.TooManyRequests, "RATE_LIMITED");

        Assert.Equal(429, problem.GetProperty("status").GetInt32());
        Assert.Equal(Text(CartWrites), Assert.Single(refused.Headers.GetValues("X-RateLimit-Limit")));
        Assert.Equal("0", Assert.Single(refused.Headers.GetValues("X-RateLimit-Remaining")));
        Assert.True(refused.Headers.RetryAfter!.Delta!.Value > TimeSpan.Zero);

        // A read of the same basket is a different bucket entirely, and is still served.
        using var read = await browser.GetAsync(new Uri("/api/v1/store/cart", UriKind.Relative), Cancellation);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        // And another browser's writes are unaffected: the partition is per session, not per host.
        using var other = CreateClient();
        other.DefaultRequestHeaders.Add("X-Cart-Session", Guid.CreateVersion7().ToString());

        using var elsewhere = await RemoveCouponAsync(other);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, elsewhere.StatusCode);
    }

    /// <summary>
    /// Place-order is limited far more tightly than the rest, under its own policy and partitioned by
    /// the shopper.
    /// </summary>
    /// <remarks>
    /// It is the one route on the platform that reserves stock and creates an order, and it is the
    /// one worth spending a dedicated limiter on. Partitioned by the customer rather than by address,
    /// because two shoppers behind one office NAT must not be able to exhaust each other's.
    /// </remarks>
    [Fact]
    public async Task Place_order_is_limited_under_its_own_policy_and_per_shopper()
    {
        SkipWithoutDocker();

        var (shopper, _) = await SignedInShopperAsync();
        var invented = Guid.CreateVersion7();

        for (var attempt = 1; attempt <= Placements; attempt++)
        {
            using var within = await PlaceAsync(shopper, invented);

            Assert.True(
                within.StatusCode != HttpStatusCode.TooManyRequests,
                $"attempt {attempt} is inside an allowance of {Placements} and was refused");
        }

        using var refused = await PlaceAsync(shopper, invented);

        var problem = await RefusedAsync(refused, HttpStatusCode.TooManyRequests, "RATE_LIMITED");

        Assert.Equal(Text(Placements), Assert.Single(refused.Headers.GetValues("X-RateLimit-Limit")));
        Assert.Equal(429, problem.GetProperty("status").GetInt32());

        // A second shopper has their own allowance and is unaffected by the first one's.
        var (other, _) = await SignedInShopperAsync();

        using var elsewhere = await PlaceAsync(other, invented);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, elsewhere.StatusCode);

        // And the shopper who ran out can still read their basket: the limit is on placing, not on
        // shopping.
        using var read = await shopper.GetAsync(new Uri("/api/v1/store/cart", UriKind.Relative), Cancellation);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    /// <summary>One <c>cart-write</c> request that changes nothing.</summary>
    private static Task<HttpResponseMessage> RemoveCouponAsync(HttpClient client)
        => client.DeleteAsync(new Uri("/api/v1/store/cart/coupon", UriKind.Relative), Cancellation);

    /// <summary>One <c>place-order</c> request against a session nobody has.</summary>
    private static Task<HttpResponseMessage> PlaceAsync(HttpClient shopper, Guid sessionId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/api/v1/store/checkout/{sessionId}/place-order", UriKind.Relative));

        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString());

        return shopper.SendAsync(request, Cancellation);
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
