using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Platform.Application.Settings;
using KlaraHome.Modules.Platform.Infrastructure.Settings;

namespace KlaraHome.UnitTests.Platform;

/// <summary>
/// The header and footer treatments a store can choose, and the template it was last given.
/// </summary>
public sealed class BrandingStyleTests
{
    private static readonly BrandingSettingsValidator Validator = new();

    /// <summary>The defaults reproduce the storefront as it was before the designer existed.</summary>
    [Fact]
    public void The_defaults_are_the_original_header_and_footer()
    {
        var branding = new BrandingSettings();

        Assert.Equal(string.Empty, branding.TemplateId);
        Assert.Equal("inline", branding.HeaderStyle);
        Assert.Equal("columns", branding.FooterStyle);
        Assert.True(Validator.Validate(branding).IsValid);
    }

    /// <summary>Every published header style is accepted.</summary>
    [Theory]
    [InlineData("inline")]
    [InlineData("centred")]
    [InlineData("search")]
    [InlineData("wordmark")]
    public void A_known_header_style_is_accepted(string style)
        => Assert.True(Validator.Validate(new BrandingSettings { HeaderStyle = style }).IsValid);

    /// <summary>Every published footer style is accepted.</summary>
    [Theory]
    [InlineData("columns")]
    [InlineData("slim")]
    [InlineData("columnsNewsletter")]
    public void A_known_footer_style_is_accepted(string style)
        => Assert.True(Validator.Validate(new BrandingSettings { FooterStyle = style }).IsValid);

    /// <summary>An unknown word, the wrong case and an empty string are all refused, naming the field.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("Inline")]
    [InlineData("sticky")]
    public void An_unknown_header_style_is_refused(string style)
    {
        var result = Validator.Validate(new BrandingSettings { HeaderStyle = style });

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(BrandingSettings.HeaderStyle));
    }

    /// <summary>An unknown footer style is refused, naming the field.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("Slim")]
    [InlineData("mega")]
    public void An_unknown_footer_style_is_refused(string style)
    {
        var result = Validator.Validate(new BrandingSettings { FooterStyle = style });

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(BrandingSettings.FooterStyle));
    }

    /// <summary>A template id is informational: empty or short is fine, an essay is not.</summary>
    [Fact]
    public void The_template_id_is_empty_or_short()
    {
        Assert.True(Validator.Validate(new BrandingSettings { TemplateId = "loom" }).IsValid);
        Assert.False(Validator.Validate(new BrandingSettings { TemplateId = new string('x', 41) }).IsValid);
    }

    /// <summary>The settings schema publishes the choices, so the admin page draws a select.</summary>
    [Fact]
    public void The_schema_publishes_the_choices()
    {
        var fields = SettingsSchemaReader.Describe(typeof(BrandingSettings), Validator);

        Assert.Equal(
            ["inline", "centred", "search", "wordmark"],
            fields.Single(field => field.Name == "headerStyle").Choices);

        Assert.Equal(
            ["columns", "slim", "columnsNewsletter"],
            fields.Single(field => field.Name == "footerStyle").Choices);

        Assert.Null(fields.Single(field => field.Name == "templateId").Choices);
    }

    /// <summary>A stored document from before the designer, with none of the new keys, reads as the defaults.</summary>
    [Fact]
    public void An_older_stored_document_reads_as_the_defaults()
    {
        var section = SettingsCatalog.Find(BrandingSettings.SectionKey)!;

        Assert.True(section.TryParse("""{ "storeName": "Klara Home" }""", out var parsed, out _));

        var branding = Assert.IsType<BrandingSettings>(parsed);

        Assert.Equal("inline", branding.HeaderStyle);
        Assert.Equal("columns", branding.FooterStyle);
    }
}
