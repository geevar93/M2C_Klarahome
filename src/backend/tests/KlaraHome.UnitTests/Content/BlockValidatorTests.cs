using System.Text.Json;
using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Infrastructure.Blocks;

namespace KlaraHome.UnitTests.Content;

/// <summary>
/// The schema check that stands between an editor's block and a shopper's page.
/// </summary>
/// <remarks>
/// Tested while writing it. Validation happens once, on the way in, so that a malformed block is a
/// message next to the field that is wrong rather than a component throwing during server-side
/// rendering — which is a blank home page and a stack trace nobody can attribute to whoever caused
/// it. Getting the refusals right is therefore the whole of that promise.
/// </remarks>
public sealed class BlockValidatorTests
{
    /// <summary>A well-formed hero is accepted and canonicalised.</summary>
    /// <remarks>
    /// Canonical means every declared field is present in schema order, absent ones written as null.
    /// Two blocks that mean the same thing are then byte-identical in the database, which is what
    /// makes a version diff readable.
    /// </remarks>
    [Fact]
    public void A_valid_hero_is_accepted()
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        var result = BlockValidator.Validate(
            BlockType.Hero,
            Json($$"""
                {
                  "headline": "Summer Sale",
                  "imageFileId": "{{Guid.Empty.ToString().Replace("0000-0000", "0000-7000")}}",
                  "align": "centre"
                }
                """),
            "blocks[0]",
            errors);

