using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.Routing;
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

    /// <summary>Caches a public read with the long-TTL reference-data policy.</summary>
    /// <remarks>
    /// A named helper rather than <c>CacheOutput("reference-data")</c> at the call site, so an
    /// endpoint opts into caching by saying what it is. It also keeps
    /// <c>Microsoft.AspNetCore.OutputCaching</c> a dependency of this project alone: it is not on a
    /// module project's compile reference set, even with an explicit <c>FrameworkReference</c>.
    /// Its short-TTL sibling is <see cref="CachePublicRead{TBuilder}"/>.
    /// </remarks>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint being built.</param>
    public static TBuilder CacheReferenceData<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
        => builder.CacheOutput(OutputCachePolicies.ReferenceData);

    /// <summary>Caches a public read with the short-TTL, query-varying policy.</summary>
    /// <remarks>
    /// Added by Step 10, the first module with a catalogue read that is genuinely public but
    /// changes often enough that fifteen minutes would be too long — a brand list a merchandiser
    /// has just edited, say. It varies by the whole query string, so a paged or filtered read is
    /// cached per distinct request rather than served from the first caller's page.
    /// </remarks>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint being built.</param>
    public static TBuilder CachePublicRead<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
        => builder.CacheOutput(OutputCachePolicies.PublicRead);
}
