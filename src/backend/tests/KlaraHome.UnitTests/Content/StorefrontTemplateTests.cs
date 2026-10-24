using System.Text.Json;
using KlaraHome.Contracts.Catalog;
using KlaraHome.Contracts.Media;
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
        var filler = NewFiller(Taxonomy([]), Products([]));
        var binder = new PageBlockBinder(Substitute.For<IFeatureFlags>());

        foreach (var template in StorefrontTemplateCatalog.All)
        {
            var bodies = await filler.FillAsync(template, TestContext.Current.CancellationToken);
            var bound = await binder.BindAsync(bodies, false, TestContext.Current.CancellationToken, allowIncomplete: true);

            Assert.True(bound.IsSuccess, template.Id);
            // Only banner grids (no picture to show) and capped carousels (nothing under the cap) are
            // dropped from an empty catalogue; everything else is kept as a draft.
            var dropped = template.Blocks.Count(block => block.Type == BlockType.BannerGrid || block.Fill?.MaxPrice is not null);

            Assert.Equal(template.Blocks.Count - dropped, bound.Value.Count);
            Assert.DoesNotContain(bound.Value, draft => draft.Type == BlockType.BannerGrid);
        }
    }

    /// <summary>Strict binding (publish and rollback) still demands the hero picture; draft mode is explicit.</summary>
    [Fact]
    public async Task Strict_binding_still_demands_the_hero_picture()
    {
        var filler = NewFiller(Taxonomy([]), Products([]));
        var binder = new PageBlockBinder(Substitute.For<IFeatureFlags>());
        var bodies = await filler.FillAsync(StorefrontTemplateCatalog.Find("loom")!, TestContext.Current.CancellationToken);

        var bound = await binder.BindAsync(bodies, false, TestContext.Current.CancellationToken);

        Assert.True(bound.IsFailure);
    }

    /// <summary>Limits, copy and arrangement the template review calls for.</summary>
    [Fact]
    public void Templates_ship_the_reviewed_arrangement()
    {
        static List<string> Types(string id)
            => [.. StorefrontTemplateCatalog.Find(id)!.Blocks.Select(block => block.Type.ToString())];

        static List<int> Limits(string id) => [.. StorefrontTemplateCatalog.Find(id)!.Blocks
            .Where(block => block.Type == BlockType.ProductCarousel)
            .Select(block => JsonDocument.Parse(block.ConfigJson).RootElement.GetProperty("limit").GetInt32())];

        Assert.Equal(["Hero", "CategoryTiles", "ProductCarousel", "Testimonial", "RichText"], Types("loom"));
        Assert.Equal(["Hero", "BannerGrid", "BannerGrid", "ProductCarousel", "Faq"], Types("atelier"));
        Assert.Equal(["CategoryTiles", "BannerGrid", "ProductCarousel", "ProductCarousel", "RichText"], Types("bazaar"));
        Assert.Equal(["RichText", "Hero", "ProductCarousel", "Testimonial"], Types("edit"));

        Assert.DoesNotContain("Hero", Types("bazaar"));
        Assert.Equal([6], Limits("loom"));
        Assert.Equal([4], Limits("atelier"));
        Assert.Equal([6, 6], Limits("bazaar"));
        Assert.Equal([6], Limits("edit"));

        var edit = StorefrontTemplateCatalog.Find("edit")!.Blocks.Single(block => block.Type == BlockType.Testimonial);

        Assert.Equal(3, JsonDocument.Parse(edit.ConfigJson).RootElement.GetProperty("items").GetArrayLength());

        static string AlignOf(string id) => JsonDocument
            .Parse(StorefrontTemplateCatalog.Find(id)!.Blocks.Single(block => block.Type == BlockType.RichText).ConfigJson)
            .RootElement.GetProperty("align").GetString()!;

        Assert.Equal("centre", AlignOf("loom"));
        Assert.Equal("start", AlignOf("bazaar"));
        Assert.Equal("start", AlignOf("edit"));
    }

    /// <summary>No em-dash or en-dash anywhere in the copy.</summary>
    [Fact]
    public void Template_copy_has_no_dashes_of_the_long_kind()
    {
        foreach (var template in StorefrontTemplateCatalog.All)
        {
            var text = string.Join(' ', template.Blocks.Select(block => block.ConfigJson))
                + template.Name + template.Suits + template.Description;

            Assert.DoesNotContain('—', text);
            Assert.DoesNotContain('–', text);
        }
    }

    /// <summary>The rich-text align field is optional and takes start or centre only.</summary>
    [Fact]
    public void Rich_text_align_is_an_optional_start_or_centre_choice()
    {
        var field = BlockCatalog.Describe(BlockType.RichText).Fields.Single(f => f.Name == "align");

        Assert.False(field.IsRequired);
        Assert.Equal(["start", "centre"], field.Choices);

        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var legacy = JsonDocument.Parse("""{ "body": "<p>Hi</p>" }""").RootElement.Clone();

        Assert.NotNull(BlockValidator.Validate(BlockType.RichText, legacy, "block", errors));
        Assert.Empty(errors);

        var bad = JsonDocument.Parse("""{ "body": "<p>Hi</p>", "align": "right" }""").RootElement.Clone();

        BlockValidator.Validate(BlockType.RichText, bad, "block", errors);

        Assert.NotEmpty(errors);
    }

    /// <summary>The hero takes the picture of the newest purchasable product that has one.</summary>
    [Fact]
    public async Task The_hero_takes_the_newest_purchasable_product_image()
    {
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var newestNoImage = Projection(start.AddDays(5), image: null);
        var newestUnbuyable = Projection(start.AddDays(4), purchasable: false, image: Guid.CreateVersion7());
        var wanted = Projection(start.AddDays(3), image: Guid.CreateVersion7());
        var older = Projection(start.AddDays(1), image: Guid.CreateVersion7());

        var filler = NewFiller(Taxonomy([]), Products([newestNoImage, newestUnbuyable, older, wanted]));
        var bodies = await filler.FillAsync(StorefrontTemplateCatalog.Find("loom")!, TestContext.Current.CancellationToken);

        var hero = bodies.Single(body => body.Type == nameof(BlockType.Hero));

        Assert.Equal(wanted.PrimaryImageFileId.ToString(), hero.Config.GetProperty("imageFileId").GetString());

        // With a picture, even strict binding of the hero now passes.
        var binder = new PageBlockBinder(Substitute.For<IFeatureFlags>());
        var bound = await binder.BindAsync([hero], false, TestContext.Current.CancellationToken);

        Assert.True(bound.IsSuccess);
    }

    /// <summary>Tiles keep a category's own picture; otherwise they override it with the first product picture.</summary>
    [Fact]
    public async Task Category_tiles_use_the_category_image_or_an_override_to_a_product_image()
    {
        var withImage = Category("Bedroom", "bedroom", parent: null, image: Guid.CreateVersion7());
        var overridden = Category("Kitchen", "kitchen", parent: null);
        var bare = Category("Outdoor", "outdoor", parent: null);

        var kitchenImage = Guid.CreateVersion7();
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var products = new[]
        {
            Projection(start.AddDays(1), category: bare, image: null),
            Projection(start.AddDays(2), category: overridden, image: Guid.CreateVersion7()),
            Projection(start.AddDays(3), category: overridden, image: kitchenImage),
            Projection(start.AddDays(4), category: withImage, image: Guid.CreateVersion7()),
        };

        var filler = NewFiller(Taxonomy([withImage, overridden, bare]), Products(products));
        var bodies = await filler.FillAsync(StorefrontTemplateCatalog.Find("bazaar")!, TestContext.Current.CancellationToken);
        var tiles = Items(bodies.Single(body => body.Type == nameof(BlockType.CategoryTiles)));

        Assert.Equal(3, tiles.Count);
        Assert.False(tiles[0].TryGetProperty("imageFileId", out _), "the category already has a picture");
        Assert.Equal(kitchenImage.ToString(), tiles[1].GetProperty("imageFileId").GetString());
        Assert.False(tiles[2].TryGetProperty("imageFileId", out _), "no product in the category has a picture");

        // The override is exactly what the composer reads back.
        var overrides = BlockReferenceReader.ReadCategoryImageOverrides(BlockType.CategoryTiles, JsonSerializer.SerializeToElement(
            new { items = tiles }));

        Assert.Equal(kitchenImage, overrides[overridden.Id]);
    }

    /// <summary>Tiles take up to six top-level active categories.</summary>
    [Fact]
    public async Task Tiles_take_six_top_level_categories()
    {
        var categories = new List<CategoryNode>();

        for (var index = 0; index < 8; index++)
        {
            categories.Add(Category($"Room {index:00}", $"room-{index:00}", parent: null));
        }

        categories.Add(Category("Child", "child", parent: categories[0].Id));

        var filler = NewFiller(Taxonomy(categories), Products([]));
        var bodies = await filler.FillAsync(StorefrontTemplateCatalog.Find("bazaar")!, TestContext.Current.CancellationToken);

        var tiles = Items(bodies.Single(body => body.Type == nameof(BlockType.CategoryTiles)));

        Assert.Equal(StorefrontTemplateFiller.MaxTiles, tiles.Count);
        Assert.DoesNotContain(tiles, tile => tile.GetProperty("categoryId").GetString() == categories[^1].Id.ToString());

        // No pictures anywhere: the banner grid is dropped, not shipped empty.
        Assert.DoesNotContain(bodies, body => body.Type == nameof(BlockType.BannerGrid));

        var binder = new PageBlockBinder(Substitute.For<IFeatureFlags>());
        var bound = await binder.BindAsync(bodies, false, TestContext.Current.CancellationToken, allowIncomplete: true);

        Assert.True(bound.IsSuccess);
    }

    /// <summary>A banner takes its category's second product picture, biggest category first; one without a picture is dropped.</summary>
    [Fact]
    public async Task Banners_take_the_second_product_image_and_are_dropped_without_one()
    {
        var big = Category("Big", "big", parent: null);
        var small = Category("Small", "small", parent: null);
        var noPicture = Category("Nothing", "nothing", parent: null);
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var bigFirst = Guid.CreateVersion7();
        var bigSecond = Guid.CreateVersion7();
        var smallImage = Guid.CreateVersion7();

        var products = new[]
        {
            Projection(start.AddDays(1), category: big, image: bigSecond),
            Projection(start.AddDays(2), category: big, image: bigFirst),
            Projection(start.AddDays(3), category: big, image: null),
            Projection(start.AddDays(4), category: small, image: smallImage),
            Projection(start.AddDays(5), category: noPicture, image: null),
        };

        var filler = NewFiller(Taxonomy([noPicture, small, big]), Products(products));
        var bodies = await filler.FillAsync(StorefrontTemplateCatalog.Find("atelier")!, TestContext.Current.CancellationToken);
        var banners = Items(bodies.First(body => body.Type == nameof(BlockType.BannerGrid)));

        // Newest first, the tile (if any) would show bigFirst; the banner shows the next one.
        Assert.Single(banners);
        Assert.Equal("Big", banners[0].GetProperty("caption").GetString());
        Assert.Equal(bigSecond.ToString(), banners[0].GetProperty("imageFileId").GetString());

        // Only one category with a picture is left for the pair, which needs two: it is dropped.
        Assert.Single(bodies, body => body.Type == nameof(BlockType.BannerGrid));
        Assert.DoesNotContain(
            bodies.Where(body => body.Type == nameof(BlockType.BannerGrid)).SelectMany(Items),
            banner => banner.GetProperty("caption").GetString() == "Nothing");

        var binder = new PageBlockBinder(Substitute.For<IFeatureFlags>());
        var bound = await binder.BindAsync(bodies, false, TestContext.Current.CancellationToken, allowIncomplete: true);

        Assert.True(bound.IsSuccess);
    }

    /// <summary>A banner prefers the category's own picture, then the demonstration one, over any product shot.</summary>
    [Fact]
    public async Task Banners_prefer_the_category_picture_then_the_demonstration_picture()
    {
        var own = Category("Alpha", "own", parent: null, image: Guid.CreateVersion7());
        var demo = Category("Beta", "demo", parent: null);
        var also = Category("Gamma", "also", parent: null);
        var demoImage = Guid.CreateVersion7();
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        var products = new[]
        {
            Projection(start.AddDays(1), category: own, image: Guid.CreateVersion7()),
            Projection(start.AddDays(2), category: own, image: Guid.CreateVersion7()),
            Projection(start.AddDays(3), category: demo, image: Guid.CreateVersion7()),
            Projection(start.AddDays(4), category: demo, image: Guid.CreateVersion7()),
            Projection(start.AddDays(5), category: also, image: Guid.CreateVersion7()),
        };

        var filler = NewFiller(
            Taxonomy([own, demo, also]),
            Products(products),
            Media(("demo-category-demo", demoImage), ("demo-category-also", Guid.CreateVersion7()), ("demo-category-own", Guid.CreateVersion7())));
        var bodies = await filler.FillAsync(StorefrontTemplateCatalog.Find("atelier")!, TestContext.Current.CancellationToken);
        var grids = bodies.Where(body => body.Type == nameof(BlockType.BannerGrid)).Select(Items).ToList();

        Assert.Single(grids[0]);
        Assert.Equal(own.ImageFileId.ToString(), grids[0][0].GetProperty("imageFileId").GetString());
        Assert.Equal(demoImage.ToString(), grids[1][0].GetProperty("imageFileId").GetString());
    }

    /// <summary>Descendant categories count towards their top-level category's banner.</summary>
    [Fact]
    public async Task A_banner_reaches_into_child_categories()
    {
        var parent = Category("Living", "living", parent: null);
        var child = Category("Sofas", "sofas", parent: parent.Id);
        var image = Guid.CreateVersion7();

        var product = Projection(DateTimeOffset.UtcNow, category: child, image: image, path: $"/{parent.Id}/{child.Id}/");

        var filler = NewFiller(Taxonomy([parent, child]), Products([product]));
        var bodies = await filler.FillAsync(StorefrontTemplateCatalog.Find("atelier")!, TestContext.Current.CancellationToken);
        var banners = Items(bodies.Single(body => body.Type == nameof(BlockType.BannerGrid)));

        // One banner left: a single banner, which the storefront draws full width. The category has a
        // single picture, so the "second" one is that one.
        Assert.Single(banners);
        Assert.Equal(image.ToString(), banners[0].GetProperty("imageFileId").GetString());
    }

    /// <summary>Bazaar shows tiles for the categories it has, so a banner never repeats one: with two categories it ships none.</summary>
    [Fact]
    public async Task Bazaar_with_two_categories_ships_tiles_and_no_banner_grid()
    {
        var a = Category("Alpha", "alpha", parent: null);
        var b = Category("Beta", "beta", parent: null);
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        var products = new[]
        {
            Projection(start.AddDays(1), category: a, image: Guid.CreateVersion7()),
            Projection(start.AddDays(2), category: a, image: Guid.CreateVersion7()),
            Projection(start.AddDays(3), category: b, image: Guid.CreateVersion7()),
            Projection(start.AddDays(4), category: b, image: Guid.CreateVersion7()),
        };

        var bodies = await NewFiller(Taxonomy([a, b]), Products(products))
            .FillAsync(StorefrontTemplateCatalog.Find("bazaar")!, TestContext.Current.CancellationToken);

        Assert.Equal(2, Items(bodies.Single(body => body.Type == nameof(BlockType.CategoryTiles))).Count);
        Assert.DoesNotContain(bodies, body => body.Type == nameof(BlockType.BannerGrid));
    }

    /// <summary>With more categories than tiles, Bazaar's banners are the categories no tile shows.</summary>
    [Fact]
    public async Task A_banner_never_repeats_a_tile_category()
    {
        var categories = Enumerable.Range(0, 9)
            .Select(index => Category($"Room {index:00}", $"room-{index:00}", parent: null))
            .ToList();
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var products = categories
            .SelectMany((category, index) => new[]
            {
                Projection(start.AddDays(index), category: category, image: Guid.CreateVersion7()),
                Projection(start.AddDays(index).AddHours(1), category: category, image: Guid.CreateVersion7()),
            })
            .ToList();

        var bodies = await NewFiller(Taxonomy(categories), Products(products))
            .FillAsync(StorefrontTemplateCatalog.Find("bazaar")!, TestContext.Current.CancellationToken);

        var tiled = Items(bodies.Single(body => body.Type == nameof(BlockType.CategoryTiles)))
            .Select(tile => tile.GetProperty("categoryId").GetString())
            .ToList();
        var bannered = bodies.Where(body => body.Type == nameof(BlockType.BannerGrid)).SelectMany(Items).ToList();

        Assert.Equal(StorefrontTemplateFiller.MaxTiles, tiled.Count);
        Assert.NotEmpty(bannered);
        Assert.All(
            bannered,
            banner => Assert.DoesNotContain(
                categories.Single(category => "/c/" + category.Slug == banner.GetProperty("href").GetString()).Id.ToString(),
                tiled));
    }

    /// <summary>A top-level category with no picture shows a picture a category below it carries (own, then demonstration) before any product shot.</summary>
    [Fact]
    public async Task A_top_level_category_borrows_a_picture_from_below_before_a_product_shot()
    {
        var parentId = Guid.CreateVersion7();
        var childId = Guid.CreateVersion7();
        var parent = new CategoryNode(parentId, null, "Soft Furnishings", "soft", $"/{parentId}/", 0, true, null, null);
        var child = new CategoryNode(childId, parentId, "Cushion Covers", "cushions", $"/{parentId}/{childId}/", 1, true, null, null);
        var demoImage = Guid.CreateVersion7();
        var product = Projection(DateTimeOffset.UtcNow, category: child, image: Guid.CreateVersion7(), path: child.Path);

        var filler = NewFiller(Taxonomy([parent, child]), Products([product]), Media(("demo-category-cushions", demoImage)));

        var atelier = await filler.FillAsync(StorefrontTemplateCatalog.Find("atelier")!, TestContext.Current.CancellationToken);
        var banner = Items(atelier.First(body => body.Type == nameof(BlockType.BannerGrid)))[0];

        Assert.Equal(demoImage.ToString(), banner.GetProperty("imageFileId").GetString());

        var bazaar = await filler.FillAsync(StorefrontTemplateCatalog.Find("bazaar")!, TestContext.Current.CancellationToken);
        var tile = Items(bazaar.Single(body => body.Type == nameof(BlockType.CategoryTiles)))[0];

        Assert.Equal(demoImage.ToString(), tile.GetProperty("imageFileId").GetString());
    }

    /// <summary>The hero keeps the picture the current home page already has, over the demonstration and product pictures.</summary>
    [Fact]
    public async Task The_hero_keeps_the_current_home_pages_picture()
    {
        var kept = Guid.CreateVersion7();
        var product = Projection(DateTimeOffset.UtcNow, image: Guid.CreateVersion7());
        var home = HomePage(
            (BlockType.RichText, new { body = "<p>Hi</p>" }),
            (BlockType.Hero, new { headline = "Mine", imageFileId = kept, align = "left" }));

        var filler = NewFiller(Taxonomy([]), Products([product]), Media(("demo-hero", Guid.CreateVersion7())));
        var bodies = await filler.FillAsync(StorefrontTemplateCatalog.Find("loom")!, TestContext.Current.CancellationToken, home);

        Assert.Equal(kept.ToString(), bodies.Single(body => body.Type == nameof(BlockType.Hero)).Config.GetProperty("imageFileId").GetString());
    }

    /// <summary>With no home page, the demonstration hero is used; with no demonstration media, the newest product picture.</summary>
    [Fact]
    public async Task The_hero_uses_the_demonstration_picture_when_there_is_no_home_page()
    {
        var demoHero = Guid.CreateVersion7();
        var productImage = Guid.CreateVersion7();
        var product = Projection(DateTimeOffset.UtcNow, image: productImage);
        var loom = StorefrontTemplateCatalog.Find("loom")!;

        var withDemo = await NewFiller(Taxonomy([]), Products([product]), Media(("demo-hero", demoHero)))
            .FillAsync(loom, TestContext.Current.CancellationToken);

        Assert.Equal(demoHero.ToString(), withDemo.Single(body => body.Type == nameof(BlockType.Hero)).Config.GetProperty("imageFileId").GetString());

        // A home page without a hero does not change that.
        var heroless = HomePage((BlockType.RichText, new { body = "<p>Hi</p>" }));
        var stillDemo = await NewFiller(Taxonomy([]), Products([product]), Media(("demo-hero", demoHero)))
            .FillAsync(loom, TestContext.Current.CancellationToken, heroless);

        Assert.Equal(demoHero.ToString(), stillDemo.Single(body => body.Type == nameof(BlockType.Hero)).Config.GetProperty("imageFileId").GetString());

        var withoutDemo = await NewFiller(Taxonomy([]), Products([product]))
            .FillAsync(loom, TestContext.Current.CancellationToken);

        Assert.Equal(productImage.ToString(), withoutDemo.Single(body => body.Type == nameof(BlockType.Hero)).Config.GetProperty("imageFileId").GetString());
    }

    /// <summary>Tile picture order: own, the current page's override, the demonstration picture, a product shot.</summary>
    [Fact]
    public async Task Tiles_prefer_the_current_override_then_the_demonstration_picture()
    {
        var own = Category("Own", "own", parent: null, image: Guid.CreateVersion7());
        var kept = Category("Kept", "kept", parent: null);
        var demo = Category("Demo", "demo", parent: null);
        var shot = Category("Shot", "shot", parent: null);
        var keptImage = Guid.CreateVersion7();
        var demoImage = Guid.CreateVersion7();
        var shotImage = Guid.CreateVersion7();
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        var products = new[]
        {
            Projection(start.AddDays(1), category: kept, image: Guid.CreateVersion7()),
            Projection(start.AddDays(2), category: demo, image: Guid.CreateVersion7()),
            Projection(start.AddDays(3), category: shot, image: shotImage),
        };

        var home = HomePage((
            BlockType.CategoryTiles,
            new { items = new object[] { new { categoryId = kept.Id, imageFileId = keptImage } } }));

        var filler = NewFiller(
            Taxonomy([own, kept, demo, shot]),
            Products(products),
            Media(("demo-category-kept", Guid.CreateVersion7()), ("demo-category-demo", demoImage)));
        var bodies = await filler.FillAsync(StorefrontTemplateCatalog.Find("bazaar")!, TestContext.Current.CancellationToken, home);
        var tiles = Items(bodies.Single(body => body.Type == nameof(BlockType.CategoryTiles)));

        // Alphabetical: Demo, Kept, Own, Shot.
        Assert.Equal(demoImage.ToString(), tiles[0].GetProperty("imageFileId").GetString());
        Assert.Equal(keptImage.ToString(), tiles[1].GetProperty("imageFileId").GetString());
        Assert.False(tiles[2].TryGetProperty("imageFileId", out _), "the category's own picture needs no override");
        Assert.Equal(shotImage.ToString(), tiles[3].GetProperty("imageFileId").GetString());
    }

    /// <summary>Atelier: biggest category alone, then a pair only when two more categories have a picture.</summary>
    [Fact]
    public async Task Atelier_shows_one_full_width_banner_then_a_pair_only_if_images_exist()
    {
        var a = Category("Alpha", "alpha", parent: null);
        var b = Category("Beta", "beta", parent: null);
        var c = Category("Gamma", "gamma", parent: null);
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var template = StorefrontTemplateCatalog.Find("atelier")!;

        // Three categories with pictures: single, then a pair.
        var three = new[]
        {
            Projection(start.AddDays(1), category: a, image: Guid.CreateVersion7()),
            Projection(start.AddDays(2), category: a, image: Guid.CreateVersion7()),
            Projection(start.AddDays(3), category: b, image: Guid.CreateVersion7()),
            Projection(start.AddDays(4), category: c, image: Guid.CreateVersion7()),
        };

        var bodies = await NewFiller(Taxonomy([a, b, c]), Products(three))
            .FillAsync(template, TestContext.Current.CancellationToken);
        var grids = bodies.Where(body => body.Type == nameof(BlockType.BannerGrid)).Select(Items).ToList();

        Assert.Equal(2, grids.Count);
        Assert.Single(grids[0]);
        Assert.Equal("Alpha", grids[0][0].GetProperty("caption").GetString());
        Assert.Equal(2, grids[1].Count);
        Assert.Equal(["Beta", "Gamma"], grids[1].Select(item => item.GetProperty("caption").GetString()));

        // Only two with pictures: the single stays, the pair is dropped rather than made a second single.
        var two = three.Take(3).ToList();

        bodies = await NewFiller(Taxonomy([a, b, c]), Products(two))
            .FillAsync(template, TestContext.Current.CancellationToken);
        grids = [.. bodies.Where(body => body.Type == nameof(BlockType.BannerGrid)).Select(Items)];

        Assert.Single(grids);
        Assert.Single(grids[0]);
    }

    /// <summary>Each carousel holds at most its limit; two carousels do not share a set when there are enough products.</summary>
    [Fact]
    public async Task Carousels_honour_their_limit_and_do_not_repeat_each_other()
    {
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var projections = Enumerable.Range(0, 14)
            .Select(index => Projection(start.AddDays(index), purchasable: index != 13, price: 500m))
            .ToList();

        foreach (var (id, limit) in new[] { ("loom", 6), ("atelier", 4), ("edit", 6) })
        {
            var bodies = await NewFiller(Taxonomy([]), Products(projections))
                .FillAsync(StorefrontTemplateCatalog.Find(id)!, TestContext.Current.CancellationToken);

            var ids = bodies.Single(body => body.Type == nameof(BlockType.ProductCarousel))
                .Config.GetProperty("productIds").EnumerateArray().Select(value => value.GetString()).ToList();

            Assert.Equal(limit, ids.Count);
            // Newest purchasable first: index 13 is not purchasable, so index 12 leads.
            Assert.Equal(projections[12].ProductId.ToString(), ids[0]);
        }

        var bazaar = await NewFiller(Taxonomy([]), Products(projections))
            .FillAsync(StorefrontTemplateCatalog.Find("bazaar")!, TestContext.Current.CancellationToken);

        var rails = bazaar.Where(body => body.Type == nameof(BlockType.ProductCarousel))
            .Select(body => body.Config.GetProperty("productIds").EnumerateArray().Select(value => value.GetString()!).ToList())
            .ToList();

        Assert.Equal(2, rails.Count);
        Assert.All(rails, rail => Assert.Equal(6, rail.Count));
        Assert.Empty(rails[0].Intersect(rails[1]));

        var binder = new PageBlockBinder(Substitute.For<IFeatureFlags>());
        var bound = await binder.BindAsync(bazaar, false, TestContext.Current.CancellationToken, allowIncomplete: true);

        Assert.True(bound.IsSuccess);
    }

    /// <summary>"Under 999" holds only products priced at or under 999, newest first.</summary>
    [Fact]
    public async Task Under_999_filters_by_price_newest_first()
    {
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var prices = new[] { 500m, 999m, 1000m, 1500m, 999.01m, 300m, 998m, 5000m, 700m, 2500m };
        var projections = prices
            .Select((price, index) => Projection(start.AddDays(index), price: price))
            .ToList();

        var bodies = await NewFiller(Taxonomy([]), Products(projections))
            .FillAsync(StorefrontTemplateCatalog.Find("bazaar")!, TestContext.Current.CancellationToken);

        var carousels = bodies.Where(body => body.Type == nameof(BlockType.ProductCarousel)).ToList();

        Assert.Equal(2, carousels.Count);

        var under = carousels[1].Config.GetProperty("productIds").EnumerateArray().Select(value => value.GetString()!).ToList();

        // 700, 998, 300, 999, 500 qualify (five), newest first. Trending already showed the six newest
        // overall, which includes 700, 998 and 300, so the unseen qualifying ones (999, 500) come first.
        Assert.Equal(5, under.Count);
        Guid[] expected = [projections[1].ProductId, projections[0].ProductId, projections[8].ProductId, projections[6].ProductId, projections[5].ProductId];

        Assert.Equal(expected.Select(id => id.ToString()), under);

        var qualifying = projections.Where(p => p.SellingPrice <= 999m).Select(p => p.ProductId.ToString()).ToHashSet();

        Assert.All(under, id => Assert.Contains(id, qualifying));
    }

    /// <summary>"Under 999" is dropped when fewer than three products qualify.</summary>
    [Fact]
    public async Task Under_999_is_dropped_with_fewer_than_three_qualifying_products()
    {
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var projections = new[]
        {
            Projection(start.AddDays(1), price: 400m),
            Projection(start.AddDays(2), price: 800m),
            Projection(start.AddDays(3), price: 1200m),
            Projection(start.AddDays(4), price: 2000m),
        };

        var bodies = await NewFiller(Taxonomy([]), Products(projections))
            .FillAsync(StorefrontTemplateCatalog.Find("bazaar")!, TestContext.Current.CancellationToken);

        var carousels = bodies.Where(body => body.Type == nameof(BlockType.ProductCarousel)).ToList();

        Assert.Single(carousels);
        Assert.Equal("Trending now", carousels[0].Config.GetProperty("heading").GetString());
        Assert.DoesNotContain(bodies, body => body.Type == nameof(BlockType.ProductCarousel)
            && body.Config.GetProperty("heading").GetString() == "Under ₹999");
    }

    [Fact]
    public async Task The_whole_catalogue_is_read_so_the_newest_product_on_a_later_page_wins_the_hero()
    {
        var now = DateTimeOffset.UtcNow;
        var oldShot = Guid.CreateVersion7();
        var newShot = Guid.CreateVersion7();
        var older = Projection(now.AddDays(-30), image: oldShot);
        var newest = Projection(now, image: newShot);

        // The walk is in variant-id order, oldest first: the newest product is on the second page.
        var source = Substitute.For<IProductProjectionSource>();
#pragma warning disable CA2012
        source.EnumerateAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<ProductProjectionPage>(new ProductProjectionPage([older.VariantId], [older], older.VariantId)));
        source.EnumerateAsync(older.VariantId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<ProductProjectionPage>(new ProductProjectionPage([newest.VariantId], [newest], null)));
