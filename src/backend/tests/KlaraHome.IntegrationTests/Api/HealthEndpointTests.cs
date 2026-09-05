using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace KlaraHome.IntegrationTests.Api;

public sealed class HealthEndpointTests(KlaraHomeApiFactory factory) : IClassFixture<KlaraHomeApiFactory>
{
    [Fact]
    public async Task Liveness_answers_200_while_the_process_is_running()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/health/live", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Healthy", payload.GetProperty("status").GetString());
        Assert.Contains(
            "self",
            payload.GetProperty("checks").EnumerateArray().Select(check => check.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Readiness_answers_200_when_every_configured_dependency_answers()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/health/ready", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Probes_are_never_cached()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/health/live", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task Probes_stay_out_of_the_public_api_document()
    {
        using var client = factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/openapi/v1.json", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.False(document.GetProperty("paths").TryGetProperty("/health/live", out _));
    }
}
