using System.ComponentModel.DataAnnotations;

namespace KlaraHome.Infrastructure.Configuration;

/// <summary>
/// The buckets from docs/04-api-specification.md §6, expressed as configuration rather than code
/// so they can be tuned per tenant without a redeploy. Endpoints opt in by naming a policy; the
/// global limiter is the backstop that protects the process itself.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Disabled only in tests. Never disabled in a deployed environment.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Backstop applied to every request, partitioned by client IP.</summary>
    public RateLimitBucket Global { get; set; } = new() { PermitLimit = 600, WindowSeconds = 60 };

    /// <summary>Anonymous storefront reads.</summary>
    public RateLimitBucket StorefrontRead { get; set; } = new() { PermitLimit = 300, WindowSeconds = 60 };

    /// <summary>Login and refresh, per IP.</summary>
    public RateLimitBucket Auth { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    /// <summary>OTP requests per IP. The per-mobile limit is enforced inside the Identity module.</summary>
    public RateLimitBucket Otp { get; set; } = new() { PermitLimit = 20, WindowSeconds = 3600 };

    /// <summary>Cart mutations, per session.</summary>
    public RateLimitBucket CartWrite { get; set; } = new() { PermitLimit = 60, WindowSeconds = 60 };

    /// <summary>Order placement, per customer.</summary>
    public RateLimitBucket PlaceOrder { get; set; } = new() { PermitLimit = 5, WindowSeconds = 60 };

    /// <summary>Admin and vendor writes, per user.</summary>
    public RateLimitBucket AdminWrite { get; set; } = new() { PermitLimit = 120, WindowSeconds = 60 };

    /// <summary>Every bucket, for validation. Add a bucket above and it is validated here too.</summary>
    public IEnumerable<RateLimitBucket> AllBuckets
        => [Global, StorefrontRead, Auth, Otp, CartWrite, PlaceOrder, AdminWrite];
}

/// <summary>A fixed-window allowance.</summary>
public sealed class RateLimitBucket
{
    /// <summary>Requests permitted inside one window.</summary>
    [Range(1, 1_000_000)]
    public int PermitLimit { get; set; } = 60;

    /// <summary>Window length in seconds.</summary>
    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Requests queued rather than rejected once the window is full.</summary>
    [Range(0, 10_000)]
    public int QueueLimit { get; set; }

    public TimeSpan Window => TimeSpan.FromSeconds(WindowSeconds);
}
