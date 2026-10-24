using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.IntegrationTests.Commerce;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Content;

/// <summary>
/// The storefront designer's endpoints against a real database: what a preview must not write, and
/// what an apply must.
/// </summary>
/// <remarks>
/// One test per behaviour that needs Postgres to be true: apply creating then replacing (the page
/// identity survives, the blocks do not), a published page staying published, and both previews
/// leaving no trace.
/// </remarks>
public sealed class StorefrontDesignerTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    private const string Admin = "/api/v1/admin";

    /// <summary>The catalogue lists the four templates with their styles and palettes.</summary>
    [Fact]
    public async Task The_templates_are_listed()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var list = await ReadAsync(await admin.GetAsync($"{Admin}/storefront-templates", Cancellation));

        Assert.Equal(["loom", "atelier", "bazaar", "edit"], list.EnumerateArray().Select(t => t.GetProperty("id").GetString()));

        var loom = list.EnumerateArray().First();

        Assert.Equal("centred", loom.GetProperty("headerStyle").GetString());
        Assert.Equal("columns", loom.GetProperty("footerStyle").GetString());
        Assert.Equal(4, loom.GetProperty("paletteIds").GetArrayLength());
    }

    /// <summary>An unknown template is a 404 on both the preview and the apply.</summary>
    [Fact]
    public async Task An_unknown_template_is_not_found()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        await RefusedAsync(
            await admin.GetAsync($"{Admin}/storefront-templates/nope/preview", Cancellation),
            HttpStatusCode.NotFound);

        await RefusedAsync(
            await admin.PostAsync($"{Admin}/storefront-templates/nope/apply", content: null, Cancellation),
            HttpStatusCode.NotFound);
    }

    /// <summary>Preview persists nothing; apply creates the home page; a second apply replaces its blocks; a published page stays published.</summary>
    [Fact]
    public async Task Apply_creates_the_home_page_then_replaces_its_blocks_and_preview_writes_nothing()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        // The database is shared with the rest of the suite, so another test may already have
        // applied a template: count the home pages rather than assume there are none.
        var homes = await ReadAsync(await admin.GetAsync($"{Admin}/pages?type=Home", Cancellation));
        var before = homes.GetProperty("items").GetArrayLength();

        Assert.InRange(before, 0, 1);

        var preview = await ReadAsync(await admin.GetAsync($"{Admin}/storefront-templates/loom/preview", Cancellation));

        Assert.Equal(5, preview.GetProperty("blocks").GetArrayLength());
        Assert.Equal("Hero", preview.GetProperty("blocks")[0].GetProperty("type").GetString());

        // The preview was not persisted: the home page count is unchanged.
        homes = await ReadAsync(await admin.GetAsync($"{Admin}/pages?type=Home", Cancellation));

        Assert.Equal(before, homes.GetProperty("items").GetArrayLength());

        var created = await ReadAsync(await admin.PostAsync($"{Admin}/storefront-templates/loom/apply", content: null, Cancellation));

        Assert.Equal("home", created.GetProperty("slug").GetString());
        Assert.Equal("Home", created.GetProperty("type").GetString());
        Assert.Equal(5, created.GetProperty("blocks").GetArrayLength());

        if (before == 0)
        {
            Assert.Equal("Draft", created.GetProperty("status").GetString());
        }

        var id = created.GetProperty("id").GetGuid();

        var replaced = await ReadAsync(await admin.PostAsync($"{Admin}/storefront-templates/atelier/apply", content: null, Cancellation));

        Assert.Equal(id, replaced.GetProperty("id").GetGuid());
        // Banner grids without a picture to show are dropped, so the count depends on the catalogue
        // the shared database happens to hold: hero, carousel and FAQ always, up to two banner grids.
        Assert.InRange(replaced.GetProperty("blocks").GetArrayLength(), 3, 5);
        Assert.Equal("Hero", replaced.GetProperty("blocks")[0].GetProperty("type").GetString());
        Assert.Equal("left", replaced.GetProperty("blocks")[0].GetProperty("config").GetProperty("align").GetString());

        var reread = await ReadAsync(await admin.GetAsync($"{Admin}/pages/{id}", Cancellation));

        Assert.Equal(replaced.GetProperty("blocks").GetArrayLength(), reread.GetProperty("blocks").GetArrayLength());

        homes = await ReadAsync(await admin.GetAsync($"{Admin}/pages?type=Home", Cancellation));

        Assert.Equal(1, homes.GetProperty("items").GetArrayLength());

        // Going live is strict: an applied hero without a picture is refused, naming it. Whether it
        // has one depends on the pictures other tests left in the shared catalogue.
        if (!replaced.GetProperty("blocks")[0].GetProperty("config").TryGetProperty("imageFileId", out _))
        {
            var refusedPublish = await RefusedAsync(
                await admin.PostAsJsonAsync($"{Admin}/pages/{id}/transition", new { status = "Published", note = "test" }, Cancellation),
                HttpStatusCode.UnprocessableEntity,
                "CONTENT_INVALID_BLOCK");

            Assert.True(refusedPublish.GetProperty("errors").TryGetProperty("blocks[0].imageFileId", out _));
        }

        // Make it publishable (text only), publish, then apply again: it stays published.
        await ReadAsync(await admin.PutAsJsonAsync(
            $"{Admin}/pages/{id}",
            new
            {
                slug = "home",
                title = "Home",
                blocks = new object[] { new { type = "RichText", config = new { body = "<p>Welcome.</p>" }, isVisible = true } },
            },
            Cancellation));

        var published = await ReadAsync(await admin.PostAsJsonAsync(
            $"{Admin}/pages/{id}/transition",
            new { status = "Published", note = "test" },
            Cancellation));

        Assert.Equal("Published", published.GetProperty("status").GetString());

        var again = await ReadAsync(await admin.PostAsync($"{Admin}/storefront-templates/bazaar/apply", content: null, Cancellation));

        Assert.Equal(id, again.GetProperty("id").GetGuid());
        Assert.Equal("Published", again.GetProperty("status").GetString());
        // Bazaar: tiles, carousel and rich text always; the banner pair and "Under 999" depend on the catalogue.
        Assert.InRange(again.GetProperty("blocks").GetArrayLength(), 3, 5);
        Assert.NotEqual("Hero", again.GetProperty("blocks")[0].GetProperty("type").GetString());
    }

    /// <summary>With real products, the previews carry pictures, honour limits and respect the price cap.</summary>
    [Fact]
    public async Task Preview_fills_pictures_limits_and_the_under_999_rail_from_the_catalogue()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var search = new SearchScenario(admin, Factory, Database, Cancellation);
        var catalogue = search.Catalog;
        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await search.DefaultSellerAsync();

        // Three products with a picture, priced under 999, so "Under 999" has its minimum.
        for (var index = 0; index < 3; index++)
        {
            var product = await catalogue.DraftAsync(taxonomy);
            await ReadAsync(await catalogue.SetProductMediaAsync(product.Id, await catalogue.ImageAsync($"cover-{index}.png")));
            await catalogue.ActivateVariantAsync(product.VariantId);
            await catalogue.PublishAsync(product.Id);
            await catalogue.OfferAsync(seller.Id, product.VariantId, 500m);
        }

        var loom = await ReadAsync(await admin.GetAsync($"{Admin}/storefront-templates/loom/preview", Cancellation));
        var hero = loom.GetProperty("blocks")[0];

        Assert.Equal("Hero", hero.GetProperty("type").GetString());
        Assert.True(hero.GetProperty("config").TryGetProperty("imageFileId", out var heroImage));
        Assert.NotEqual(Guid.Empty, heroImage.GetGuid());

        var loomCarousel = loom.GetProperty("blocks").EnumerateArray().Single(b => b.GetProperty("type").GetString() == "ProductCarousel");

        Assert.InRange(loomCarousel.GetProperty("products").GetArrayLength(), 3, 6);

        var atelier = await ReadAsync(await admin.GetAsync($"{Admin}/storefront-templates/atelier/preview", Cancellation));
        var atelierBlocks = atelier.GetProperty("blocks").EnumerateArray().ToList();

        Assert.InRange(atelierBlocks.Single(b => b.GetProperty("type").GetString() == "ProductCarousel").GetProperty("products").GetArrayLength(), 3, 4);

        var banners = atelierBlocks.Where(b => b.GetProperty("type").GetString() == "BannerGrid").ToList();

        Assert.NotEmpty(banners);
        Assert.Equal(1, banners[0].GetProperty("config").GetProperty("items").GetArrayLength());
        Assert.All(
            banners.SelectMany(b => b.GetProperty("config").GetProperty("items").EnumerateArray()),
            item => Assert.True(item.TryGetProperty("imageFileId", out _), "a banner always has a picture"));

        var bazaar = await ReadAsync(await admin.GetAsync($"{Admin}/storefront-templates/bazaar/preview", Cancellation));
        var bazaarBlocks = bazaar.GetProperty("blocks").EnumerateArray().ToList();

        Assert.Equal("CategoryTiles", bazaarBlocks[0].GetProperty("type").GetString());
        Assert.DoesNotContain(bazaarBlocks, b => b.GetProperty("type").GetString() == "Hero");

        var rails = bazaarBlocks.Where(b => b.GetProperty("type").GetString() == "ProductCarousel").ToList();

        Assert.Equal(2, rails.Count);
        Assert.All(rails, rail => Assert.InRange(rail.GetProperty("config").GetProperty("productIds").GetArrayLength(), 3, 6));
        Assert.All(
            rails[1].GetProperty("products").EnumerateArray(),
            card => Assert.True(card.GetProperty("price").GetDecimal() <= 999m));
    }

    /// <summary>Applying a template keeps the picture the current home page's hero already has, rather than a product shot.</summary>
    [Fact]
    public async Task Apply_keeps_the_existing_home_pages_hero_picture()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var search = new SearchScenario(admin, Factory, Database, Cancellation);
        var catalogue = search.Catalog;
        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await search.DefaultSellerAsync();
        var product = await catalogue.DraftAsync(taxonomy);

        await ReadAsync(await catalogue.SetProductMediaAsync(product.Id, await catalogue.ImageAsync("shot.png")));
        await catalogue.ActivateVariantAsync(product.VariantId);
        await catalogue.PublishAsync(product.Id);
        await catalogue.OfferAsync(seller.Id, product.VariantId, 500m);

        // No home page yet and no demonstration media in this database: the product picture is used.
        var first = await ReadAsync(await admin.PostAsync($"{Admin}/storefront-templates/loom/apply", content: null, Cancellation));
        var productShot = first.GetProperty("blocks")[0].GetProperty("config").GetProperty("imageFileId").GetGuid();
        var id = first.GetProperty("id").GetGuid();

        // The merchant chooses their own hero picture.
        var chosen = await catalogue.ImageAsync("my-hero.png");

        await ReadAsync(await admin.PutAsJsonAsync(
            $"{Admin}/pages/{id}",
            new
            {
                slug = "home",
                title = "Home",
                blocks = new object[]
                {
                    new
                    {
                        type = "Hero",
                        config = new { headline = "Mine", imageFileId = chosen, align = "left" },
                        isVisible = true,
                    },
                },
            },
            Cancellation));

        Assert.NotEqual(productShot, chosen);

        // Both the preview and the apply keep it, whichever template follows.
        var preview = await ReadAsync(await admin.GetAsync($"{Admin}/storefront-templates/atelier/preview", Cancellation));

        Assert.Equal(chosen, preview.GetProperty("blocks")[0].GetProperty("config").GetProperty("imageFileId").GetGuid());

        var applied = await ReadAsync(await admin.PostAsync($"{Admin}/storefront-templates/edit/apply", content: null, Cancellation));
        var hero = applied.GetProperty("blocks").EnumerateArray().Single(b => b.GetProperty("type").GetString() == "Hero");

        Assert.Equal(chosen, hero.GetProperty("config").GetProperty("imageFileId").GetGuid());
    }

    /// <summary>The newest product having no picture neither breaks the preview nor the apply.</summary>
    /// <remarks>
    /// The projection used to report a missing picture as <see cref="Guid.Empty"/>, which the filler
    /// took for a picture and the validator refused, so the whole template answered 422.
    /// </remarks>
    [Fact]
    public async Task A_product_without_a_picture_does_not_break_the_preview_or_the_apply()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var search = new SearchScenario(admin, Factory, Database, Cancellation);
        var catalogue = search.Catalog;
        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await search.DefaultSellerAsync();
        var product = await catalogue.DraftAsync(taxonomy);

        await catalogue.ActivateVariantAsync(product.VariantId);
        await catalogue.PublishAsync(product.Id);
        await catalogue.OfferAsync(seller.Id, product.VariantId, 500m);

        foreach (var template in new[] { "loom", "atelier", "bazaar", "edit" })
        {
            var preview = await ReadAsync(await admin.GetAsync($"{Admin}/storefront-templates/{template}/preview", Cancellation));

            AssertNoEmptyPicture(preview);
        }

        AssertNoEmptyPicture(await ReadAsync(await admin.PostAsync($"{Admin}/storefront-templates/loom/apply", content: null, Cancellation)));
    }

    /// <summary>No block, and no item inside one, points at <see cref="Guid.Empty"/> as its picture (null is "none yet").</summary>
    private static void AssertNoEmptyPicture(JsonElement page)
    {
        foreach (var block in page.GetProperty("blocks").EnumerateArray())
        {
            var config = block.GetProperty("config");

            AssertPicture(config);

            if (config.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    AssertPicture(item);
                }
            }
        }

        static void AssertPicture(JsonElement owner)
        {
            if (owner.TryGetProperty("imageFileId", out var image) && image.ValueKind != JsonValueKind.Null)
            {
                Assert.NotEqual(Guid.Empty, image.GetGuid());
            }
        }
    }

    /// <summary>A draft preview renders unsaved blocks, saves nothing, and refuses bad ones like a save does.</summary>
    [Fact]
    public async Task Preview_draft_renders_unsaved_blocks_without_persisting_them()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        var page = await ReadAsync(await admin.PostAsJsonAsync(
            $"{Admin}/pages",
            new { slug = $"draft-preview-{Guid.NewGuid():N}", type = "Landing", title = "Draft preview", summary = (string?)null },
            Cancellation));

        var id = page.GetProperty("id").GetGuid();

        var blocks = new object[]
        {
            new
            {
                type = "RichText",
                config = new { heading = "Unsaved", body = "<p>Not stored.</p>" },
                isVisible = true,
            },
            new
            {
                // No picture yet: fine for a preview, which is what the canvas needs mid-edit.
                type = "Hero",
                config = new { headline = "No picture yet", align = "left" },
                isVisible = true,
            },
        };

        var rendered = await ReadAsync(await admin.PostAsJsonAsync(
            $"{Admin}/pages/{id}/preview-draft",
            new { blocks },
            Cancellation));

        Assert.Equal(id, rendered.GetProperty("id").GetGuid());
        Assert.Equal(2, rendered.GetProperty("blocks").GetArrayLength());
        Assert.Equal("Unsaved", rendered.GetProperty("blocks")[0].GetProperty("config").GetProperty("heading").GetString());

        var stored = await ReadAsync(await admin.GetAsync($"{Admin}/pages/{id}", Cancellation));

        Assert.Equal(0, stored.GetProperty("blocks").GetArrayLength());

        // The same validation response a save gives (422, CONTENT_INVALID_BLOCK, field paths in `errors`).
        var refused = await RefusedAsync(
            await admin.PostAsJsonAsync(
                $"{Admin}/pages/{id}/preview-draft",
                new { blocks = new object[] { new { type = "RichText", config = new { headline = "wrong field" }, isVisible = true } } },
                Cancellation),
            HttpStatusCode.UnprocessableEntity,
            "CONTENT_INVALID_BLOCK");

        Assert.True(refused.GetProperty("errors").TryGetProperty("blocks[0].body", out _));

        await RefusedAsync(
            await admin.PostAsJsonAsync($"{Admin}/pages/{Guid.NewGuid()}/preview-draft", new { blocks }, Cancellation),
            HttpStatusCode.NotFound);
    }

    /// <summary>A save accepts an incomplete draft; publishing it is refused with the block named.</summary>
    [Fact]
    public async Task Save_accepts_an_incomplete_draft_and_publish_refuses_it()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        var page = await ReadAsync(await admin.PostAsJsonAsync(
            $"{Admin}/pages",
            new { slug = $"incomplete-{Guid.NewGuid():N}", type = "Landing", title = "Incomplete", summary = (string?)null },
            Cancellation));

        var id = page.GetProperty("id").GetGuid();
        var slug = page.GetProperty("slug").GetString();

        var saved = await ReadAsync(await admin.PutAsJsonAsync(
            $"{Admin}/pages/{id}",
            new
            {
                slug,
                title = "Incomplete",
                blocks = new object[] { new { type = "Hero", config = new { headline = "No picture yet", align = "left" }, isVisible = true } },
            },
            Cancellation));

        Assert.Equal(1, saved.GetProperty("blocks").GetArrayLength());

        var refused = await RefusedAsync(
            await admin.PostAsJsonAsync($"{Admin}/pages/{id}/transition", new { status = "Published", note = "x" }, Cancellation),
            HttpStatusCode.UnprocessableEntity,
            "CONTENT_INVALID_BLOCK");

        Assert.True(refused.GetProperty("errors").TryGetProperty("blocks[0].imageFileId", out _));

        var still = await ReadAsync(await admin.GetAsync($"{Admin}/pages/{id}", Cancellation));

        Assert.Equal("Draft", still.GetProperty("status").GetString());
    }
}
