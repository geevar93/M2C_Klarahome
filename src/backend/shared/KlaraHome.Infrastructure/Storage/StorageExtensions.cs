using KlaraHome.Infrastructure.Health;
using KlaraHome.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KlaraHome.Infrastructure.Storage;

/// <summary>Registers object storage for a host that has it.</summary>
public static class StorageExtensions
{
    /// <summary>Name of the readiness check reported for object storage.</summary>
    public const string HealthCheckName = "storage";

    /// <summary>
    /// Registers <see cref="IFileStorage"/> and, where credentials exist, a readiness check that
    /// proves the bucket is reachable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The abstraction is registered unconditionally so a module can depend on it without asking
    /// whether the host is configured; the check is registered only when there is something to
    /// check, which is the rule <c>AddKlaraHomeHealthChecks</c> already follows for the database.
    /// </para>
    /// <para>
    /// Every module that stores a file calls this, and a host has more than one of them. Repeating
    /// a service registration is merely wasteful, but repeating a health check is fatal: the health
    /// service refuses two registrations under one name and the host fails to start. So the whole
    /// method runs once per container, and the marker below is what remembers that it has.
    /// </para>
    /// </remarks>
    /// <param name="services">The container.</param>
    /// <param name="configuration">Root configuration.</param>
    public static IServiceCollection AddKlaraHomeStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(StorageRegistrationMarker)))
        {
            return services;
        }

        services.AddSingleton<StorageRegistrationMarker>();

        services.AddValidatedOptions<StorageOptions>(configuration, StorageOptions.SectionName);
        services.AddSingleton<IFileStorage, S3FileStorage>();

        var options = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>();

        if (options?.IsConfigured == true)
        {
            services
                .AddHealthChecks()
                .AddCheck<StorageHealthCheck>(
                    HealthCheckName,
                    // Degraded rather than Unhealthy: the storefront still browses, checks out and
                    // takes payment with object storage down. Images do not load and uploads fail,
                    // which is bad and is not "stop routing traffic to this container".
                    failureStatus: HealthStatus.Degraded,
                    tags: [HealthCheckExtensions.ReadyTag]);
        }

        return services;
    }

    /// <summary>Present in the container once storage has been registered. Holds no state.</summary>
    private sealed class StorageRegistrationMarker;
}
