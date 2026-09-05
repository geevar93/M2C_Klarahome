using System.Globalization;
using FluentValidation;
using KlaraHome.Contracts.Media;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Media.Domain;
using KlaraHome.Modules.Media.Infrastructure;
using KlaraHome.Modules.Media.Infrastructure.Persistence;
using KlaraHome.Modules.Media.Infrastructure.Validation;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Media.Application;

/// <summary>A file as the admin media library lists it.</summary>
/// <param name="Id">The file id other modules store.</param>
/// <param name="FileName">The upload name.</param>
/// <param name="ContentType">The verified MIME type.</param>
/// <param name="ByteSize">Size in bytes.</param>
/// <param name="Width">Pixel width, for a raster image.</param>
/// <param name="Height">Pixel height, for a raster image.</param>
/// <param name="Visibility">Public or private.</param>
/// <param name="Status">Whether it can be served.</param>
/// <param name="ScanState">What is known about whether it is safe.</param>
/// <param name="OwnerType">What it belongs to, when that was recorded.</param>
/// <param name="Url">The public URL, or null for a private file.</param>
/// <param name="Variants">Responsive renditions, empty for anything but a public raster image.</param>
/// <param name="UploadedAt">When it was stored.</param>
internal sealed record MediaFileResponse(
    Guid Id,
    string FileName,
    string ContentType,
    long ByteSize,
    int? Width,
    int? Height,
    string Visibility,
    string Status,
    string ScanState,
    string? OwnerType,
    string? Url,
    IReadOnlyList<MediaVariant> Variants,
    DateTimeOffset UploadedAt);

/// <summary>A short-lived link to a private file.</summary>
/// <param name="Url">The signed URL.</param>
/// <param name="ExpiresAt">When it stops working.</param>
internal sealed record MediaLinkResponse(string Url, DateTimeOffset ExpiresAt);

/// <summary>Stores an uploaded file.</summary>
/// <param name="Content">The complete content, already read from the request.</param>
/// <param name="FileName">The name it was uploaded under.</param>
/// <param name="Intent">Whether it is meant to be an image or a document.</param>
/// <param name="Visibility">Which bucket it belongs in.</param>
/// <param name="OwnerType">What it belongs to, for the orphan sweep.</param>
/// <param name="OwnerId">The id of the thing it belongs to.</param>
internal sealed record UploadMediaCommand(
    ReadOnlyMemory<byte> Content,
    string FileName,
    MediaIntent Intent,
    MediaVisibility Visibility,
    string? OwnerType,
    Guid? OwnerId) : ICommand<MediaFileResponse>;

/// <summary>Rules a caller can get wrong without ever reaching storage.</summary>
internal sealed class UploadMediaCommandValidator : AbstractValidator<UploadMediaCommand>
{
    public UploadMediaCommandValidator()
    {
        RuleFor(command => command.FileName)
            .NotEmpty()
            .MaximumLength(255);

        RuleFor(command => command.OwnerType)
            .MaximumLength(64)
            .When(command => command.OwnerType is not null);

        // Not a size limit — that is configuration and is checked against the real limit in the
        // service. This only refuses an upload with no bytes in it at all.
        RuleFor(command => command.Content)
            .Must(content => !content.IsEmpty)
            .WithMessage("The file is empty.");
    }
}

/// <summary>Lists the media library, newest first.</summary>
/// <param name="Visibility">Restrict to one bucket, or null for both.</param>
/// <param name="ContentType">Restrict to one MIME type, or null.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListMediaQuery(string? Visibility, string? ContentType, string? Cursor, int? Size)
    : IQuery<PagedResult<MediaFileResponse>>;

/// <summary>Reads one file's registry entry.</summary>
/// <param name="FileId">The file id.</param>
internal sealed record GetMediaQuery(Guid FileId) : IQuery<MediaFileResponse>;

/// <summary>Mints a short-lived link to a private file.</summary>
/// <param name="FileId">The file id.</param>
internal sealed record GetMediaLinkQuery(Guid FileId) : IQuery<MediaLinkResponse>;

/// <summary>Retires a file and removes the object behind it.</summary>
/// <param name="FileId">The file id.</param>
internal sealed record DeleteMediaCommand(Guid FileId) : ICommand;

