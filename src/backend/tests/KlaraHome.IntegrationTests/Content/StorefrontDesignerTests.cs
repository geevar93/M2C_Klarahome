using System.Net;
using System.Net.Http.Json;
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

        var preview = await ReadAsync(await admin.GetAsync($"{Admin}/storefront-templates/loom/preview", Cancellation));

        Assert.Equal(5, preview.GetProperty("blocks").GetArrayLength());
        Assert.Equal("Hero", preview.GetProperty("blocks")[0].GetProperty("type").GetString());

        // The preview was not persisted: there is still no home page.
        var homes = await ReadAsync(await admin.GetAsync($"{Admin}/pages?type=Home", Cancellation));
        var before = homes.GetProperty("items").GetArrayLength();

        Assert.Equal(0, before);

        var created = await ReadAsync(await admin.PostAsync($"{Admin}/storefront-templates/loom/apply", content: null, Cancellation));

        Assert.Equal("home", created.GetProperty("slug").GetString());
        Assert.Equal("Home", created.GetProperty("type").GetString());
        Assert.Equal("Draft", created.GetProperty("status").GetString());
        Assert.Equal(5, created.GetProperty("blocks").GetArrayLength());

        var id = created.GetProperty("id").GetGuid();

        var replaced = await ReadAsync(await admin.PostAsync($"{Admin}/storefront-templates/atelier/apply", content: null, Cancellation));

        Assert.Equal(id, replaced.GetProperty("id").GetGuid());
        Assert.Equal(4, replaced.GetProperty("blocks").GetArrayLength());
        Assert.Equal("Hero", replaced.GetProperty("blocks")[0].GetProperty("type").GetString());
        Assert.Equal("left", replaced.GetProperty("blocks")[0].GetProperty("config").GetProperty("align").GetString());

        var reread = await ReadAsync(await admin.GetAsync($"{Admin}/pages/{id}", Cancellation));

        Assert.Equal(4, reread.GetProperty("blocks").GetArrayLength());

        homes = await ReadAsync(await admin.GetAsync($"{Admin}/pages?type=Home", Cancellation));

        Assert.Equal(1, homes.GetProperty("items").GetArrayLength());

        // Going live is strict: the applied hero has no picture, so publishing is refused, naming it.
        var refusedPublish = await RefusedAsync(
            await admin.PostAsJsonAsync($"{Admin}/pages/{id}/transition", new { status = "Published", note = "test" }, Cancellation),
            HttpStatusCode.UnprocessableEntity,
            "CONTENT_INVALID_BLOCK");

        Assert.True(refusedPublish.GetProperty("errors").TryGetProperty("blocks[0].imageFileId", out _));

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
        Assert.Equal(5, again.GetProperty("blocks").GetArrayLength());
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
