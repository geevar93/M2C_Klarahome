using System.Globalization;
using System.Security.Cryptography;
using KlaraHome.Contracts.Media;
using KlaraHome.Infrastructure.Storage;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Media.Domain;
using KlaraHome.Modules.Media.Infrastructure.Persistence;
using KlaraHome.Modules.Media.Infrastructure.Scanning;
using KlaraHome.Modules.Media.Infrastructure.Validation;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Media.Infrastructure;

/// <summary>Why an upload was refused.</summary>
internal static class MediaErrors
{
    /// <summary>The bytes are not a type this platform accepts for that intent.</summary>
    public static Error UnsupportedType(MediaIntent intent) => Error.Validation(
        "MEDIA_UNSUPPORTED_TYPE",
        intent == MediaIntent.Image
            ? "The file is not a JPEG, PNG, GIF or WebP image."
            : "The file is not a PDF document.");

    /// <summary>The file is larger than this deployment accepts.</summary>
    public static Error TooLarge(long limit) => Error.Validation(
        "MEDIA_TOO_LARGE",
        string.Create(
            CultureInfo.InvariantCulture,
            $"The file is larger than the {limit / (1024 * 1024)} MB limit."));

    /// <summary>The image is larger in pixels than the derivative service will decode.</summary>
    public static Error TooManyPixels(int limit) => Error.Validation(
        "MEDIA_DIMENSIONS_TOO_LARGE",
        string.Create(CultureInfo.InvariantCulture, $"The image is wider or taller than {limit} pixels."));

    /// <summary>Nothing was uploaded.</summary>
    public static readonly Error Empty = Error.Validation("MEDIA_EMPTY", "The file is empty.");

    /// <summary>A scanner is required and none is registered.</summary>
    public static readonly Error ScanRequired = Error.Unavailable(
        "MEDIA_SCAN_UNAVAILABLE",
        "Uploads are refused because this deployment requires virus scanning and no scanner is configured.");

    /// <summary>A scanner found something.</summary>
    public static readonly Error Infected = Error.Validation(
        "MEDIA_INFECTED",
        "The file was rejected by the virus scanner.");

    /// <summary>Object storage is not configured or is unreachable.</summary>
    public static readonly Error StorageUnavailable = Error.Unavailable(
        "MEDIA_STORAGE_UNAVAILABLE",
        "Object storage is not available.");

    /// <summary>No file with that id, or it has been deleted.</summary>
    public static readonly Error NotFound = Error.NotFound("MEDIA_NOT_FOUND", "No such file.");
}