/// <param name="media">Validates, scans, stores and registers.</param>
/// <param name="context">Reads the row back for the response.</param>
/// <param name="audit">Records the upload.</param>
internal sealed class UploadMediaCommandHandler(
    MediaStorageService media,
    MediaDbContext context,
    IAuditLogger audit) : ICommandHandler<UploadMediaCommand, MediaFileResponse>
{
    /// <summary>The action recorded in the audit trail for an upload.</summary>
    public const string AuditAction = "media.file.uploaded";

    /// <summary>The entity type recorded against that action.</summary>
    public const string AuditEntityType = "MediaFile";

    public async Task<Result<MediaFileResponse>> HandleAsync(
        UploadMediaCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var stored = await media
            .StoreAsync(
                command.Content,
                command.FileName,
                command.Intent,
                command.Visibility,
                command.OwnerType,
                command.OwnerId,
                cancellationToken)
            .ConfigureAwait(false);

        if (stored.IsFailure)
        {
            return Result.Failure<MediaFileResponse>(stored.Error);
        }

        var file = await context.Files
            .AsNoTracking()
            .FirstAsync(candidate => candidate.Id == stored.Value.Id, cancellationToken)
            .ConfigureAwait(false);

        await audit
            .RecordAsync(
                new AuditEntry
                {
                    Action = AuditAction,
                    EntityType = AuditEntityType,
                    EntityId = file.Id.ToString(),
                    After = new
                    {
                        file.FileName,
                        file.ContentType,
                        file.ByteSize,
                        Visibility = file.Visibility.ToString(),
                        ScanState = file.ScanState.ToString(),
                    },
                },
                cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(MediaProjection.ToResponse(file, stored.Value));
    }
}

/// <param name="context">The Media data context.</param>
/// <param name="library">Builds URLs and renditions.</param>
internal sealed class ListMediaQueryHandler(MediaDbContext context, MediaLibrary library)
    : IQueryHandler<ListMediaQuery, PagedResult<MediaFileResponse>>
{
    public async Task<Result<PagedResult<MediaFileResponse>>> HandleAsync(
        ListMediaQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);
        var files = context.Files.AsNoTracking().Where(file => file.Status != StoredFileStatus.Deleted);

        if (Enum.TryParse<MediaVisibility>(query.Visibility, ignoreCase: true, out var visibility))
        {
            files = files.Where(file => file.Visibility == visibility);
        }

        if (!string.IsNullOrWhiteSpace(query.ContentType))
        {
            files = files.Where(file => file.ContentType == query.ContentType);
        }

        // Keyset on (created_at, id): the timestamp alone is not unique, and two files stored in
        // the same millisecond would otherwise straddle a page boundary and one would be skipped.
        if (Cursor.TryDecode(query.Cursor, out var key)
            && key.Split('|') is [var timestamp, var id]
            && DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, out var after)
            && Guid.TryParse(id, out var afterId))
        {
            files = files.Where(file =>
                file.CreatedAt < after || (file.CreatedAt == after && file.Id.CompareTo(afterId) < 0));
        }

        var page = await files
            .OrderByDescending(file => file.CreatedAt)
            .ThenByDescending(file => file.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var items = hasMore ? page[..size] : page;

        var next = hasMore
            ? Cursor.Encode(string.Create(
                CultureInfo.InvariantCulture,
                $"{items[^1].CreatedAt:O}|{items[^1].Id}"))
            : null;

        IReadOnlyList<MediaFileResponse> responses =
            [.. items.Select(file => MediaProjection.ToResponse(file, library.Project(file)))];

        return Result.Success(new PagedResult<MediaFileResponse>(responses, new PageInfo(size, next)));
    }
}

/// <param name="context">The Media data context.</param>
/// <param name="library">Builds URLs and renditions.</param>
internal sealed class GetMediaQueryHandler(MediaDbContext context, MediaLibrary library)
    : IQueryHandler<GetMediaQuery, MediaFileResponse>
{
    public async Task<Result<MediaFileResponse>> HandleAsync(GetMediaQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var file = await context.Files
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == query.FileId, cancellationToken)
            .ConfigureAwait(false);

        return file is null || file.Status == StoredFileStatus.Deleted
            ? Result.Failure<MediaFileResponse>(MediaErrors.NotFound)
            : Result.Success(MediaProjection.ToResponse(file, library.Project(file)));
    }
}

/// <param name="library">Mints the signed URL.</param>
/// <param name="options">Supplies the lifetime, so the response can state when it expires.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class GetMediaLinkQueryHandler(
    IMediaLibrary library,
    Microsoft.Extensions.Options.IOptions<KlaraHome.Infrastructure.Storage.StorageOptions> options,
    KlaraHome.SharedKernel.Time.IClock clock) : IQueryHandler<GetMediaLinkQuery, MediaLinkResponse>
{
    public async Task<Result<MediaLinkResponse>> HandleAsync(
        GetMediaLinkQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var url = await library.GetSignedUrlAsync(query.FileId, cancellationToken).ConfigureAwait(false);

        return url is null
            ? Result.Failure<MediaLinkResponse>(MediaErrors.NotFound)
            : Result.Success(new MediaLinkResponse(
                url,
                clock.UtcNow.AddMinutes(options.Value.SignedUrlMinutes)));
    }
}

/// <param name="media">Performs the retirement and the object delete.</param>
/// <param name="audit">Records it.</param>
internal sealed class DeleteMediaCommandHandler(MediaStorageService media, IAuditLogger audit)
    : ICommandHandler<DeleteMediaCommand>
{
    /// <summary>The action recorded in the audit trail for a deletion.</summary>
    public const string AuditAction = "media.file.deleted";

    public async Task<Result> HandleAsync(DeleteMediaCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await media.DeleteAsync(command.FileId, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return result;
        }

        await audit
            .RecordAsync(
                new AuditEntry
                {
                    Action = AuditAction,
                    EntityType = UploadMediaCommandHandler.AuditEntityType,
                    EntityId = command.FileId.ToString(),
                },
                cancellationToken)
            .ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Maps a registry row and its published projection onto the admin response.</summary>
internal static class MediaProjection
{
    /// <summary>Builds the admin-facing response.</summary>
    /// <param name="file">The registry row, which carries what only an administrator sees.</param>
    /// <param name="published">The published projection, which carries the URLs.</param>
    public static MediaFileResponse ToResponse(StoredFile file, MediaFile published)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(published);

        return new MediaFileResponse(
            file.Id,
            file.FileName,
            file.ContentType,
            file.ByteSize,
            file.Width,
            file.Height,
            file.Visibility.ToString(),
            file.Status.ToString(),
            file.ScanState.ToString(),
            file.OwnerType,
            published.Url,
            published.Variants,
            file.CreatedAt);
    }
}
