namespace KlaraHome.Infrastructure.RateLimiting;

/// <summary>
/// Names of the rate-limit policies an endpoint can opt into with
/// <c>.RequireRateLimiting(RateLimitPolicies.PlaceOrder)</c>. The limits themselves are
/// configuration (docs/04-api-specification.md §6).
/// </summary>
public static class RateLimitPolicies
{
    public const string StorefrontRead = "storefront-read";
    public const string Auth = "auth";
    public const string Otp = "otp";
    public const string CartWrite = "cart-write";
    public const string PlaceOrder = "place-order";
    public const string AdminWrite = "admin-write";
}