#pragma warning restore CA2012

        var bodies = await NewFiller(Taxonomy([]), source)
            .FillAsync(StorefrontTemplateCatalog.Find("loom")!, TestContext.Current.CancellationToken);

        var hero = bodies.Single(body => body.Type == nameof(BlockType.Hero));

        Assert.Equal(newShot, hero.Config.GetProperty("imageFileId").GetGuid());
    }

    private static StorefrontTemplateFiller NewFiller(
        ICatalogTaxonomy taxonomy,
        IProductProjectionSource products,
        IDemoMediaCatalogue? media = null)
        => new(taxonomy, products, media ?? Media());

    private static IDemoMediaCatalogue Media(params (string Key, Guid Id)[] files)
    {
        var catalogue = Substitute.For<IDemoMediaCatalogue>();

        catalogue.ListAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyDictionary<string, Guid>>(
                files.ToDictionary(file => file.Key, file => file.Id, StringComparer.Ordinal)));

        return catalogue;
    }

    /// <summary>A home page in memory with the given blocks, as the apply handler would load it.</summary>
    private static ContentPage HomePage(params (BlockType Type, object Config)[] blocks)
    {
        var now = DateTimeOffset.UtcNow;
        var page = ContentPage.Create("home", PageType.Home, "Home");

        page.Describe("home", "Home", null, new SeoMetadata(), null, null, [], now);
        page.SyncBlocks(
            [.. blocks.Select(block => new BlockDraft(
                null,
                block.Type,
                JsonSerializer.Serialize(block.Config),
                IsVisible: true,
                StartsAt: null,
                EndsAt: null))],
            now);

        return page;
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

    private static CategoryNode Category(string name, string slug, Guid? parent, Guid? image = null)
        => new(Guid.CreateVersion7(), parent, name, slug, "/" + slug, parent is null ? 0 : 1, true, image, null);

    private static ProductProjection Projection(
        DateTimeOffset publishedAt,
        bool purchasable = true,
        decimal price = 900m,
        Guid? image = null,
        CategoryNode? category = null,
        string? path = null)
    {
        var categoryId = category?.Id ?? Guid.CreateVersion7();

        return new ProductProjection(
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
            categoryId,
            category?.Name ?? "Category",
            category?.Slug ?? "category",
            path ?? $"/{categoryId}/",
            null,
            null,
            null,
            purchasable,
            true,
            price + 100m,
            price,
            "INR",
            null,
            0,
            true,
            true,
            image,
            publishedAt,
            []);
    }
}
