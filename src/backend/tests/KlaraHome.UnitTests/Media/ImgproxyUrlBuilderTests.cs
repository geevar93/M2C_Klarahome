using KlaraHome.Modules.Media.Infrastructure;
using KlaraHome.Modules.Media.Infrastructure.Imaging;
using Microsoft.Extensions.Options;

namespace KlaraHome.UnitTests.Media;

/// <summary>
/// Rendition URLs are computed, never stored, so the URL is the whole contract between this
/// platform and imgproxy.
/// </summary>
public sealed class ImgproxyUrlBuilderTests
{
    private const string Key = "6465762d6b6579";
    private const string Salt = "6465762d73616c74";

    [Fact]
    public void An_unconfigured_deployment_produces_no_variants()
    {
        var builder = Build(configured: false, signed: false);

        Assert.False(builder.IsConfigured);
        Assert.Empty(builder.BuildVariants("tenant/2026/09/abc.png", 2000));
    }

    [Fact]
    public void A_variant_url_names_the_width_the_format_and_the_encoded_source()
    {
        var url = Build(configured: true, signed: false).Build("tenant/2026/09/abc.png", 480);

        Assert.StartsWith("https://img.example.test/insecure/rs:fit:480:0/", url, StringComparison.Ordinal);
        Assert.EndsWith(".webp", url, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unsigned_url_says_so_where_the_signature_would_be()
    {
        // imgproxy's own convention. It is here as an assertion because a deployment that reaches
        // production unsigned is an open image proxy, and this is what that looks like.
        Assert.Contains("/insecure/", Build(configured: true, signed: false).Build("a/b.png", 160), StringComparison.Ordinal);
    }

    [Fact]
    public void A_signed_url_is_stable_for_the_same_input()
    {
        var builder = Build(configured: true, signed: true);

        var first = builder.Build("tenant/2026/09/abc.png", 480);
        var second = builder.Build("tenant/2026/09/abc.png", 480);

        Assert.Equal(first, second);
        Assert.DoesNotContain("/insecure/", first, StringComparison.Ordinal);
    }

    [Fact]
    public void A_signature_changes_with_the_path_it_covers()
    {
        var builder = Build(configured: true, signed: true);

        Assert.NotEqual(
            builder.Build("tenant/2026/09/abc.png", 480),
            builder.Build("tenant/2026/09/abc.png", 960));
    }

    [Fact]
    public void Renditions_wider_than_the_original_are_not_offered()
    {
        // Upscaling costs bandwidth to deliver a blurrier picture.
        var variants = Build(configured: true, signed: false).BuildVariants("logo.png", sourceWidth: 500);

        Assert.Equal(["thumb", "small"], variants.Select(variant => variant.Name));
        Assert.All(variants, variant => Assert.True(variant.Width <= 500));
    }

    [Fact]
    public void An_original_narrower_than_every_rendition_still_gets_one()
    {
        // So a caller asking for "the small one" always has something to ask for.
        var variants = Build(configured: true, signed: false).BuildVariants("favicon.png", sourceWidth: 32);

        Assert.Single(variants);
        Assert.Equal("thumb", variants[0].Name);
    }

    [Fact]
    public void Duplicated_widths_produce_one_rendition_each()
    {
        // The configuration binder appends to a collection-typed property that already has a
        // value, so a default list and a configuration file naming the same widths gives eight
        // entries - and, before this was normalised, a response reading "thumb, small, thumb,
        // small".
        var options = new MediaOptions
        {
            VariantWidths = [160, 480, 960, 1600, 160, 480, 960, 1600],
            Imgproxy = new ImgproxyOptions
            {
                BaseUrl = "https://img.example.test",
                SourceBaseUrl = "http://minio:9000/media-public",
                Format = "webp",
            },
        };

        var variants = new ImgproxyUrlBuilder(Options.Create(options)).BuildVariants("photo.jpg", null);

        Assert.Equal(["thumb", "small", "medium", "large"], variants.Select(variant => variant.Name));
        Assert.Equal([160, 480, 960, 1600], variants.Select(variant => variant.Width));
    }

    [Fact]
    public void Widths_given_out_of_order_are_named_smallest_first()
    {
        var options = new MediaOptions
        {
            VariantWidths = [960, 160, 480],
            Imgproxy = new ImgproxyOptions
            {
                BaseUrl = "https://img.example.test",
                SourceBaseUrl = "http://minio:9000/media-public",
                Format = "webp",
            },
        };

        var variants = new ImgproxyUrlBuilder(Options.Create(options)).BuildVariants("photo.jpg", null);

        Assert.Equal([160, 480, 960], variants.Select(variant => variant.Width));
        Assert.Equal("thumb", variants[0].Name);
    }

    [Fact]
    public void An_unknown_source_width_offers_every_rendition()
    {
        var variants = Build(configured: true, signed: false).BuildVariants("photo.jpg", sourceWidth: null);

        Assert.Equal(4, variants.Count);
    }

    private static ImgproxyUrlBuilder Build(bool configured, bool signed)
    {
        var options = new MediaOptions
        {
            VariantWidths = [160, 480, 960, 1600],
            Imgproxy = new ImgproxyOptions
            {
                BaseUrl = configured ? "https://img.example.test" : string.Empty,
                SourceBaseUrl = configured ? "http://minio:9000/media-public" : string.Empty,
                Key = signed ? Key : string.Empty,
                Salt = signed ? Salt : string.Empty,
                Format = "webp",
            },
        };

        return new ImgproxyUrlBuilder(Options.Create(options));
    }
}
