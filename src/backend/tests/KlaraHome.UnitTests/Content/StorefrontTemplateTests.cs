using System.Text.Json;
using KlaraHome.Contracts.Catalog;
using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Content.Application;
using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Infrastructure.Blocks;
using NSubstitute;

namespace KlaraHome.UnitTests.Content;

/// <summary>
/// The storefront designer's template catalogue and the filler that makes a template alive.
/// </summary>
/// <remarks>
/// A template is data that has to stay valid against <see cref="BlockCatalog"/>, which is the thing a
/// future edit to either would silently break: an apply that fails at four o'clock because somebody
/// renamed a field. These tests are that fence.
/// </remarks>
public sealed class StorefrontTemplateTests
{
    private static readonly string[] KnownPalettes =
        ["sand", "terracotta", "berry", "emerald", "slate", "ocean", "coral", "indigo", "violet"];

    /// <summary>Exactly the four templates the designer promises, in order.</summary>
    [Fact]
    public void The_catalogue_holds_the_four_templates()
    {
        Assert.Equal(
            ["loom", "atelier", "bazaar", "edit"],
            StorefrontTemplateCatalog.All.Select(template => template.Id));
    }

    /// <summary>Header, footer and palettes are all members of the sets the storefront and the themes know.</summary>
    [Fact]
    public void Every_template_names_known_styles_and_three_or_four_palettes()
    {
        foreach (var template in StorefrontTemplateCatalog.All)
        {
            Assert.Contains(template.HeaderStyle, StorefrontTemplateCatalog.HeaderStyles);
            Assert.Contains(template.FooterStyle, StorefrontTemplateCatalog.FooterStyles);
            Assert.InRange(template.PaletteIds.Count, 3, 4);
            Assert.All(template.PaletteIds, id => Assert.Contains(id, KnownPalettes));
            Assert.Equal(template.PaletteIds.Count, template.PaletteIds.Distinct().Count());
        }
    }

    /// <summary>Each default config is valid against the block catalogue, with only the known fill-ins missing.</summary>
    /// <remarks>
    /// In draft mode there are no errors at all. In strict mode the only complaints allowed are the
    /// three things a template cannot know: the hero picture, a banner picture, and a carousel's
    /// products. Anything else is a typo in the template.
    /// </remarks>
    [Fact]
    public void Every_default_block_validates_against_the_block_catalogue()
    {
        foreach (var template in StorefrontTemplateCatalog.All)
        {
            for (var index = 0; index < template.Blocks.Count; index++)
            {
                var block = template.Blocks[index];
                var config = JsonDocument.Parse(block.ConfigJson).RootElement.Clone();
                var path = $"{template.Id}[{index}]";

                var draftErrors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                var draft = BlockValidator.Validate(block.Type, config, path, draftErrors, allowIncomplete: true);

                Assert.Empty(draftErrors);
                Assert.NotNull(draft);

                var strictErrors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                BlockValidator.Validate(block.Type, config, path, strictErrors);

                Assert.All(
                    strictErrors.Keys,
                    key => Assert.True(
                        key.EndsWith(".imageFileId", StringComparison.Ordinal)
                        || key.EndsWith(".collectionSlug", StringComparison.Ordinal),
                        $"{key} should not need filling in"));
            }
        }
    }

    /// <summary>Blocks that carry no reference fields are valid even in strict mode.</summary>
    [Fact]
    public void Text_blocks_are_strictly_valid_as_they_stand()
    {
        var textBlocks = StorefrontTemplateCatalog.All
            .SelectMany(template => template.Blocks)
            .Where(block => block.Type is BlockType.RichText or BlockType.Faq or BlockType.Testimonial)
            .ToList();

        Assert.NotEmpty(textBlocks);

        foreach (var block in textBlocks)
        {
            var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var config = JsonDocument.Parse(block.ConfigJson).RootElement.Clone();

            Assert.NotNull(BlockValidator.Validate(block.Type, config, "block", errors));
            Assert.Empty(errors);
        }
    }

    /// <summary>The hero carries full-width versus split in <c>align</c>, the only layout field it has.</summary>
    [Fact]
    public void Heroes_use_align_for_full_width_and_split()
    {
        static string AlignOf(string id) => JsonDocument
            .Parse(StorefrontTemplateCatalog.Find(id)!.Blocks.Single(block => block.Type == BlockType.Hero).ConfigJson)
            .RootElement.GetProperty("align").GetString()!;

        Assert.Equal(StorefrontTemplateCatalog.HeroFullWidthAlign, AlignOf("loom"));
        Assert.Equal(StorefrontTemplateCatalog.HeroSplitAlign, AlignOf("atelier"));
        Assert.Equal(StorefrontTemplateCatalog.HeroSplitAlign, AlignOf("edit"));
        Assert.Contains(StorefrontTemplateCatalog.HeroFullWidthAlign, BlockCatalog.Describe(BlockType.Hero)
            .Fields.Single(field => field.Name == "align").Choices!);
    }

    /// <summary>An empty catalogue leaves reference fields empty, and the result still binds.</summary>
    [Fact]
    public async Task An_empty_catalogue_still_binds()
    {
        var filler = new StorefrontTemplateFiller(Taxonomy([]), Products([]));
        var binder = new PageBlockBinder(Substitute.For<IFeatureFlags>());

        foreach (var template in StorefrontTemplateCatalog.All)
        {
            var bodies = await filler.FillAsync(template, TestContext.Current.CancellationToken);
            var bound = await binder.BindAsync(bodies, false, TestContext.Current.CancellationToken, allowIncomplete: true);

            Assert.True(bound.IsSuccess, template.Id);
            Assert.Equal(template.Blocks.Count, bound.Value.Count);
        }
    }

