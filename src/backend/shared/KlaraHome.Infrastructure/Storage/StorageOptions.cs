using System.ComponentModel.DataAnnotations;

namespace KlaraHome.Infrastructure.Storage;

/// <summary>
/// Where objects are stored, and how they are reached (docs/08-integrations.md §4).
/// </summary>
/// <remarks>
/// <para>
/// Every value here is S3 vocabulary rather than MinIO vocabulary, which is the whole point:
/// moving to AWS S3 in <c>ap-south-1</c> or to Cloudflare R2 is a change to
/// <see cref="Endpoint"/>, the credentials and <see cref="ForcePathStyle"/> — no code (ADR-010).
/// </para>
/// <para>
/// Two buckets, not one, and the split is a security boundary rather than a filing convention:
/// <see cref="Bucket"/> is world-readable and holds catalogue and CMS imagery;
/// <see cref="PrivateBucket"/> holds invoices, KYC documents, labels and exports, and is reachable
/// only through a signed URL issued after an authorisation check.
/// </para>
/// </remarks>
public sealed class StorageOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = "Storage";

    /// <summary>
    /// The S3 endpoint. A MinIO service address in development; empty for AWS S3 itself, where the
    /// SDK derives the endpoint from <see cref="Region"/>.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Access key id.</summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>Secret access key. Supplied as a Docker secret in a deployed environment.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>The world-readable bucket: catalogue imagery, CMS assets, branding.</summary>
    [Required]
    public string Bucket { get; set; } = "media-public";

    /// <summary>The private bucket: invoices, credit notes, labels, KYC documents, exports.</summary>
    [Required]
    public string PrivateBucket { get; set; } = "docs-private";

    /// <summary>Region. Mumbai by default, because that is where the data must stay.</summary>
    [Required]
    public string Region { get; set; } = "ap-south-1";

    /// <summary>
    /// Path-style addressing (<c>host/bucket/key</c>) rather than virtual-host style
    /// (<c>bucket.host/key</c>). Required by MinIO on a bare host name; AWS S3 wants it off.
    /// </summary>
    public bool ForcePathStyle { get; set; } = true;

    /// <summary>
    /// Public base URL for the world-readable bucket, without a trailing slash. This is what a
    /// browser fetches, so it is the CDN or reverse-proxy address rather than the internal
    /// <see cref="Endpoint"/> the API talks to.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>How long a signed URL for a private object stays valid.</summary>
    [Range(1, 1440)]
    public int SignedUrlMinutes { get; set; } = 10;

    /// <summary>
    /// The endpoint a <em>signed</em> URL is issued against, when it differs from
    /// <see cref="Endpoint"/>. Empty means they are the same.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The API writes objects over an internal address a browser cannot reach — <c>http://minio:9000</c>
    /// on the compose network. A presigned URL, though, is handed to a browser, and SigV4 signs the
    /// host it is issued for: a URL signed for the internal host is both unreachable and, if the
    /// host were rewritten, invalid.
    /// </para>
    /// <para>
    /// So the signature is produced against the public address instead. MinIO validates it because
    /// <c>MINIO_SERVER_URL</c> tells it which host to expect; AWS S3 and R2 need nothing set here,
    /// because their public and internal endpoints are already the same.
    /// </para>
    /// </remarks>
    public string SignedUrlEndpoint { get; set; } = string.Empty;

    /// <summary>Whether object storage is configured at all.</summary>
    /// <remarks>
    /// A host with no credentials — the integration-test API, for instance — registers the
    /// abstraction and no client, and reports the checks it can run rather than failing one it
    /// cannot act on. That is the same rule the database health check follows.
    /// </remarks>
    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(AccessKey) && !string.IsNullOrWhiteSpace(SecretKey);
}
