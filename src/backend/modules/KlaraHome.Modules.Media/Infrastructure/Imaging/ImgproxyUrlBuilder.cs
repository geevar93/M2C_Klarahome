using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KlaraHome.Contracts.Media;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Media.Infrastructure.Imaging;

/// <summary>
/// Builds the signed imgproxy URLs the storefront asks renditions through
/// (docs/08-integrations.md §4).
/// </summary>
/// <remarks>
/// <para>
/// No rendition is ever stored. A variant URL is an instruction — "fit this original into 480
/// pixels and give me WebP" — that imgproxy carries out and caches, so changing the rendition
/// widths changes what the next page load requests and needs no regeneration job and no variants
/// table.
/// </para>
/// <para>
/// <b>The signature is what stops imgproxy being an open image proxy.</b> Unsigned, anybody who
/// finds the host can resize arbitrary images through it — bandwidth billed to this deployment,
/// under this deployment's domain. The key and salt are hex, exactly as imgproxy's own
/// <c>IMGPROXY_KEY</c> and <c>IMGPROXY_SALT</c> are.
/// </para>
/// </remarks>
/// <param name="options">Rendition widths and the imgproxy configuration.</param>
internal sealed class ImgproxyUrlBuilder(IOptions<MediaOptions> options)
{
    private readonly MediaOptions _media = options.Value;

    /// <summary>
    /// The rendition widths, de-duplicated and ordered.
    /// </summary>
    /// <remarks>
    /// Not simply <c>MediaOptions.VariantWidths</c>. The configuration binder <em>appends</em> to a
    /// collection-typed property that already has a value rather than replacing it, so a default of
    /// four widths and a configuration file naming the same four produces eight — and a response
    /// carrying "thumb, small, thumb, small". Normalising here makes the builder correct whatever
    /// the binder does, and keeps the names positional and stable.
    /// </remarks>
    private readonly List<int> _widths = [.. options.Value.VariantWidths.Distinct().Order()];

    /// <summary>Whether renditions can be produced. False leaves the original as the only URL.</summary>
    public bool IsConfigured => _media.Imgproxy.IsConfigured;

    /// <summary>
    /// The responsive renditions of one public image.
    /// </summary>
    /// <remarks>
    /// A rendition wider than the original is not produced: upscaling a 200-pixel logo to 1600
    /// costs bandwidth to deliver a blurrier picture. The list is therefore shorter for a small
    /// original, and empty for anything that is not a raster image.
    /// </remarks>
    /// <param name="storageKey">The original's object key.</param>
    /// <param name="sourceWidth">The original's pixel width, when it is known.</param>
    public IReadOnlyList<MediaVariant> BuildVariants(string storageKey, int? sourceWidth)
    {
        if (!IsConfigured)
        {
            return [];
        }

        var widths = _widths
            .Where(width => sourceWidth is null || width <= sourceWidth)
            .ToList();

        // An original narrower than the smallest rendition still gets one, so a caller asking for
        // "the small one" always has something to ask for.
        if (widths.Count == 0 && _widths.Count > 0)
        {
            widths.Add(_widths[0]);
        }

        return [.. widths.Select(width => new MediaVariant(NameFor(width), width, Build(storageKey, width)))];
    }

    /// <summary>Builds one rendition URL.</summary>
    /// <param name="storageKey">The original's object key.</param>
    /// <param name="width">Target width in pixels.</param>
    public string Build(string storageKey, int width)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        var source = $"{_media.Imgproxy.SourceBaseUrl.TrimEnd('/')}/{storageKey}";
        var encoded = Base64Url(Encoding.UTF8.GetBytes(source));

        // rs:fit:{w}:0 - fit inside the width, height unconstrained, aspect ratio preserved.
        // Zero height rather than a computed one: the original's ratio is the source of truth, and
        // computing a height here would round differently from the way imgproxy rounds.
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"/rs:fit:{width}:0/{encoded}.{_media.Imgproxy.Format}");

        var signature = _media.Imgproxy.IsSigned ? Sign(path) : "insecure";

        return $"{_media.Imgproxy.BaseUrl.TrimEnd('/')}/{signature}{path}";
    }

    /// <summary>
    /// The rendition's name, which is what a client asks for rather than a pixel count.
    /// </summary>
    /// <remarks>
    /// Positional rather than absolute: the names stay meaningful when the widths are re-tuned,
    /// and a client written against "thumb" does not break when a thumbnail becomes 200 pixels.
    /// </remarks>
    private string NameFor(int width)
    {
        var index = _widths.IndexOf(width);

        return index switch
        {
            0 => "thumb",
            1 => "small",
            2 => "medium",
            3 => "large",
            _ => string.Create(CultureInfo.InvariantCulture, $"w{width}"),
        };
    }

    /// <summary>HMAC-SHA256 over salt + path, keyed with the shared secret, exactly as imgproxy verifies it.</summary>
    private string Sign(string path)
    {
        var key = Convert.FromHexString(_media.Imgproxy.Key);
        var salt = Convert.FromHexString(_media.Imgproxy.Salt);

        using var hmac = new HMACSHA256(key);
        hmac.TransformBlock(salt, 0, salt.Length, null, 0);

        var pathBytes = Encoding.UTF8.GetBytes(path);
        hmac.TransformFinalBlock(pathBytes, 0, pathBytes.Length);

        return Base64Url(hmac.Hash!);
    }

    /// <summary>
    /// Base64 in the URL alphabet with padding stripped, which is the encoding imgproxy expects
    /// for both the signature and the source URL.
    /// </summary>
    private static string Base64Url(byte[] value)
        => Convert.ToBase64String(value)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}
