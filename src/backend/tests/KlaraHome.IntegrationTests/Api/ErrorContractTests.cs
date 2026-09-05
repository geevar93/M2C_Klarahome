using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace KlaraHome.IntegrationTests.Api;

/// <summary>
/// The RFC 9457 contract from docs/04-api-specification.md §1.2, asserted against the real
/// middleware pipeline rather than against the mapping code in isolation.
/// </summary>
public sealed class ErrorContractTests(KlaraHomeApiFactory factory) : IClassFixture<KlaraHomeApiFactory>
{
    [Fact]
    public async Task A_deliberate_exception_becomes_a_500_problem_document_that_leaks_nothing()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/api/v1/diagnostics/boom", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.False(
            body.Contains("Deliberate diagnostic failure", StringComparison.Ordinal),
            "the exception message must not reach the client");
        Assert.False(
            body.Contains("at KlaraHome", StringComparison.Ordinal),
            "a stack trace must never be serialised");

        var problem = JsonDocument.Parse(body).RootElement;
        Assert.Equal(500, problem.GetProperty("status").GetInt32());
        Assert.Equal("UNEXPECTED_ERROR", problem.GetProperty("code").GetString());
        Assert.Equal("https://klarahome.dev/errors/unexpected-error", problem.GetProperty("type").GetString());
        Assert.Equal("Unexpected error", problem.GetProperty("title").GetString());
        Assert.Equal("/api/v1/diagnostics/boom", problem.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("correlationId").GetString()));
    }

    [Theory]
    [InlineData("Malformed", 400, "DIAGNOSTIC_MALFORMED")]
    [InlineData("Unauthorized", 401, "UNAUTHORIZED")]
    [InlineData("Forbidden", 403, "FORBIDDEN")]
    [InlineData("NotFound", 404, "DIAGNOSTIC_NOT_FOUND")]
    [InlineData("Conflict", 409, "DIAGNOSTIC_CONFLICT")]
    [InlineData("Gone", 410, "DIAGNOSTIC_GONE")]
    [InlineData("Validation", 422, "VALIDATION_FAILED")]
    [InlineData("RateLimited", 429, "RATE_LIMITED")]
    [InlineData("Unavailable", 503, "DIAGNOSTIC_UNAVAILABLE")]
    public async Task Every_error_type_reaches_the_wire_with_its_status_and_its_code(
        string errorType,
        int expectedStatus,
        string expectedCode)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            new Uri($"/api/v1/diagnostics/error/{errorType}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, (int)response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(expectedCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_unrouted_path_produces_the_same_document_shape_as_a_handled_failure()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/api/v1/there-is-no-such-resource", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("NOT_FOUND", problem.GetProperty("code").GetString());
        Assert.Equal("https://klarahome.dev/errors/not-found", problem.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("correlationId").GetString()));
    }

    [Fact]
    public async Task Validation_failures_return_422_with_camel_cased_field_names()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/diagnostics/echo", UriKind.Relative),
            new { message = "", repeat = 99 },
            TestContext.Current.CancellationToken);

        Assert.Equal(422, (int)response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("VALIDATION_FAILED", problem.GetProperty("code").GetString());

        var errors = problem.GetProperty("errors");
        Assert.Contains(
            "Message is required.",
            errors.GetProperty("message").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains(
            "Repeat must be between 1 and 10.",
            errors.GetProperty("repeat").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task A_business_rule_refusal_returns_422_with_its_own_code_and_no_field_errors()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/diagnostics/echo", UriKind.Relative),
            new { message = "refuse", repeat = 1 },
            TestContext.Current.CancellationToken);

        Assert.Equal(422, (int)response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("ECHO_REFUSED", problem.GetProperty("code").GetString());
        Assert.False(problem.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task A_valid_command_travels_through_the_dispatcher_and_returns_its_response()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/diagnostics/echo", UriKind.Relative),
            new { message = "ok", repeat = 3 },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("okokok", payload.GetProperty("message").GetString());
        Assert.Equal(6, payload.GetProperty("length").GetInt32());
    }

    [Fact]
    public async Task A_malformed_body_is_a_400_and_not_a_500()
    {
        using var client = factory.CreateClient();
        using var content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(
            new Uri("/api/v1/diagnostics/echo", UriKind.Relative),
            content,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
    }
}
