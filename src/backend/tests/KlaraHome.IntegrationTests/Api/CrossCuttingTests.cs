using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.Infrastructure.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.IntegrationTests.Api;

/// <summary>Correlation, module composition and the generated API document.</summary>
public sealed class CrossCuttingTests(KlaraHomeApiFactory factory) : IClassFixture<KlaraHomeApiFactory>
{
    [Fact]
    public async Task An_inbound_correlation_id_is_echoed_on_the_response()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/meta", UriKind.Relative));
        request.Headers.Add("X-Correlation-Id", "order-support-ticket-4417");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("order-support-ticket-4417", Assert.Single(response.Headers.GetValues("X-Correlation-Id")));
    }

    [Fact]
    public async Task A_correlation_id_is_generated_when_the_caller_supplies_none()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/api/v1/meta", UriKind.Relative),
            TestContext.Current.CancellationToken);

        var correlationId = Assert.Single(response.Headers.GetValues("X-Correlation-Id"));
        Assert.False(string.IsNullOrWhiteSpace(correlationId));
    }

    [Theory]
    [InlineData("has spaces")]
    [InlineData("newline\ttab")]
    [InlineData("<script>alert(1)</script>")]
    public async Task A_correlation_id_that_could_forge_a_log_line_is_replaced_not_propagated(string forged)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/meta", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", forged);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.NotEqual(forged, Assert.Single(response.Headers.GetValues("X-Correlation-Id")));
    }

    [Fact]
    public async Task The_same_correlation_id_appears_in_the_header_and_in_the_problem_document()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri("/api/v1/diagnostics/boom", UriKind.Relative));
        request.Headers.Add("X-Correlation-Id", "trace-me-9021");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("trace-me-9021", problem.GetProperty("correlationId").GetString());
        Assert.Equal("trace-me-9021", Assert.Single(response.Headers.GetValues("X-Correlation-Id")));
    }

    /// <remarks>
    /// Asserted against the registry's rules rather than against a list of the modules that happened
    /// to exist when this was written. The literal four names it used to carry were the four modules
    /// of Step 3, and every step since that added a module had to remember to extend a list that was
    /// not testing anything the registry does not already guarantee. What it does guarantee — and
    /// what a boundary violation would break — is that no name and no schema is claimed twice, and
    /// that the modules come back in the migration order they declare.
    /// </remarks>
    [Fact]
    public void Every_module_is_discovered_and_registered_exactly_once()
    {
        var registry = factory.Services.GetRequiredService<ModuleRegistry>();

        Assert.NotEmpty(registry.Modules);
        Assert.Distinct(registry.Modules.Select(module => module.Name));
        Assert.Distinct(registry.Modules.Select(module => module.Schema));
        Assert.Equal(
            registry.Modules.Select(module => module.Order).Order(),
            registry.Modules.Select(module => module.Order));
    }

    [Fact]
    public async Task The_api_surface_is_reachable_under_the_versioned_prefix()
    {
        using var client = factory.CreateClient();

        var payload = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/v1/meta", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal("v1", payload.GetProperty("apiVersion").GetString());
        Assert.Equal("Development", payload.GetProperty("environment").GetString());
        Assert.Equal(TimeSpan.Zero, payload.GetProperty("serverTimeUtc").GetDateTimeOffset().Offset);
    }

    [Fact]
    public async Task The_openapi_document_renders_and_names_its_operations_for_a_client_generator()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/openapi/v1.json", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.StartsWith("3.1", document.GetProperty("openapi").GetString(), StringComparison.Ordinal);
        Assert.Equal("Klara Home API", document.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("v1", document.GetProperty("info").GetProperty("version").GetString());

        var meta = document.GetProperty("paths").GetProperty("/api/v1/meta").GetProperty("get");
        Assert.Equal("metaGet", meta.GetProperty("operationId").GetString());
        Assert.True(
            meta.GetProperty("responses").TryGetProperty("500", out _),
            "every operation declares the universal failure responses");
    }

    [Fact]
    public async Task Development_only_diagnostics_stay_out_of_the_published_document()
    {
        using var client = factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/openapi/v1.json", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain(
            document.GetProperty("paths").EnumerateObject().Select(path => path.Name),
            path => path.Contains("diagnostics", StringComparison.Ordinal));
    }
}
