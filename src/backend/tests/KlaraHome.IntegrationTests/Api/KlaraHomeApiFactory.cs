using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace KlaraHome.IntegrationTests.Api;

/// <summary>
/// Boots the real API host in-process. Configuration is supplied in memory rather than read from
/// the host appsettings files, so a test asserts behaviour and never an environment.
/// </summary>
public class KlaraHomeApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Settings layered over the host defaults. Override to change one thing.</summary>
    protected virtual IDictionary<string, string?> Settings => new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["Api:EnableDiagnosticsEndpoints"] = "true",
        ["Api:TrustProxyHeaders"] = "false",
        ["Observability:ConsoleJson"] = "true",
        ["Observability:MinimumLevel"] = "Warning",
        // The limiter has its own test; leaving it on would make every other test order-dependent.
        ["RateLimiting:Enabled"] = "false",
        // No connection strings: the readiness probe then reports only the checks it can run.
        ["ConnectionStrings:Postgres"] = string.Empty,
        ["ConnectionStrings:Redis"] = string.Empty,
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(Environments.Development);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(Settings));
    }
}
