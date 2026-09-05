using KlaraHome.Contracts.Platform;
using KlaraHome.IntegrationTests.Api;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Identity.Infrastructure.Access;
using KlaraHome.Modules.Identity.Infrastructure.External;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KlaraHome.IntegrationTests.Identity;

/// <summary>
/// The real API host pointed at the migrated test database, with the two things that would
/// otherwise need a paid account or the internet replaced.
/// </summary>
/// <remarks>
/// Only the network boundary is substituted — the OTP dispatcher and the identity provider.
/// Everything that decides whether a code, a callback or a link is accepted is the implementation
/// that runs in production.
/// </remarks>
/// <param name="connectionString">The migrated database from the collection fixture.</param>
public sealed class IdentityApiFactory(string connectionString) : KlaraHomeApiFactory
{
    /// <summary>The codes and links this host has sent.</summary>
    internal CapturingOtpDispatcher Otp { get; } = new();

    /// <summary>Stands in for Google at the network boundary.</summary>
    internal FakeIdentityProvider Provider { get; } = new();

    /// <summary>
    /// Feature flags this host overrides, by key. Empty means the seeded defaults.
    /// </summary>
    /// <remarks>
    /// Flags are rows in another module's schema, shared by every test in the collection. Turning
    /// one off through the admin API would make each test depend on what the last one left behind,
    /// so this overlays the reader instead and leaves the real evaluator in place for every key it
    /// does not name.
    /// </remarks>
    internal Dictionary<string, bool> Features { get; } = new(StringComparer.Ordinal);

    protected override IDictionary<string, string?> Settings
    {
        get
        {
            var settings = base.Settings;

            foreach (var (key, value) in KlaraHomeSchemaFixture.SharedSettings(connectionString))
            {
                settings[key] = value;
            }

            // A configured provider, so the external endpoints are reachable. The adapter behind
            // it is the fake, so no client id ever leaves this process.
            settings["Auth:External:Google:Enabled"] = "true";
            settings["Auth:External:Google:ClientId"] = "test-client-id";
            settings["Auth:External:Google:ClientSecret"] = "test-client-secret";
            settings["Auth:External:CallbackBaseUrl"] = "http://localhost";
            settings["Auth:External:AllowedReturnUrls:0"] = "https://shop.example.test";
            settings["Auth:External:DefaultReturnUrl"] = "https://shop.example.test/";

            return settings;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IOtpDispatcher>(Otp));

            // One provider, not the two the module registers: these tests drive Google's path, and
            // assert separately that an unknown provider is refused.
            services.RemoveAll<IExternalIdentityProvider>();
            services.AddSingleton<IExternalIdentityProvider>(Provider);

            OverrideFeatureFlags(services, Features);
        });
    }

    /// <summary>
    /// Wraps whatever <see cref="IFeatureFlags"/> the module registered, answering from the
    /// override map first and delegating everything else.
    /// </summary>
    /// <remarks>
    /// The registration is replaced rather than added, and the original is rebuilt from its own
    /// descriptor — the Platform module registers it as a factory, so there is no implementation
    /// type to resolve by name. All three descriptor shapes are handled because which one a module
    /// happens to use is not something a test host should depend on.
    /// </remarks>
    private static void OverrideFeatureFlags(IServiceCollection services, IReadOnlyDictionary<string, bool> overrides)
    {
        var registered = services.LastOrDefault(service => service.ServiceType == typeof(IFeatureFlags))
                         ?? throw new InvalidOperationException("No IFeatureFlags is registered.");

        services.Remove(registered);

        services.Add(ServiceDescriptor.Describe(
            typeof(IFeatureFlags),
            provider => new OverriddenFeatureFlags(Original(provider, registered), overrides),
            registered.Lifetime));
    }

    private static IFeatureFlags Original(IServiceProvider provider, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is IFeatureFlags instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return (IFeatureFlags)descriptor.ImplementationFactory(provider);
        }

        return (IFeatureFlags)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);
    }
}

/// <summary>A flag reader that answers from a map first, and from the real evaluator otherwise.</summary>
/// <param name="inner">The real evaluator.</param>
/// <param name="overrides">The keys this host pins, and what to.</param>
internal sealed class OverriddenFeatureFlags(IFeatureFlags inner, IReadOnlyDictionary<string, bool> overrides)
    : IFeatureFlags
{
    /// <inheritdoc />
    public ValueTask<bool> IsEnabledAsync(
        string key,
        FeatureAudience audience = default,
        CancellationToken cancellationToken = default)
        => overrides.TryGetValue(key, out var pinned)
            ? ValueTask.FromResult(pinned)
            : inner.IsEnabledAsync(key, audience, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<string, bool>> GetAllAsync(
        FeatureAudience audience = default,
        CancellationToken cancellationToken = default)
    {
        var all = await inner.GetAllAsync(audience, cancellationToken).ConfigureAwait(false);
        var merged = new Dictionary<string, bool>(all, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in overrides)
        {
            merged[key] = value;
        }

        return merged;
    }
}
