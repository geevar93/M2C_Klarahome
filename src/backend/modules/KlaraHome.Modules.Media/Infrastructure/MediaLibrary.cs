using KlaraHome.Contracts.Media;
using KlaraHome.Infrastructure.Storage;
using KlaraHome.Modules.Media.Domain;
using KlaraHome.Modules.Media.Infrastructure.Imaging;
using KlaraHome.Modules.Media.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Media.Infrastructure;

/// <summary>
/// Resolves file ids into something a response can carry (ADR-016).
/// </summary>
/// <remarks>
/// The published half of this module. Every other module holds file ids and none of them may read
/// <c>media.files</c>, so this is where an id becomes a URL, a size and a set of renditions.
/// </remarks>
/// <param name="context">The Media data context.</param>
/// <param name="storage">Object storage, for public URLs and signed links.</param>
/// <param name="imgproxy">Builds the responsive renditions.</param>
/// <param name="options">Signed-URL lifetime.</param>
internal sealed class MediaLibrary(
    MediaDbContext context,
    IFileStorage storage,
    ImgproxyUrlBuilder imgproxy,
    IOptions<StorageOptions> options) : IMediaLibrary
{
    /// <inheritdoc />
    public async Task<MediaFile?> GetAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await context.Files
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == fileId, cancellationToken)
            .ConfigureAwait(false);

        return file is null || !file.IsServable ? null : Project(file);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, MediaFile>> GetManyAsync(
        IReadOnlyCollection<Guid> fileIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileIds);

        if (fileIds.Count == 0)
        {
            return new Dictionary<Guid, MediaFile>();
        }

        var files = await context.Files
            .AsNoTracking()
            .Where(candidate => fileIds.Contains(candidate.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return files
            .Where(file => file.IsServable)
            .ToDictionary(file => file.Id, Project);
    }

    /// <inheritdoc />
    public async Task<string?> GetSignedUrlAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await context.Files
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == fileId, cancellationToken)
            .ConfigureAwait(false);

        if (file is null || !file.IsServable || !storage.IsAvailable)
        {
            return null;
        }

        return file.Visibility == MediaVisibility.Public
            ? storage.GetPublicUrl(file.StorageKey)
            : storage.GetSignedUrl(
                file.StorageKey,
                StorageVisibility.Private,
                TimeSpan.FromMinutes(options.Value.SignedUrlMinutes),
                file.FileName);
    }

    /// <summary>
    /// Maps a registry row onto the published shape.
    /// </summary>
    /// <remarks>
    /// A private file's <c>Url</c> is deliberately null. There is no URL that is correct for
    /// everybody — the only URL a private document has is a signed one issued to a caller whose
    /// authorisation has been checked — and returning a permanent one here is how a KYC document
    /// ends up in a list response.
    /// </remarks>
    /// <param name="file">The registry row.</param>
    internal MediaFile Project(StoredFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var isPublic = file.Visibility == MediaVisibility.Public;
        var isImage = file.ContentType.StartsWith("image/", StringComparison.Ordinal);

        return new MediaFile(
            file.Id,
            file.Visibility,
            file.ContentType,
            file.FileName,
            file.ByteSize,
            file.Width,
            file.Height,
            isPublic && storage.IsAvailable ? storage.GetPublicUrl(file.StorageKey) : null,
            isPublic && isImage ? imgproxy.BuildVariants(file.StorageKey, file.Width) : []);
    }
}
