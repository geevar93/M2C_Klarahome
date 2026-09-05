using Microsoft.Extensions.Options;

namespace KlaraHome.IntegrationTests.Api;

/// <summary>
/// Fail-fast configuration. A container that is misconfigured must refuse to start rather than
/// serve traffic that fails later, in a way nobody traces back to an environment variable.
/// </summary>
public sealed class StartupValidationTests
{
    [Fact]
    public async Task A_tenant_code_that_breaks_the_naming_rule_stops_the_host_from_starting()
    {
        await using var factory = new MisconfiguredFactory(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tenant:Code"] = "Not Kebab Case",
        });

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("Tenant", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_api_version_that_is_not_a_version_segment_stops_the_host_from_starting()
    {
        await using var factory = new MisconfiguredFactory(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Api:Version"] = "version-one",
        });

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    [Fact]
    public async Task An_otlp_endpoint_that_is_not_an_absolute_uri_stops_the_host_from_starting()
    {
        await using var factory = new MisconfiguredFactory(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Observability:OtlpEndpoint"] = "collector-without-a-scheme",
        });

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("OtlpEndpoint", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rate_limit_window_outside_the_permitted_range_stops_the_host_from_starting()
    {
        await using var factory = new MisconfiguredFactory(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["RateLimiting:Global:WindowSeconds"] = "0",
        });

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    private sealed class MisconfiguredFactory(IDictionary<string, string?> overrides) : KlaraHomeApiFactory
    {
        protected override IDictionary<string, string?> Settings
        {
            get
            {
                var settings = base.Settings;
                foreach (var (key, value) in overrides)
                {
                    settings[key] = value;
                }

                return settings;
            }
        }
    }
}
