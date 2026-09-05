using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Platform.Domain;
using KlaraHome.Modules.Platform.Infrastructure.FeatureFlags;
using KlaraHome.Modules.Platform.Infrastructure.Persistence;
using KlaraHome.Modules.Platform.Infrastructure.Seeding;
using KlaraHome.Modules.Platform.Infrastructure.Settings;
using KlaraHome.Modules.Platform.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KlaraHome.IntegrationTests.Platform;

/// <summary>
/// What a first deploy leaves behind, and what a second one must not disturb.
/// </summary>
[Collection(PlatformSchema.CollectionName)]
public sealed class PlatformSeedingTests(PlatformSchemaFixture fixture)
{
    [Fact]
    public async Task The_configured_tenant_has_a_row()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();

        var row = await context.Tenants.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(tenant.TenantId, row.Id);
        Assert.Equal(PlatformSchemaFixture.TenantCode, row.Code);
        Assert.Equal(PlatformSchemaFixture.TenantName, row.Name);
        Assert.Equal(TenantStatus.Active, row.Status);
    }

    [Fact]
    public async Task The_jurisdictions_and_tariff_chapters_are_seeded()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        Assert.Equal(
            IndianJurisdictions.All.Count,
            await context.States.CountAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            HsnChapters.All.Count,
            await context.HsnCodes.CountAsync(TestContext.Current.CancellationToken));

        var maharashtra = await context.States
            .SingleAsync(state => state.Code == "27", TestContext.Current.CancellationToken);

        Assert.Equal("Maharashtra", maharashtra.Name);
    }

    [Fact]
    public async Task Reference_data_carries_no_tenant_and_is_therefore_visible_to_every_query()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        // The global tenant filter applies to ITenantScoped entities only. If states ever gained
        // that marker, this query would return nothing under a different tenant and every address
        // form in the product would silently empty.
        var columns = await context.Database
            .SqlQuery<string>($"""
                SELECT column_name AS "Value" FROM information_schema.columns
                WHERE table_schema = 'platform' AND table_name = 'states'
                """)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("tenant_id", columns);
    }

    [Fact]
    public async Task Every_settings_section_has_a_row()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        var keys = await context.StoreSettings
            .Select(setting => setting.Key)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            SettingsCatalog.Sections.Select(section => section.Key).Order(StringComparer.Ordinal),
            keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Branding_and_localization_come_from_the_deployments_configuration()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var scope = fixture.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IStoreSettings>();

        var branding = await settings.GetAsync<BrandingSettings>(TestContext.Current.CancellationToken);
        var localization = await settings.GetAsync<LocalizationSettings>(TestContext.Current.CancellationToken);

        // This is the whole point of the module: a fresh install already carries the client's name,
        // not ours, without a line of code changing.
        Assert.Equal(PlatformSchemaFixture.TenantName, branding.StoreName);
        Assert.Equal("en-IN", localization.Locale);
        Assert.Equal("Asia/Kolkata", localization.TimeZone);
    }

    [Fact]
    public async Task Every_declared_feature_flag_has_a_row()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        var keys = await context.FeatureFlags
            .Select(flag => flag.Key)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            PlatformFeatures.All.Select(flag => flag.Key).Order(StringComparer.Ordinal),
            keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Seeding_again_changes_nothing()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        var before = await CountsAsync();
        await fixture.ReseedAsync(TestContext.Current.CancellationToken);
        var after = await CountsAsync();

        // A seeder runs on every deploy. One that duplicated a row, or reset a value an operator
        // had changed, would be indistinguishable from a product that forgets its own settings.
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task The_tenant_readiness_check_passes_for_the_configured_tenant()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var scope = fixture.Services.CreateScope();

        var check = new TenantHealthCheck(
            scope.ServiceProvider.GetRequiredService<PlatformDbContext>(),
            scope.ServiceProvider.GetRequiredService<ITenantContext>());

        var result = await check.CheckHealthAsync(
            new HealthCheckContext(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task The_tenant_readiness_check_fails_when_the_configured_tenant_has_no_row()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var scope = fixture.Services.CreateScope();

        // This is what changing Tenant:Code on a deployment that already holds data looks like:
        // a different derived id, and every query returning nothing. It has to be loud.
        var check = new TenantHealthCheck(
            scope.ServiceProvider.GetRequiredService<PlatformDbContext>(),
            new Testing.Persistence.FixedTenantContext(Guid.CreateVersion7(), "someone-else"));

        var result = await check.CheckHealthAsync(
            new HealthCheckContext(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    private async Task<(int Tenants, int States, int Hsn, int Settings, int Flags)> CountsAsync()
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var token = TestContext.Current.CancellationToken;

        return (
            await context.Tenants.CountAsync(token),
            await context.States.CountAsync(token),
            await context.HsnCodes.CountAsync(token),
            await context.StoreSettings.CountAsync(token),
            await context.FeatureFlags.CountAsync(token));
    }
}
