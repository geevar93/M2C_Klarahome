using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Infrastructure.Caching;

/// <summary>
/// Output caching for public reads. The store is in-memory for now; it moves to Redis with the
/// distributed HybridCache work, at which point only this file changes.
/// </summary>
public static class OutputCacheExtensions
{
    public static IServiceCollection AddKlaraHomeOutputCache(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOutputCache(options =>
        {
            // Nothing is cached unless an endpoint opts in by naming a policy.
            options.AddBasePolicy(policy => policy.NoCache(), excludeDefaultPolicy: true);

            options.AddPolicy(OutputCachePolicies.PublicRead, policy => policy
                .Expire(TimeSpan.FromSeconds(60))
                .SetVaryByQuery("*")
                .SetVaryByHeader("Accept-Language")
                .Cache());

            options.AddPolicy(OutputCachePolicies.ReferenceData, policy => policy
                .Expire(TimeSpan.FromMinutes(15))
                .SetVaryByHeader("Accept-Language")
                .Cache());
        });

        return services;
    }
}
