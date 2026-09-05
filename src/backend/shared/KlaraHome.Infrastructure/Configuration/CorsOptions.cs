namespace KlaraHome.Infrastructure.Configuration;

/// <summary>
/// Browser origins allowed to call the API. There is no wildcard path: the storefront and admin
/// origins are listed explicitly per environment (docs/07-security-compliance.md §3).
/// </summary>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    /// <summary>The named policy applied to the whole API.</summary>
    public const string PolicyName = "KlaraHomeDefault";

    /// <summary>Exact origins, scheme included, no trailing slash.</summary>
    public IList<string> AllowedOrigins { get; } = [];

    /// <summary>Response headers the browser is allowed to read.</summary>
    public IList<string> ExposedHeaders { get; } =
    [
        "X-Correlation-Id",
        "X-RateLimit-Limit",
        "X-RateLimit-Remaining",
        "X-RateLimit-Reset",
        "Retry-After",
    ];

    /// <summary>Required for the refresh-token cookie to travel.</summary>
    public bool AllowCredentials { get; set; } = true;

    /// <summary>Preflight cache lifetime, in seconds.</summary>
    public int PreflightMaxAgeSeconds { get; set; } = 600;
}
