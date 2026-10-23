using KlaraHome.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace KlaraHome.Infrastructure.Health;

/// <summary>
/// Liveness and readiness probes. Liveness answers "is this process worth keeping"; readiness
/// answers "can it serve traffic right now", which means its dependencies must answer too
/// (docs/06-infrastructure-devops.md §4).
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>Checks tagged this way answer <c>/health/live</c>.</summary>
    public const string LiveTag = "live";

    /// <summary>Checks tagged this way answer <c>/health/ready</c>.</summary>
    public const string ReadyTag = "ready";

    public static IServiceCollection AddKlaraHomeHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var builder = services
            .AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy("The process is running."), tags: [LiveTag, ReadyTag]);

        var postgres = configuration.GetConnectionString("Postgres");
        if (!string.IsNullOrWhiteSpace(postgres))
        {
            builder.AddNpgSql(
                postgres,
                name: "postgres",
                failureStatus: HealthStatus.Unhealthy,
                tags: [ReadyTag],
                timeout: TimeSpan.FromSeconds(3));
        }

        var redis = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redis))
        {
            // Built from the connection string with the settings a restricted ACL user needs, so
            // that the probe on a shared Redis server issues only commands that user may run
            // (RedisConnection.CreateOptions). One multiplexer for the process, connected on the
            // first readiness check and disposed with the container.
            var prefix = configuration[$"{RedisOptions.SectionName}:{nameof(RedisOptions.KeyPrefix)}"];
            var redisOptions = RedisConnection.CreateOptions(
                redis,
                string.IsNullOrWhiteSpace(prefix) ? RedisOptions.DefaultKeyPrefix : prefix);

            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));

            builder.AddRedis(
                provider => provider.GetRequiredService<IConnectionMultiplexer>(),
                name: "redis",
                failureStatus: HealthStatus.Degraded,
                tags: [ReadyTag],
                timeout: TimeSpan.FromSeconds(3));
        }

        return services;
    }
}
