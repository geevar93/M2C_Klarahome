using System.Text.Json;
using FluentValidation;
using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Platform.Infrastructure.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.UnitTests.Platform;

/// <summary>
/// The settings catalogue is the single list that drives the seeder, the admin surface, the public
/// store config and the typed reader. If it is inconsistent, all four are.
/// </summary>
public sealed class SettingsCatalogTests
{
    [Fact]
    public void Every_section_has_a_distinct_key()
    {
        var keys = SettingsCatalog.Sections.Select(section => section.Key).ToList();

        Assert.Distinct(keys, StringComparer.OrdinalIgnoreCase);
        Assert.All(keys, key => Assert.False(string.IsNullOrWhiteSpace(key)));
    }

    [Fact]
    public void Every_section_is_reachable_by_its_own_key()
    {
        foreach (var section in SettingsCatalog.Sections)
        {
            var found = SettingsCatalog.Find(section.Key);

            Assert.NotNull(found);
            Assert.Equal(section.SectionType, found.SectionType);
        }
    }

    [Fact]
    public void An_unknown_key_resolves_to_nothing_rather_than_throwing()
    {
        // The admin surface turns this into a 404. It must not be an exception, because the key
        // comes straight from a URL.
        Assert.Null(SettingsCatalog.Find("no-such-section"));
        Assert.Null(SettingsCatalog.Find(string.Empty));
    }

    [Fact]
    public void A_sections_defaults_round_trip_through_storage()
    {
        foreach (var section in SettingsCatalog.Sections)
        {
            var defaults = section.SerializeDefaults();

            Assert.True(
                section.TryParse(defaults, out var parsed, out var error),
                $"The defaults of '{section.Key}' did not parse back: {error}");

            Assert.Equal(defaults, section.Serialize(parsed!));
        }
    }

    [Fact]
    public void A_stored_document_is_camel_cased_like_every_other_payload()
    {
        var document = new SettingsSectionDescriptor<BrandingSettings>().SerializeDefaults();

        using var parsed = JsonDocument.Parse(document);

        Assert.True(parsed.RootElement.TryGetProperty("storeName", out _));
        Assert.False(parsed.RootElement.TryGetProperty("StoreName", out _));
    }

    [Fact]
    public void A_property_the_section_does_not_declare_is_rejected()
    {
        var section = SettingsCatalog.Find(BrandingSettings.SectionKey)!;

        // Silently dropping it would let an operator save a typo and believe the store name had
        // changed.
        var accepted = section.TryParse(
            """{ "storeName": "Klara Home", "storNam": "typo" }""",
            out _,
            out var error);

        Assert.False(accepted);
        Assert.NotNull(error);
    }

    [Fact]
    public void A_document_that_is_not_an_object_is_rejected()
    {
        var section = SettingsCatalog.Find(BrandingSettings.SectionKey)!;

        Assert.False(section.TryParse("\"not an object\"", out _, out _));
        Assert.False(section.TryParse("[1, 2, 3]", out _, out _));
    }

    [Fact]
    public void Validation_reports_the_offending_field()
    {
        var services = BuildValidators();
        var section = SettingsCatalog.Find(LegalSettings.SectionKey)!;

        Assert.True(section.TryParse("""{ "gstin": "not-a-gstin" }""", out var parsed, out _));

        var errors = section.Validate(services, parsed!);

        Assert.Contains(nameof(LegalSettings.Gstin), errors.Keys);
    }

    [Fact]
    public void A_valid_section_produces_no_errors()
    {
        var services = BuildValidators();

        foreach (var section in SettingsCatalog.Sections)
        {
            Assert.True(section.TryParse(section.SerializeDefaults(), out var parsed, out _));

            var errors = section.Validate(services, parsed!);

            Assert.True(
                errors.Count == 0,
                $"The defaults of '{section.Key}' fail their own validator: "
                + string.Join("; ", errors.Select(pair => $"{pair.Key}: {string.Join(", ", pair.Value)}")));
        }
    }

    /// <summary>The validators as the host registers them: scanned out of the module assembly.</summary>
    private static ServiceProvider BuildValidators()
    {
        var services = new ServiceCollection();

        services.AddValidatorsFromAssembly(
            typeof(KlaraHome.Modules.Platform.PlatformModule).Assembly,
            ServiceLifetime.Scoped,
            includeInternalTypes: true);

        return services.BuildServiceProvider();
    }
}
