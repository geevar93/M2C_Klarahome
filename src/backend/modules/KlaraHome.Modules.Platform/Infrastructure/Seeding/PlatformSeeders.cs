using System.Text.Json;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Configuration;
using KlaraHome.Infrastructure.Persistence.Seeding;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Platform.Domain;
using KlaraHome.Modules.Platform.Infrastructure.FeatureFlags;
using KlaraHome.Modules.Platform.Infrastructure.Persistence;
using KlaraHome.Modules.Platform.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Platform.Infrastructure.Seeding;

/// <summary>
/// Writes the tenant row this deployment's configuration describes.
/// </summary>
/// <remarks>
/// Configuration is the source of the tenant's identity, not this table: <c>Tenant:Id</c> has to
/// exist before any row can be written, and onboarding a second business must be a configuration
/// change rather than a database edit (IMPLEMENTATION_PLAN §6). The row is the database's record
/// of that configured tenant — what makes <c>tenant_id</c> on every other table resolvable to a
/// name — and it is what the <c>platform-tenant</c> readiness check compares the configuration
/// against.
/// </remarks>
/// <param name="context">The Platform module's context.</param>
/// <param name="tenant">The ambient tenant, as configured.</param>
/// <param name="options">Supplies the display name.</param>
internal sealed class TenantSeeder(
    PlatformDbContext context,
    ITenantContext tenant,
    IOptions<TenantOptions> options) : IDataSeeder
{
    /// <inheritdoc />
    public string Name => "Platform.Tenant";

    /// <inheritdoc />
    public int Order => 10;

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var displayName = string.IsNullOrWhiteSpace(options.Value.Name) ? tenant.Code : options.Value.Name;

        var existing = await context.Tenants
            .FirstOrDefaultAsync(candidate => candidate.Id == tenant.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            context.Tenants.Add(Tenant.Register(tenant.TenantId, tenant.Code, displayName));
        }
        else if (!string.Equals(existing.Name, displayName, StringComparison.Ordinal))
        {
            // The name follows configuration; the code does not, because every row already written
            // carries the id the code derived.
            existing.Rename(displayName);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Writes the reference data every other module looks jurisdictions and tariff chapters up in.
/// </summary>
/// <param name="context">The Platform module's context.</param>
internal sealed class ReferenceDataSeeder(PlatformDbContext context) : IDataSeeder
{
    /// <inheritdoc />
    public string Name => "Platform.ReferenceData";

    /// <inheritdoc />
    public int Order => 20;

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var states = await context.States.ToDictionaryAsync(
            state => state.Code,
            StringComparer.Ordinal,
            cancellationToken).ConfigureAwait(false);

        foreach (var (code, name, kind) in IndianJurisdictions.All)
        {
            if (states.TryGetValue(code, out var existing))
            {
                // Jurisdictions are renamed and reconstituted - Orissa to Odisha, Jammu and Kashmir
                // from state to union territory - and the GST code survives both. Following the
                // compiled-in list is how a deploy corrects a name rather than leaving the old one
                // on every address form.
                existing.Rename(name, kind);
            }
            else
            {
                context.States.Add(StateOrUnionTerritory.Define(code, name, kind));
            }
        }

        var chapters = await context.HsnCodes.ToDictionaryAsync(
            hsn => hsn.Code,
            StringComparer.Ordinal,
            cancellationToken).ConfigureAwait(false);

        foreach (var (code, description) in HsnChapters.All)
        {
            if (chapters.TryGetValue(code, out var existing))
            {
                // Descriptions are corrected between Harmonized System revisions; rates are not
                // touched, because an operator may have set one against a chapter deliberately.
                existing.Update(description, existing.DefaultGstRate);
            }
            else
            {
                context.HsnCodes.Add(HsnCode.Define(code, description));
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Fills any settings section the deployment has never saved with its defaults, overlaid with what
/// configuration already knows.
/// </summary>
/// <remarks>
/// It never overwrites a section that exists. A seeder runs on every deploy, and one that reset the
/// store name each time would be indistinguishable from a product that forgets its own branding.
/// </remarks>
/// <param name="context">The Platform module's context.</param>
/// <param name="tenant">The ambient tenant, for the initial store name.</param>
/// <param name="options">Supplies locale, timezone and display name.</param>
internal sealed class StoreSettingsSeeder(
    PlatformDbContext context,
    ITenantContext tenant,
    IOptions<TenantOptions> options) : IDataSeeder
{
    /// <inheritdoc />
    public string Name => "Platform.StoreSettings";

    /// <inheritdoc />
    public int Order => 30;

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var existing = await context.StoreSettings
            .ToDictionaryAsync(setting => setting.Key, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        foreach (var section in SettingsCatalog.Sections)
        {
            if (existing.TryGetValue(section.Key, out var stored))
            {
                // Visibility is the section type's declaration, not an operator's choice, so it is
                // realigned on every deploy even though the value is left alone.
                stored.SetVisibility(section.IsPublic);
                continue;
            }

            context.StoreSettings.Add(StoreSetting.Create(section.Key, InitialValueOf(section), section.IsPublic));
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The section's own defaults, except where the deployment's configuration already answers the
    /// question — which is what makes a fresh install of the product carry the client's name and
    /// locale rather than ours.
    /// </summary>
    private string InitialValueOf(ISettingsSectionDescriptor section)
    {
        var tenantOptions = options.Value;
        var displayName = string.IsNullOrWhiteSpace(tenantOptions.Name) ? tenant.Code : tenantOptions.Name;

        if (section.SectionType == typeof(BrandingSettings))
        {
            return JsonSerializer.Serialize(new BrandingSettings { StoreName = displayName }, SettingsJson.Storage);
        }

        if (section.SectionType == typeof(LocalizationSettings))
        {
            return JsonSerializer.Serialize(
                new LocalizationSettings
                {
                    Locale = tenantOptions.DefaultLocale,
                    TimeZone = tenantOptions.DefaultTimeZone,
                },
                SettingsJson.Storage);
        }

        return section.SerializeDefaults();
    }
}

/// <summary>
/// Declares the module's feature flags so the admin UI lists every switch that exists, not only the
/// ones somebody has already touched.
/// </summary>
/// <param name="context">The Platform module's context.</param>
internal sealed class FeatureFlagSeeder(PlatformDbContext context) : IDataSeeder
{
    /// <inheritdoc />
    public string Name => "Platform.FeatureFlags";

    /// <inheritdoc />
    public int Order => 40;

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var existing = await context.FeatureFlags
            .ToDictionaryAsync(flag => flag.Key, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        foreach (var declaration in PlatformFeatures.All)
        {
            if (existing.TryGetValue(declaration.Key, out var flag))
            {
                // The description follows the code; the switch and the rollout belong to whoever
                // last changed them in the admin UI.
                flag.Describe(declaration.Description);
                continue;
            }

            context.FeatureFlags.Add(
                FeatureFlag.Declare(declaration.Key, declaration.Enabled, declaration.Description));
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
