using KlaraHome.IntegrationTests.Api;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Identity.Infrastructure.Access;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KlaraHome.IntegrationTests.Identity;

/// <summary>
/// The real API host pointed at the migrated test database, with the OTP dispatcher replaced by
/// one that keeps what it was asked to send.
/// </summary>
/// <remarks>
/// Only the delivery channel is substituted. Everything that decides whether a code is accepted —
/// the hash, the expiry, the attempt budget, the per-destination throttle — is the implementation
/// that runs in production.
/// </remarks>
/// <param name="connectionString">The migrated database from the collection fixture.</param>
public sealed class IdentityApiFactory(string connectionString) : KlaraHomeApiFactory
{
    /// <summary>The codes and links this host has sent.</summary>
    internal CapturingOtpDispatcher Otp { get; } = new();

    protected override IDictionary<string, string?> Settings
    {
        get
        {
            var settings = base.Settings;

            foreach (var (key, value) in KlaraHomeSchemaFixture.SharedSettings(connectionString))
            {
                settings[key] = value;
            }

            return settings;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IOtpDispatcher>(Otp)));
    }
}