/// <summary>
/// Accepts an upload: validates it by content, scans it, stores it and registers it.
/// </summary>
/// <remarks>
/// <para>
/// The order matters and is the whole design. Validation and scanning happen on the bytes in
/// memory, <em>before</em> anything is written to a bucket — so a rejected file was never stored,
/// never had a URL, and leaves nothing to clean up. Registration happens last, so a row exists only
/// for an object that exists.
/// </para>
/// <para>
/// The reverse failure is possible and accepted: the object is written and the database write then
/// fails, leaving an object nobody has a row for. That is an orphan taking up disk, which the
/// sweep at Step 31 collects — strictly better than a row promising a file that is not there.
/// </para>
/// </remarks>
/// <param name="context">The Media data context.</param>
/// <param name="storage">Object storage.</param>
/// <param name="scanner">The virus-scan seam.</param>
/// <param name="library">Projects the registered row onto the published shape.</param>
/// <param name="options">Size and dimension limits.</param>
/// <param name="tenantContext">Supplies the tenant prefix in the object key.</param>
/// <param name="userContext">Records who uploaded it.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class MediaStorageService(
    MediaDbContext context,
    IFileStorage storage,
    IVirusScanner scanner,
    MediaLibrary library,
    IOptions<MediaOptions> options,
    ITenantContext tenantContext,
    IUserContext userContext,
    IClock clock)
{
    /// <summary>Stores an uploaded file and returns it as other modules will see it.</summary>
    /// <param name="content">The complete content.</param>
    /// <param name="fileName">The name it was uploaded under.</param>
    /// <param name="intent">What it is meant to be, which decides what is accepted.</param>
    /// <param name="visibility">Which bucket it belongs in.</param>
    /// <param name="ownerType">What it belongs to, for the orphan sweep.</param>
    /// <param name="ownerId">The id of the thing it belongs to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<MediaFile>> StoreAsync(
        ReadOnlyMemory<byte> content,
        string fileName,
        MediaIntent intent,
        MediaVisibility visibility,
        string? ownerType = null,
        Guid? ownerId = null,
        CancellationToken cancellationToken = default)
    {
        var limits = options.Value;

        if (content.IsEmpty)
        {
            return Result.Failure<MediaFile>(MediaErrors.Empty);
        }

        if (!storage.IsAvailable)
        {
            return Result.Failure<MediaFile>(MediaErrors.StorageUnavailable);
        }

        var limit = intent == MediaIntent.Image ? limits.MaxImageBytes : limits.MaxDocumentBytes;

        if (content.Length > limit)
        {
            return Result.Failure<MediaFile>(MediaErrors.TooLarge(limit));
        }

        var inspected = FileInspector.Inspect(content.Span, intent);

        if (inspected is null)
        {
            return Result.Failure<MediaFile>(MediaErrors.UnsupportedType(intent));
        }

        if (inspected.Width > limits.MaxImageDimension || inspected.Height > limits.MaxImageDimension)
        {
            return Result.Failure<MediaFile>(MediaErrors.TooManyPixels(limits.MaxImageDimension));
        }

        if (limits.RequireVirusScan && !scanner.IsRealScanner)
        {
            return Result.Failure<MediaFile>(MediaErrors.ScanRequired);
        }

        var scan = await scanner.ScanAsync(content, fileName, cancellationToken).ConfigureAwait(false);

        if (scan == ScanState.Infected)
        {
            return Result.Failure<MediaFile>(MediaErrors.Infected);
        }

        var checksum = Convert.ToHexStringLower(SHA256.HashData(content.Span));
        var key = BuildKey(inspected.Extension);

        using (var buffer = new MemoryStream(content.ToArray(), writable: false))
        {
            await storage
                .PutAsync(
                    key,
                    buffer,
                    inspected.ContentType,
                    visibility == MediaVisibility.Public ? StorageVisibility.Public : StorageVisibility.Private,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var file = StoredFile.Register(
            key,
            visibility,
            SafeFileName(fileName, inspected.Extension),
            inspected.ContentType,
            content.Length,
            checksum);

        if (inspected is { Width: { } width, Height: { } height })
        {
            file.DescribeImage(width, height);
        }

        file.Scanned(scan, clock.UtcNow);
        file.AttachTo(ownerType, ownerId);
        file.UploadedByUser(userContext.UserId);

        context.Files.Add(file);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(library.Project(file));
    }

    /// <summary>Retires a file and removes the object behind it.</summary>
    /// <remarks>
    /// The row survives the object. Six other schemas may hold this id and none of them can be
    /// joined to, so a deleted file has to resolve to "nothing" rather than to a missing row that
    /// a caller cannot tell apart from a bug.
    /// </remarks>
    /// <param name="fileId">The file to retire.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> DeleteAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await context.Files
            .FirstOrDefaultAsync(candidate => candidate.Id == fileId, cancellationToken)
            .ConfigureAwait(false);

        if (file is null || file.Status == StoredFileStatus.Deleted)
        {
            return Result.Failure(MediaErrors.NotFound);
        }

        file.Retire();
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (storage.IsAvailable)
        {
            await storage
                .DeleteAsync(
                    file.StorageKey,
                    file.Visibility == MediaVisibility.Public ? StorageVisibility.Public : StorageVisibility.Private,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return Result.Success();
    }

    /// <summary>
    /// Builds the object key: tenant, year, month, then a UUIDv7.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The uploaded filename is deliberately <em>not</em> part of the key. A key built from a
    /// caller-supplied name is a path-traversal question ("../"), a collision question (two people
    /// uploading <c>logo.png</c>) and an information-disclosure question (a bucket listing that
    /// reads like a customer's document folder) all at once.
    /// </para>
    /// <para>
    /// The date prefix is what makes a lifecycle rule expressible — "everything under 2026/01" —
    /// and keeps a bucket listing navigable once there are a million objects in it.
    /// </para>
    /// </remarks>
    private string BuildKey(string extension)
    {
        var now = clock.UtcNow;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{tenantContext.Code}/{now:yyyy}/{now:MM}/{SharedKernel.Primitives.UuidV7.New():n}{extension}");
    }

    /// <summary>
    /// Reduces the uploaded name to something safe to store and to echo back in a download header.
    /// </summary>
    /// <param name="fileName">The name as uploaded.</param>
    /// <param name="extension">The extension the content actually justifies.</param>
    private static string SafeFileName(string fileName, string extension)
    {
        var name = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
        var cleaned = new string([.. name.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' or ' ')]).Trim();

        if (cleaned.Length > 100)
        {
            cleaned = cleaned[..100];
        }

        // The extension comes from the inspector, never from the caller: an image that claimed to
        // be .html would otherwise keep saying so on every download.
        return (cleaned.Length == 0 ? "file" : cleaned) + extension;
    }
}
