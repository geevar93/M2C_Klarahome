using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Step8;

/// <summary>
/// Step 8's first acceptance criterion: an image uploads and returns responsive variants — and
/// everything that must not upload does not.
/// </summary>
[Collection(KlaraHomeSchema.CollectionName)]
public sealed class MediaApiTests(KlaraHomeSchemaFixture fixture) : Step8TestBase(fixture)
{
    [Fact]
    public async Task An_image_uploads_and_comes_back_with_responsive_variants()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var response = await UploadAsync(admin, TestPng(640, 480), "hero.png", "image/png");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var file = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);

        Assert.Equal("image/png", file.GetProperty("contentType").GetString());
        Assert.Equal(640, file.GetProperty("width").GetInt32());
        Assert.Equal(480, file.GetProperty("height").GetInt32());
        Assert.Equal("Ready", file.GetProperty("status").GetString());

        // The honest answer, not a comforting one: nothing scanned it (docs/08-integrations.md §4).
        Assert.Equal("Skipped", file.GetProperty("scanState").GetString());

        Assert.StartsWith(
            "https://cdn.example.test/media-public/",
            file.GetProperty("url").GetString(),
            StringComparison.Ordinal);

        var variants = file.GetProperty("variants").EnumerateArray().ToList();

        // Two, not four: a rendition wider than the 640-pixel original would be an upscale.
        Assert.Equal(["thumb", "small"], variants.Select(variant => variant.GetProperty("name").GetString()));
        Assert.All(variants, variant => Assert.True(variant.GetProperty("width").GetInt32() <= 640));

        foreach (var variant in variants)
        {
            var url = variant.GetProperty("url").GetString()!;

            Assert.StartsWith("https://img.example.test/", url, StringComparison.Ordinal);
            Assert.DoesNotContain("/insecure/", url, StringComparison.Ordinal);
            Assert.Contains($"rs:fit:{variant.GetProperty("width").GetInt32()}:0", url, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_uploaded_filename_never_becomes_the_storage_key()
    {
        SkipWithoutDocker();

        // A key built from a caller-supplied name is a path-traversal question, a collision
        // question and an information-disclosure question at once.
        var admin = await SignedInAdministratorAsync();

        var response = await UploadAsync(admin, TestPng(64, 64), "../../etc/passwd.png", "image/png");
        response.EnsureSuccessStatusCode();

        var file = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        var url = file.GetProperty("url").GetString()!;

        Assert.DoesNotContain("..", url, StringComparison.Ordinal);
        Assert.DoesNotContain("passwd", url, StringComparison.Ordinal);

        // The name survives for the download, reduced to its last segment and stripped of anything
        // that is not a name. The extension comes from the content, never from the caller.
        Assert.Equal("passwd.png", file.GetProperty("fileName").GetString());
    }

    [Fact]
    public async Task A_script_named_as_an_image_is_refused()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var payload = "<?php system($_GET['c']); ?>"u8.ToArray();

        var response = await UploadAsync(admin, payload, "logo.png", "image/png");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("MEDIA_UNSUPPORTED_TYPE", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_image_over_the_limit_is_refused_and_nothing_is_stored()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var before = Factory.Storage.Count;

        var oversized = new byte[8192];
        TestPng(64, 64).CopyTo(oversized, 0);

        var response = await UploadAsync(admin, oversized, "big.png", "image/png");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("MEDIA_TOO_LARGE", problem.GetProperty("code").GetString());

        // A rejected file was never stored, so there is nothing to clean up.
        Assert.Equal(before, Factory.Storage.Count);
    }

    [Fact]
    public async Task A_private_document_has_no_public_url_and_is_reached_only_through_a_signed_link()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        var response = await UploadAsync(
            admin,
            "%PDF-1.7\nfake"u8.ToArray(),
            "kyc.pdf",
            "application/pdf",
            visibility: "private");

        response.EnsureSuccessStatusCode();

        var file = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        var id = file.GetProperty("id").GetString();

        // There is no URL that is correct for everybody, so there is no URL.
        Assert.Equal(JsonValueKind.Null, file.GetProperty("url").ValueKind);
        Assert.Empty(file.GetProperty("variants").EnumerateArray());

        var link = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/media/{id}/link", Cancellation);

        Assert.StartsWith("https://signed.example.test/", link.GetProperty("url").GetString(), StringComparison.Ordinal);
        Assert.True(link.GetProperty("expiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task A_deleted_file_resolves_to_nothing_rather_than_to_an_error()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var upload = await UploadAsync(admin, TestPng(100, 100), "temp.png", "image/png");
        upload.EnsureSuccessStatusCode();

        var file = await upload.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        var id = file.GetProperty("id").GetString();

        var deleted = await admin.DeleteAsync(new Uri($"/api/v1/admin/media/{id}", UriKind.Relative), Cancellation);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var reread = await admin.GetAsync(new Uri($"/api/v1/admin/media/{id}", UriKind.Relative), Cancellation);
        Assert.Equal(HttpStatusCode.NotFound, reread.StatusCode);

        // Deleting twice is a 404, not a 500.
        var again = await admin.DeleteAsync(new Uri($"/api/v1/admin/media/{id}", UriKind.Relative), Cancellation);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task The_library_lists_what_was_uploaded_newest_first()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        await (await UploadAsync(admin, TestPng(120, 120), "one.png", "image/png")).Content
            .ReadFromJsonAsync<JsonElement>(Cancellation);
        await (await UploadAsync(admin, TestPng(130, 130), "two.png", "image/png")).Content
            .ReadFromJsonAsync<JsonElement>(Cancellation);

        var page = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/media?size=2", Cancellation);
        var items = page.GetProperty("items").EnumerateArray().ToList();

        Assert.Equal(2, items.Count);
        Assert.True(
            items[0].GetProperty("uploadedAt").GetDateTimeOffset()
            >= items[1].GetProperty("uploadedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_upload()
    {
        SkipWithoutDocker();

        var response = await UploadAsync(CreateClient(), TestPng(64, 64), "anon.png", "image/png");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        byte[] content,
        string fileName,
        string contentType,
        string? visibility = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);

        var url = visibility is null ? "/api/v1/admin/media" : $"/api/v1/admin/media?visibility={visibility}";

        return await client.PostAsync(new Uri(url, UriKind.Relative), form, Cancellation);
    }
}
