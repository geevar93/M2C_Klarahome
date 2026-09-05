using System.ComponentModel.DataAnnotations;

namespace KlaraHome.Modules.Media.Infrastructure;

/// <summary>
/// What this deployment will accept, and how it renders what it accepted.
/// </summary>
/// <remarks>
/// Limits are configuration rather than settings, because they are an operational property of the
/// deployment — how much disk and bandwidth it has — rather than something a shopkeeper decides.
/// </remarks>
internal sealed class MediaOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = "Media";

    /// <summary>Largest image accepted, in bytes. Ten megabytes by default.</summary>
    [Range(1024, 268_435_456)]
    public long MaxImageBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Largest document accepted, in bytes. Twenty-five megabytes by default.</summary>
    [Range(1024, 268_435_456)]
    public long MaxDocumentBytes { get; set; } = 25 * 1024 * 1024;

    /// <summary>
    /// Largest image accepted in either dimension, in pixels.
    /// </summary>
    /// <remarks>
    /// A cap on pixels and not only on bytes, because the two are not the same: a 40 000 x 40 000
    /// PNG of one flat colour compresses to a few hundred kilobytes and costs 6 GB to decode. The
    /// derivative service is what would decode it.
    /// </remarks>
    [Range(64, 20_000)]
    public int MaxImageDimension { get; set; } = 8_000;

    /// <summary>
    /// Whether an upload is refused while no real virus scanner is registered.
    /// </summary>
    /// <remarks>
    /// Off by default, because v1 ships with the no-op scanner and a deployment that refused every
    /// upload would be useless. On, it turns "we thought it was scanning" into a visible refusal —
    /// which is the state a deployment handling KYC documents should be in.
    /// </remarks>
    public bool RequireVirusScan { get; set; }

    /// <summary>Responsive rendition widths in CSS pixels, smallest first.</summary>
    public IReadOnlyList<int> VariantWidths { get; set; } = [160, 480, 960, 1600];

    /// <summary>How image derivatives are produced.</summary>
    public ImgproxyOptions Imgproxy { get; set; } = new();
}

/// <summary>
/// The imgproxy service that resizes and re-encodes images on demand
/// (docs/08-integrations.md §4).
/// </summary>
/// <remarks>
/// Derivatives are computed, never stored: there is no variants table and no regeneration job,
/// because the URL <em>is</em> the instruction. Changing the rendition widths changes what the
/// storefront asks for on the next page load.
/// </remarks>
internal sealed class ImgproxyOptions
{
    /// <summary>
    /// Public base URL of the imgproxy service, without a trailing slash. Empty disables
    /// derivatives entirely, and the original file's URL is returned for every rendition.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Base URL imgproxy itself uses to fetch an original, without a trailing slash. This is the
    /// internal address of the object store, which the browser cannot and should not reach.
    /// </summary>
    public string SourceBaseUrl { get; set; } = string.Empty;

    /// <summary>Hex-encoded signing key. Empty means unsigned URLs, which imgproxy allows.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Hex-encoded signing salt.</summary>
    public string Salt { get; set; } = string.Empty;

    /// <summary>Output format requested from imgproxy. WebP is universally supported; AVIF is smaller.</summary>
    [RegularExpression("^(webp|avif|jpg|png)$")]
    public string Format { get; set; } = "webp";

    /// <summary>Whether URLs are signed. Unsigned imgproxy is an open resizing proxy for anybody who finds it.</summary>
    public bool IsSigned => !string.IsNullOrWhiteSpace(Key) && !string.IsNullOrWhiteSpace(Salt);

    /// <summary>Whether derivatives can be produced at all.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(SourceBaseUrl);
}