    /// <summary>Strict binding (publish and rollback) still demands the hero picture; draft mode is explicit.</summary>
    [Fact]
    public async Task Strict_binding_still_demands_the_hero_picture()
    {
        var filler = new StorefrontTemplateFiller(Taxonomy([]), Products([]));
        var binder = new PageBlockBinder(Substitute.For<IFeatureFlags>());
        var bodies = await filler.FillAsync(StorefrontTemplateCatalog.Find("loom")!, TestContext.Current.CancellationToken);

        var bound = await binder.BindAsync(bodies, false, TestContext.Current.CancellationToken);

        Assert.True(bound.IsFailure);
    }

    /// <summary>Tiles take up to six top-level active categories; the banner grid links to the first two.</summary>
    [Fact]
    public async Task Categories_fill_the_tiles_and_the_banner_links()
    {
        var categories = new List<CategoryNode>();

        for (var index = 0; index < 8; index++)
        {
            categories.Add(Category($"Room {index:00}", $"room-{index:00}", parent: null));
        }

        categories.Add(Category("Child", "child", parent: categories[0].Id));

        var filler = new StorefrontTemplateFiller(Taxonomy(categories), Products([]));
        var bodies = await filler.FillAsync(StorefrontTemplateCatalog.Find("bazaar")!, TestContext.Current.CancellationToken);

        var tiles = Items(bodies.Single(body => body.Type == nameof(BlockType.CategoryTiles)));
        var banners = Items(bodies.Single(body => body.Type == nameof(BlockType.BannerGrid)));

        Assert.Equal(StorefrontTemplateFiller.MaxTiles, tiles.Count);
        Assert.DoesNotContain(tiles, tile => tile.GetProperty("categoryId").GetString() == categories[^1].Id.ToString());

        Assert.Equal(2, banners.Count);
        Assert.Equal("/c/room-00", banners[0].GetProperty("href").GetString());
        Assert.Equal("Room 01", banners[1].GetProperty("caption").GetString());
        Assert.False(banners[0].TryGetProperty("imageFileId", out _));

        var binder = new PageBlockBinder(Substitute.For<IFeatureFlags>());
        var bound = await binder.BindAsync(bodies, false, TestContext.Current.CancellationToken, allowIncomplete: true);

        Assert.True(bound.IsSuccess);
    }

    /// <summary>Carousels get the newest purchasable products, at most eight, each carousel the same set.</summary>
    [Fact]
    public async Task Recent_products_fill_every_carousel()
    {
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var projections = Enumerable.Range(0, 12)
            .Select(index => Projection(start.AddDays(index), purchasable: index != 11))
            .ToList();

        var filler = new StorefrontTemplateFiller(Taxonomy([]), Products(projections));
        var bodies = await filler.FillAsync(StorefrontTemplateCatalog.Find("bazaar")!, TestContext.Current.CancellationToken);

        var carousels = bodies.Where(body => body.Type == nameof(BlockType.ProductCarousel)).ToList();

        Assert.Equal(2, carousels.Count);

        foreach (var carousel in carousels)
        {
            var ids = carousel.Config.GetProperty("productIds").EnumerateArray().Select(id => id.GetString()).ToList();

            Assert.Equal(StorefrontTemplateFiller.MaxProducts, ids.Count);
            // Newest purchasable first: index 11 is not purchasable, so index 10 leads.
            Assert.Equal(projections[10].ProductId.ToString(), ids[0]);
        }

        var binder = new PageBlockBinder(Substitute.For<IFeatureFlags>());
        var bound = await binder.BindAsync(bodies, false, TestContext.Current.CancellationToken, allowIncomplete: true);

        Assert.True(bound.IsSuccess);
        Assert.All(bound.Value.Where(draft => draft.Type == BlockType.ProductCarousel),
            draft => Assert.Contains("productIds", draft.Config, StringComparison.Ordinal));
    }

    private static List<JsonElement> Items(BlockBody body)
        => [.. body.Config.GetProperty("items").EnumerateArray()];

    // NSubstitute configures a ValueTask-returning member by calling it; that is the API, not a leak.
#pragma warning disable CA2012
    private static ICatalogTaxonomy Taxonomy(IReadOnlyList<CategoryNode> nodes)
    {
        var taxonomy = Substitute.For<ICatalogTaxonomy>();

        taxonomy.ListCategoriesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<IReadOnlyList<CategoryNode>>(nodes));

        return taxonomy;
    }

    private static IProductProjectionSource Products(IReadOnlyList<ProductProjection> items)
    {
        var source = Substitute.For<IProductProjectionSource>();

        source.EnumerateAsync(Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<ProductProjectionPage>(
                new ProductProjectionPage([.. items.Select(item => item.VariantId)], items, null)));

        return source;
    }

#pragma warning restore CA2012

    private static CategoryNode Category(string name, string slug, Guid? parent)
        => new(Guid.CreateVersion7(), parent, name, slug, "/" + slug, parent is null ? 0 : 1, true, null, null);

    private static ProductProjection Projection(DateTimeOffset publishedAt, bool purchasable)
        => new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "Vendor",
            "vendor",
            null,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "SKU",
            "Product",
            "Variant",
            "product",
            null,
            Guid.CreateVersion7(),
            "Category",
            "category",
            "category",
            null,
            null,
            null,
            purchasable,
            true,
            1000m,
            900m,
            "INR",
            null,
            0,
            true,
            true,
            null,
            publishedAt,
            []);
}
