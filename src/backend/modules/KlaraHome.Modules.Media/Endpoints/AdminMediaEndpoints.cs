using KlaraHome.Contracts.Media;
using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Errors;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Media.Application;
using KlaraHome.Modules.Media.Infrastructure;
using KlaraHome.Modules.Media.Infrastructure.Validation;
using KlaraHome.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Media.Endpoints;

/// <summary>
/// The media library's administrative surface (docs/04-api-specification.md §4).
/// </summary>
/// <remarks>
/// Admin-only, and there is no storefront counterpart. A public image is fetched straight from the
/// bucket or from imgproxy — routing image traffic through the API would put a .NET process in
/// front of every picture on every page, which is what a CDN exists to avoid.
/// </remarks>
internal static class AdminMediaEndpoints
{
    /// <summary>Permission required to upload or delete a file.</summary>
    public const string ManagePermission = "media.file.manage";

    /// <summary>Permission required to browse the library or read one entry.</summary>
    public const string ReadPermission = "media.file.read";

    /// <summary>Maps the admin surface beneath the versioned API group.</summary>
    /// <param name="endpoints">The versioned API group the module is handed.</param>
    public static IEndpointRouteBuilder MapAdminMediaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/admin/media")
            .WithTags("Media");

        group.MapGet("/", async (
                [AsParameters] MediaListFilter filter,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new ListMediaQuery(filter.Visibility, filter.ContentType, filter.Cursor, filter.Size);
                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);
                return result.ToOk(context);
            })
            .WithName("adminMediaList")
            .WithSummary("Lists stored files, newest first.")
            .RequirePermission(ReadPermission)
            .Produces<PagedResult<MediaFileResponse>>();

        group.MapPost("/", UploadAsync)
            .WithName("adminMediaUpload")
            .WithSummary("Uploads a file and returns its id, URL and responsive renditions.")
            .RequirePermission(ManagePermission)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .DisableAntiforgery()
            .Produces<MediaFileResponse>(StatusCodes.Status201Created);

        group.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetMediaQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminMediaGet")
            .WithSummary("Returns one file's registry entry.")
            .RequirePermission(ReadPermission)
            .Produces<MediaFileResponse>();

        group.MapGet("/{id:guid}/link", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetMediaLinkQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminMediaLink")
            .WithSummary("Mints a short-lived link to a private file.")
            .RequirePermission(ReadPermission)
            .Produces<MediaLinkResponse>();

        group.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new DeleteMediaCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("adminMediaDelete")
            .WithSummary("Retires a file and removes the object behind it.")
            .RequirePermission(ManagePermission)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite);

        return endpoints;
    }

    /// <summary>
    /// Reads the multipart body into memory and hands it to the upload command.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In memory rather than streamed to storage, and the size cap is enforced <em>while</em>
    /// reading rather than afterwards: the content has to be inspected and hashed before anything
    /// is written, and a request that turns out to be 900 MB must not have been buffered before
    /// anybody noticed.
    /// </para>
    /// <para>
    /// <c>DisableAntiforgery</c> is on the endpoint because this is a bearer-token API with no
    /// cookie credential to forge — the refresh cookie cannot authorise a request — and ASP.NET
    /// Core otherwise requires a token on every multipart form post.
    /// </para>
    /// </remarks>
    private static async Task<IResult> UploadAsync(
        IFormFile file,
        HttpContext context,
        IDispatcher dispatcher,
        IOptions<MediaOptions> options,
        string? visibility = null,
        string? ownerType = null,
        Guid? ownerId = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (file is null || file.Length == 0)
        {
            return MediaErrors.Empty.ToProblemResult(context);
        }

        var isPrivate = string.Equals(visibility, "private", StringComparison.OrdinalIgnoreCase);
        var limits = options.Value;

        // The intent is inferred from what the caller says the file is, and then verified against
        // the bytes: a "document" that is really a JPEG is refused by the inspector, not accepted
        // under the wrong limit.
        var intent = file.ContentType is not null
                     && file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? MediaIntent.Image
            : MediaIntent.Document;

        var limit = intent == MediaIntent.Image ? limits.MaxImageBytes : limits.MaxDocumentBytes;

        if (file.Length > limit)
        {
            return MediaErrors.TooLarge(limit).ToProblemResult(context);
        }

        using var buffer = new MemoryStream((int)file.Length);
        await using (var upload = file.OpenReadStream())
        {
            await upload.CopyToAsync(buffer, context.RequestAborted).ConfigureAwait(false);
        }

        var command = new UploadMediaCommand(
            buffer.ToArray(),
            file.FileName,
            intent,
            isPrivate ? MediaVisibility.Private : MediaVisibility.Public,
            ownerType,
            ownerId);

        var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

        return result.Match(
            stored => Results.Created($"{context.Request.Path}/{stored.Id}", stored),
            error => error.ToProblemResult(context));
    }
}

/// <summary>Query-string filter for the media listing.</summary>
/// <param name="Visibility">Restrict to <c>public</c> or <c>private</c>.</param>
/// <param name="ContentType">Restrict to one MIME type.</param>
/// <param name="Cursor">Opaque page token from the previous page.</param>
/// <param name="Size">Page size.</param>
internal sealed record MediaListFilter(string? Visibility, string? ContentType, string? Cursor, int? Size);
