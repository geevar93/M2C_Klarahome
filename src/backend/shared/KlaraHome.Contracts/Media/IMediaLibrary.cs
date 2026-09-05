namespace KlaraHome.Contracts.Media;

/// <summary>Who may read a stored file, and therefore which bucket it lives in.</summary>
public enum MediaVisibility
{
    /// <summary>Catalogue and CMS imagery. Served straight from the public bucket or a CDN.</summary>
    Public = 0,

    /// <summary>Invoices, KYC documents, labels, exports. Reached only through a signed URL.</summary>
    Private = 1,
}

/// <summary>
/// One rendition of an image, as the storefront asks for it.
/// </summary>
/// <param name="Name">The rendition key — <c>thumb</c>, <c>small</c>, <c>medium</c>, <c>large</c>.</param>
/// <param name="Width">Target width in CSS pixels; the height follows the source aspect ratio.</param>
/// <param name="Url">A ready-to-use URL, signed if the deployment signs imgproxy URLs.</param>
public sealed record MediaVariant(string Name, int Width, string Url);

/// <summary>
/// A stored file, as every other module sees it.
/// </summary>
/// <param name="Id">The file id other modules store.</param>
/// <param name="Visibility">Public or private.</param>
/// <param name="ContentType">The MIME type, verified against the bytes at upload.</param>
/// <param name="FileName">The name the file was uploaded under, for downloads.</param>
/// <param name="ByteSize">Size in bytes.</param>
/// <param name="Width">Pixel width for a raster image; null otherwise.</param>
/// <param name="Height">Pixel height for a raster image; null otherwise.</param>
/// <param name="Url">
/// The canonical URL: a public URL for a public file, and <see langword="null"/> for a private one —
/// a private document has no URL until somebody's authorisation has been checked and one is signed.
/// </param>
/// <param name="Variants">Responsive renditions, empty for anything that is not a raster image.</param>
public sealed record MediaFile(
    Guid Id,
    MediaVisibility Visibility,
    string ContentType,
    string FileName,
    long ByteSize,
    int? Width,
    int? Height,
    string? Url,
    IReadOnlyList<MediaVariant> Variants);

/// <summary>
/// Reads the media registry from another module (ADR-016).
/// </summary>
/// <remarks>
/// <para>
/// Eight columns across six schemas hold a <c>file_id</c>, and none of them may join to
/// <c>media.files</c> — a foreign key across schemas is forbidden by <c>01-architecture.md</c> §2.1.
/// This is how those ids become something a response can carry.
/// </para>
/// <para>
/// A file id that resolves to nothing is a normal answer, not an error: the file may have been
/// deleted after the reference was stored. Callers render what they have rather than failing the
/// page.
/// </para>
/// </remarks>
public interface IMediaLibrary
{
    /// <summary>Resolves one file, or null if it is unknown or deleted.</summary>
    /// <param name="fileId">The stored file id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<MediaFile?> GetAsync(Guid fileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves many files at once. Unknown ids are simply absent from the result.
    /// </summary>
    /// <remarks>
    /// The reason this method exists: a product listing page resolves fifty thumbnails, and fifty
    /// single-id calls is the query pattern that makes a listing page slow.
    /// </remarks>
    /// <param name="fileIds">The ids to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyDictionary<Guid, MediaFile>> GetManyAsync(
        IReadOnlyCollection<Guid> fileIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A short-lived URL granting read access to a private file.
    /// </summary>
    /// <remarks>
    /// Call it only after deciding the caller may see the document. The URL carries no
    /// authorisation of its own beyond its expiry, so minting one is the grant.
    /// </remarks>
    /// <param name="fileId">The stored file id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string?> GetSignedUrlAsync(Guid fileId, CancellationToken cancellationToken = default);
}