        Assert.Empty(errors);
        Assert.NotNull(result);
        Assert.Contains("\"headline\":\"Summer Sale\"", result.Config, StringComparison.Ordinal);
        Assert.Contains("\"subheadline\":null", result.Config, StringComparison.Ordinal);
        Assert.Single(result.References.MediaIds);
    }

    /// <summary>A required field that is missing is named.</summary>
    [Fact]
    public void A_missing_required_field_is_refused()
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        var result = BlockValidator.Validate(BlockType.Hero, Json("""{ "headline": "Sale" }"""), "blocks[0]", errors);

        Assert.Null(result);
        Assert.Contains("blocks[0].imageFileId", errors.Keys);
    }

    /// <summary>
    /// A property the schema does not declare is refused rather than dropped.
    /// </summary>
    /// <remarks>
    /// The deliberate choice. A silently dropped field is a merchandiser who typed <c>headLine</c>,
    /// saw no error, and cannot work out why the hero has no heading.
    /// </remarks>
    [Fact]
    public void An_unknown_property_is_refused()
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        var result = BlockValidator.Validate(
            BlockType.RichText,
            Json("""{ "body": "<p>Hi</p>", "headLine": "Oops" }"""),
            "blocks[0]",
            errors);

        Assert.Null(result);
        Assert.Contains("blocks[0].headLine", errors.Keys);
    }

    /// <summary>A choice field accepts only the words it lists.</summary>
    [Fact]
    public void A_choice_field_accepts_only_its_choices()
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        BlockValidator.Validate(
            BlockType.RichText,
            Json("""{ "body": "<p>Hi</p>", "width": "enormous" }"""),
            "blocks[0]",
            errors);

        Assert.Contains("blocks[0].width", errors.Keys);
    }

    /// <summary>Rich text is sanitised, and the editor is told what went.</summary>
    /// <remarks>
    /// Sanitised rather than refused: a paste from a word processor being rejected outright leaves an
    /// editor with nothing they can do. Silence would be worse — they would find out three weeks
    /// later that half a page never rendered.
    /// </remarks>
    [Fact]
    public void Rich_text_is_sanitised_and_reported()
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        BlockValidator.Validate(
            BlockType.RichText,
            Json("""{ "body": "<p onclick=\"x()\">Hi</p>" }"""),
            "blocks[0]",
            errors);

        Assert.Contains("blocks[0].body", errors.Keys);
    }

    /// <summary>A link field refuses a scripting scheme.</summary>
    [Fact]
    public void A_link_field_refuses_a_scripting_scheme()
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        BlockValidator.Validate(
            BlockType.Hero,
            Json($$"""
                {
                  "headline": "Sale",
                  "imageFileId": "0192aaaa-0000-7000-8000-000000000001",
                  "ctaHref": "javascript:alert(1)"
                }
                """),
            "blocks[0]",
            errors);

        Assert.Contains("blocks[0].ctaHref", errors.Keys);
    }

    /// <summary>A carousel shows either a collection or a list, never both and never neither.</summary>
    /// <remarks>
    /// Both would be two answers to what the block shows; neither would be none. Either renders as an
    /// empty strip on the home page with nothing on screen to say why, which is the only thing worth
    /// refusing at save time rather than discovering at render time.
    /// </remarks>
    [Theory]
    [InlineData("""{ "heading": "Picks" }""")]
    [InlineData("""{ "collectionSlug": "sale", "productIds": ["0192aaaa-0000-7000-8000-000000000001"] }""")]
    public void A_carousel_needs_exactly_one_source(string config)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        var result = BlockValidator.Validate(BlockType.ProductCarousel, Json(config), "blocks[0]", errors);

        Assert.Null(result);
        Assert.Contains("blocks[0].collectionSlug", errors.Keys);
    }

    /// <summary>A repeated block's children are validated one by one, and named by index.</summary>
    [Fact]
    public void Items_are_validated_individually()
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        BlockValidator.Validate(
            BlockType.Faq,
            Json("""
                {
                  "items": [
                    { "question": "Do you deliver?", "answer": "<p>Yes.</p>" },
                    { "question": "How long?" }
                  ]
                }
                """),
            "blocks[0]",
            errors);

        Assert.Contains("blocks[0].items[1].answer", errors.Keys);
        Assert.DoesNotContain("blocks[0].items[0].answer", errors.Keys);
    }

    /// <summary>Custom HTML is stored exactly as it was written.</summary>
    /// <remarks>
    /// Deliberately unsanitised: it is the escape hatch the custom-HTML permission and its flag exist
    /// to guard, and sanitising it would leave nothing that could carry an embed. The control is who
    /// may write one, not what one may contain.
    /// </remarks>
    [Fact]
    public void Custom_html_is_not_sanitised()
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        var result = BlockValidator.Validate(
            BlockType.CustomHtml,
            Json("""{ "html": "<iframe src=\"https://player.example/v/1\"></iframe>" }"""),
            "blocks[0]",
            errors);

        Assert.Empty(errors);
        Assert.NotNull(result);
        Assert.Contains("iframe", result.Config, StringComparison.Ordinal);
    }

    /// <summary>What a block points at is gathered while it is being checked.</summary>
    /// <remarks>
    /// Collected during validation rather than rediscovered at render time, and the two walks have to
    /// agree — <see cref="BlockReferenceReader"/> reads a stored block and must find the same set.
    /// </remarks>
    [Fact]
    public void References_are_gathered_and_read_back_the_same()
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        var config = Json("""
            {
              "heading": "Shop by room",
              "columns": "3",
              "items": [
                { "categoryId": "0192aaaa-0000-7000-8000-000000000001", "imageFileId": "0192bbbb-0000-7000-8000-000000000001" },
                { "categoryId": "0192aaaa-0000-7000-8000-000000000002" }
              ]
            }
            """);

        var validated = BlockValidator.Validate(BlockType.CategoryTiles, config, "blocks[0]", errors);

        Assert.Empty(errors);
        Assert.NotNull(validated);
        Assert.Equal(2, validated.References.CategoryIds.Count);
        Assert.Single(validated.References.MediaIds);

        var read = BlockReferenceReader.Read(BlockType.CategoryTiles, Json(validated.Config));

        Assert.Equal(validated.References.CategoryIds, read.CategoryIds);
        Assert.Equal(validated.References.MediaIds, read.MediaIds);
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
