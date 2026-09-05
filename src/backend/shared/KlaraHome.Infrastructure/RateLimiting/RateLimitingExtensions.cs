using System.Globalization;
using System.Threading.RateLimiting;
using KlaraHome.Infrastructure.Configuration;
using KlaraHome.Infrastructure.Errors;
using KlaraHome.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KlaraHome.Infrastructure.RateLimiting;

/// <summary>Edge rate limiting, backing the buckets in docs/04-api-specification.md §6.</summary>
/// <remarks>
/// Traefik also rate-limits at the edge; this layer exists because the per-customer and
/// per-session buckets need application identity that the proxy does not have.
/// <para>
/// Limits are read from <see cref="RateLimitingOptions"/> through the options pipeline rather
/// than captured at registration, so configuration from any provider — environment, secrets,
/// a test host — reaches the limiter.
/// </para>
/// </remarks>
public static class RateLimitingExtensions
{
    private const string LimitHeader = "X-RateLimit-Limit";
    private const string RemainingHeader = "X-RateLimit-Remaining";
    private const string ResetHeader = "X-RateLimit-Reset";
    private const string PolicyItemKey = "__klarahome.ratelimit.policy";

    public static IServiceCollection AddKlaraHomeRateLimiting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                context => Fixed(ClientKey(context), Options(context).Global));

            AddPolicy(limiter, RateLimitPolicies.StorefrontRead, options => options.StorefrontRead, ClientKey);
            AddPolicy(limiter, RateLimitPolicies.Auth, options => options.Auth, ClientKey);
            AddPolicy(limiter, RateLimitPolicies.Otp, options => options.Otp, ClientKey);
            AddPolicy(limiter, RateLimitPolicies.CartWrite, options => options.CartWrite, SessionKey);
            AddPolicy(limiter, RateLimitPolicies.PlaceOrder, options => options.PlaceOrder, UserKey);
            AddPolicy(limiter, RateLimitPolicies.AdminWrite, options => options.AdminWrite, UserKey);

            limiter.OnRejected = async (context, cancellationToken) =>
            {
                var bucket = BucketFor(context.HttpContext);
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var value)
                    ? value
                    : bucket.Window;

                var response = context.HttpContext.Response;
                response.Headers.RetryAfter =
                    ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                response.Headers[LimitHeader] = bucket.PermitLimit.ToString(CultureInfo.InvariantCulture);
                response.Headers[RemainingHeader] = "0";
                response.Headers[ResetHeader] = DateTimeOffset.UtcNow
                    .Add(retryAfter)
                    .ToUnixTimeSeconds()
                    .ToString(CultureInfo.InvariantCulture);

                var problem = Error
                    .RateLimited(message: "Too many requests. Please retry after a short wait.")
                    .ToProblemDetails(context.HttpContext);

                response.StatusCode = StatusCodes.Status429TooManyRequests;
                await response
                    .WriteAsJsonAsync(problem, options: null, "application/problem+json", cancellationToken)
                    .ConfigureAwait(false);
            };
        });

        return services;
    }

    private static void AddPolicy(
        RateLimiterOptions limiter,
        string policyName,
        Func<RateLimitingOptions, RateLimitBucket> selector,
        Func<HttpContext, string> keySelector)
        => limiter.AddPolicy(policyName, context =>
        {
            context.Items[PolicyItemKey] = policyName;
            return Fixed(keySelector(context), selector(Options(context)));
        });

    private static RateLimitingOptions Options(HttpContext context)
        => context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

    private static RateLimitPartition<string> Fixed(string partitionKey, RateLimitBucket bucket)
        => RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = bucket.PermitLimit,
            Window = bucket.Window,
            QueueLimit = bucket.QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });

    private static RateLimitBucket BucketFor(HttpContext context)
    {
        var options = Options(context);

        return context.Items.TryGetValue(PolicyItemKey, out var value) && value is string policy
            ? policy switch
            {
                RateLimitPolicies.StorefrontRead => options.StorefrontRead,
                RateLimitPolicies.Auth => options.Auth,
                RateLimitPolicies.Otp => options.Otp,
                RateLimitPolicies.CartWrite => options.CartWrite,
                RateLimitPolicies.PlaceOrder => options.PlaceOrder,
                RateLimitPolicies.AdminWrite => options.AdminWrite,
                _ => options.Global,
            }
            : options.Global;
    }

    /// <summary>
    /// Partition by client IP. Behind Traefik the real address arrives via forwarded headers,
    /// which the host enables for known proxies only.
    /// </summary>
    private static string ClientKey(HttpContext context)
        => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string UserKey(HttpContext context)
        => context.User.Identity?.IsAuthenticated == true
            ? "user:" + (context.User.FindFirst("sub")?.Value ?? "anonymous")
            : "ip:" + ClientKey(context);

    private static string SessionKey(HttpContext context)
        => context.Request.Headers.TryGetValue("X-Cart-Session", out var session) && !string.IsNullOrEmpty(session)
            ? "cart:" + session.ToString()
            : UserKey(context);
}
