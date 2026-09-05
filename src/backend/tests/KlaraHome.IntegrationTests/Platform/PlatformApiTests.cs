using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.IntegrationTests.Api;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Platform.Infrastructure.FeatureFlags;

namespace KlaraHome.IntegrationTests.Platform;

/// <summary>
/// The Platform module over HTTP, through the real host: the storefront configuration document,
/// the reference data, and a feature flag actually changing what the API serves.
/// </summary>
[Collection(KlaraHomeSchema.CollectionName)]
public sealed class PlatformApiTests(KlaraHomeSchemaFixture fixture) : IDisposable
{
    private PlatformApiFactory? _factory;

    public void Dispose() => _factory?.Dispose();

    [Fact]
    public async Task The_store_config_carries_the_branding_this_deployment_is_configured_with()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var client = CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/store/config",
            TestContext.Current.CancellationToken);

        Assert.Equal(KlaraHomeSchemaFixture.TenantCode, document.GetProperty("tenantCode").GetString());

        var branding = document.GetProperty("settings").GetProperty("branding");
        Assert.False(string.IsNullOrWhiteSpace(branding.GetProperty("storeName").GetString()));

        // Legal and support are published because the law requires them to be, and the storefront
        // footer reads them from here rather than from anything compiled in.
        Assert.True(document.GetProperty("settings").TryGetProperty("legal", out _));
        Assert.True(document.GetProperty("settings").TryGetProperty("support", out _));
        Assert.True(document.GetProperty("features").TryGetProperty(PlatformFeatures.PublicStoreConfig, out _));
    }

    [Fact]
    public async Task A_feature_flag_turns_an_endpoint_off_and_on_again()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var client = await CreateAdminClientAsync();

        var before = await client.GetAsync(new Uri("/api/v1/store/config", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        try
        {
            var off = await client.PutAsJsonAsync(
                $"/api/v1/admin/feature-flags/{PlatformFeatures.PublicStoreConfig}",
                new { enabled = false },
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, off.StatusCode);

            // No deploy, no restart: the next request already sees it.
            var during = await client.GetAsync(new Uri("/api/v1/store/config", UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, during.StatusCode);

            var problem = await during.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            Assert.Equal("FEATURE_DISABLED", problem.GetProperty("code").GetString());
        }
        finally
        {
            await client.PutAsJsonAsync(
                $"/api/v1/admin/feature-flags/{PlatformFeatures.PublicStoreConfig}",
                new { enabled = true },
                TestContext.Current.CancellationToken);
        }

        var after = await client.GetAsync(new Uri("/api/v1/store/config", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task A_settings_change_over_http_is_stored_and_appears_in_the_audit_search()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var client = await CreateAdminClientAsync();

        var response = await client.PutAsJsonAsync(
            "/api/v1/admin/settings/commerce",
            new { returnWindowDays = 14, codEnabled = true, codOrderValueLimit = 2500 },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/admin/settings",
            TestContext.Current.CancellationToken);

        var commerce = stored.GetProperty("sections")
            .EnumerateArray()
            .Single(section => section.GetProperty("key").GetString() == "commerce")
            .GetProperty("value");

        Assert.Equal(14, commerce.GetProperty("returnWindowDays").GetInt32());
        Assert.Equal(2500m, commerce.GetProperty("codOrderValueLimit").GetDecimal());

        var audit = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/admin/audit-logs?entityType=StoreSetting&entityId=commerce&size=5",
            TestContext.Current.CancellationToken);

        var items = audit.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);

        var entry = items[0];
        Assert.Equal("platform.settings.updated", entry.GetProperty("action").GetString());

        // An enum crosses the wire as its name. "actorType": 4 would break the day a value is
        // inserted into the middle of the enum, and tells a support engineer nothing meanwhile.
        Assert.Equal("StaffUser", entry.GetProperty("actorType").GetString());

        // The stored documents are objects in the response, not strings holding JSON.
        Assert.Equal(JsonValueKind.Object, entry.GetProperty("before").ValueKind);
        Assert.Equal(JsonValueKind.Object, entry.GetProperty("after").ValueKind);
        Assert.Equal(14, entry.GetProperty("after").GetProperty("returnWindowDays").GetInt32());

        // Written from the request, not passed in by the caller.
        Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("correlationId").GetString()));
    }

    [Fact]
    public async Task An_invalid_settings_document_is_a_422_naming_the_field()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var client = await CreateAdminClientAsync();

        var response = await client.PutAsJsonAsync(
            "/api/v1/admin/settings/branding",
            new { storeName = "Klara Home", primaryColor = "puce" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableContent, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("VALIDATION_FAILED", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty(nameof(Contracts.Platform.BrandingSettings.PrimaryColor), out _));
    }

    [Fact]
    public async Task The_jurisdictions_are_served_to_an_anonymous_caller()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var client = CreateClient();

        var states = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/store/states",
            TestContext.Current.CancellationToken);

        var all = states.EnumerateArray().ToList();
        Assert.Equal(36, all.Count);

        var maharashtra = all.Single(state => state.GetProperty("code").GetString() == "27");
        Assert.Equal("Maharashtra", maharashtra.GetProperty("name").GetString());
        Assert.Equal("State", maharashtra.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task An_unknown_pincode_is_a_not_found_rather_than_an_empty_answer()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var client = CreateClient();

        // The dataset is imported by an operator and is empty here, which is exactly the state a
        // fresh deployment is in.
        var response = await client.GetAsync(new Uri("/api/v1/store/pincodes/400001", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("PINCODE_NOT_FOUND", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_malformed_pincode_never_reaches_a_handler()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var client = CreateClient();

        // The route constraint rejects it, so no query is issued and nothing has to defend itself
        // against six arbitrary characters in a path segment.
        foreach (var candidate in new[] { "abcdef", "012345", "4000012", "400" })
        {
            var response = await client.GetAsync(
                new Uri($"/api/v1/store/pincodes/{candidate}", UriKind.Relative),
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    private HttpClient CreateClient()
    {
        _factory ??= new PlatformApiFactory(fixture.ConnectionString);
        return _factory.CreateClient();
    }

    /// <summary>
    /// A client signed in as the deployment's first administrator, through the real sign-in
    /// including its mandatory second factor.
    /// </summary>
    /// <remarks>
    /// The admin surface is behind permission policies from Step 7, so these tests authenticate
    /// rather than assert against an open endpoint. Signing in properly also means the audit
    /// entries they check now carry a real actor.
    /// </remarks>
    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = CreateClient();

        await TestSignIn.SignInAsync(
            client,
            "admin",
            KlaraHomeSchemaFixture.BootstrapEmail,
            KlaraHomeSchemaFixture.BootstrapPassword,
            TestContext.Current.CancellationToken);

        return client;
    }
}

/// <summary>
/// The real API host, pointed at the migrated test database.
/// </summary>
/// <remarks>
/// The base factory deliberately runs without a database, so most API tests assert middleware and
/// contracts rather than data. These ones cannot: a settings change that is not stored, or a flag
/// that does not reach the endpoint, would pass against a stub.
/// </remarks>
/// <param name="connectionString">The migrated database from the collection fixture.</param>
public sealed class PlatformApiFactory(string connectionString) : KlaraHomeApiFactory
{
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
}
