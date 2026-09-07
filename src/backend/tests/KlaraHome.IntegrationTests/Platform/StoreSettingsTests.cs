using System.Text.Json;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Platform.Application.FeatureFlags;
using KlaraHome.Modules.Platform.Application.Settings;
using KlaraHome.Modules.Platform.Infrastructure.FeatureFlags;
using KlaraHome.Modules.Platform.Infrastructure.Persistence;
using KlaraHome.Modules.Platform.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.IntegrationTests.Platform;

/// <summary>
/// Settings and feature flags through the real handlers, against the real tables: what an operator
/// changes, what other code then reads, and what the audit trail records about it.
/// </summary>
[Collection(KlaraHomeSchema.CollectionName)]
public sealed class StoreSettingsTests(KlaraHomeSchemaFixture fixture)
{
    [Fact]
    public async Task Changing_a_section_changes_what_the_typed_reader_returns()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var scope = fixture.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var settings = scope.ServiceProvider.GetRequiredService<IStoreSettings>();

        var updated = await dispatcher.SendAsync(
            new UpdateStoreSettingCommand(
                SupportSettings.SectionKey,
                Document("""{ "email": "help@example.in", "phone": "+919876543210" }""")),
            TestContext.Current.CancellationToken);

        Assert.True(updated.IsSuccess, updated.IsFailure ? updated.Error.ToString() : string.Empty);

        // Read through the cache the storefront reads through, not straight from the table: an
        // invalidation that did not happen would show up right here.
        var support = await settings.GetAsync<SupportSettings>(TestContext.Current.CancellationToken);

        Assert.Equal("help@example.in", support.Email);
        Assert.Equal("+919876543210", support.Phone);

        // A section is replaced as a whole, so the fields the request omitted are back at their
        // defaults rather than left at whatever they were.
        Assert.Equal(48, support.AcknowledgementHours);
    }

    [Fact]
    public async Task A_settings_change_writes_an_audit_entry_with_a_real_before_and_after()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var scope = fixture.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        var tagline = "Audited at " + Guid.CreateVersion7().ToString("n");

        var result = await dispatcher.SendAsync(
            new UpdateStoreSettingCommand(
                BrandingSettings.SectionKey,
                Document($$"""{ "storeName": "{{KlaraHomeSchemaFixture.TenantName}}", "tagline": "{{tagline}}" }""")),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : string.Empty);

        var entry = await context.AuditLogs
            .AsNoTracking()
            .Where(row => row.Action == UpdateStoreSettingCommandHandler.AuditAction
                          && row.EntityId == BrandingSettings.SectionKey)
            .OrderByDescending(row => row.OccurredAt)
            .FirstAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateStoreSettingCommandHandler.AuditEntityType, entry.EntityType);
        Assert.NotNull(entry.Before);
        Assert.NotNull(entry.After);

        // The "after" is what was stored; the "before" is what was really there, not the request
        // echoed back.
        Assert.Contains(tagline, entry.After, StringComparison.Ordinal);
        Assert.DoesNotContain(tagline, entry.Before, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_section_is_a_not_found_rather_than_a_new_row()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var scope = fixture.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.SendAsync(
            new UpdateStoreSettingCommand("not-a-section", Document("{}")),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("SETTINGS_SECTION_UNKNOWN", result.Error.Code);
    }

    [Fact]
    public async Task An_invalid_value_is_refused_and_nothing_is_stored()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var scope = fixture.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var settings = scope.ServiceProvider.GetRequiredService<IStoreSettings>();

        var before = await settings.GetAsync<LegalSettings>(TestContext.Current.CancellationToken);

        var result = await dispatcher.SendAsync(
            new UpdateStoreSettingCommand(LegalSettings.SectionKey, Document("""{ "gstin": "nonsense" }""")),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Contains(nameof(LegalSettings.Gstin), result.Error.FieldErrors.Keys);

        var after = await settings.GetAsync<LegalSettings>(TestContext.Current.CancellationToken);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task A_property_the_section_does_not_declare_is_refused()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var scope = fixture.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.SendAsync(
            new UpdateStoreSettingCommand(
                CommerceSettings.SectionKey,
                Document("""{ "returnWindowDay": 14 }""")),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("SETTINGS_DOCUMENT_INVALID", result.Error.Code);
    }

    [Fact]
    public async Task Reading_the_admin_view_returns_every_section()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var scope = fixture.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.QueryAsync(
            new GetStoreSettingsQuery(),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            SettingsCatalog.Sections.Select(section => section.Key),
            result.Value.Sections.Select(section => section.Key));
        Assert.All(result.Value.Sections, section => Assert.Equal(JsonValueKind.Object, section.Value.ValueKind));
    }

    [Fact]
    public async Task Turning_a_flag_off_is_visible_to_the_next_evaluation()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var scope = fixture.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var flags = scope.ServiceProvider.GetRequiredService<IFeatureFlags>();

        Assert.True(await flags.IsEnabledAsync(
            PlatformFeatures.PincodeLookup,
            cancellationToken: TestContext.Current.CancellationToken));

        try
        {
            var off = await dispatcher.SendAsync(
                new UpdateFeatureFlagCommand(PlatformFeatures.PincodeLookup, false, null, null),
                TestContext.Current.CancellationToken);

            Assert.True(off.IsSuccess, off.IsFailure ? off.Error.ToString() : string.Empty);

            // The flag set is cached; if the write did not invalidate it, this would still be true
            // for another minute.
            Assert.False(await flags.IsEnabledAsync(
                PlatformFeatures.PincodeLookup,
                cancellationToken: TestContext.Current.CancellationToken));
        }
        finally
        {
            await dispatcher.SendAsync(
                new UpdateFeatureFlagCommand(PlatformFeatures.PincodeLookup, true, null, null),
                TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task A_flag_change_is_audited()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var scope = fixture.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        var description = "Audited at " + Guid.CreateVersion7().ToString("n");

        await dispatcher.SendAsync(
            new UpdateFeatureFlagCommand(PlatformFeatures.PublicStoreConfig, true, null, description),
            TestContext.Current.CancellationToken);

        var entry = await context.AuditLogs
            .AsNoTracking()
            .Where(row => row.Action == UpdateFeatureFlagCommandHandler.AuditAction
                          && row.EntityId == PlatformFeatures.PublicStoreConfig)
            .OrderByDescending(row => row.OccurredAt)
            .FirstAsync(TestContext.Current.CancellationToken);

        Assert.Contains(description, entry.After, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_flag_nothing_declares_cannot_be_invented_from_the_admin_surface()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var scope = fixture.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        // A flag nothing reads is a switch that does not work, which looks exactly like a bug.
        var result = await dispatcher.SendAsync(
            new UpdateFeatureFlagCommand("platform.not-declared", true, null, null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("FEATURE_FLAG_UNKNOWN", result.Error.Code);
    }

    [Fact]
    public async Task A_rollout_percentage_outside_the_range_is_refused()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        using var scope = fixture.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.SendAsync(
            new UpdateFeatureFlagCommand(
                PlatformFeatures.PublicStoreConfig,
                true,
                new RolloutModel(150, [], []),
                null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("VALIDATION_FAILED", result.Error.Code);
    }

    private static JsonElement Document(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
