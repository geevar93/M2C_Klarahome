using System.Text;
using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Errors;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Catalog.Application.Import;
using KlaraHome.Modules.Catalog.Infrastructure;
using KlaraHome.Modules.Catalog.Infrastructure.Import;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Catalog.Endpoints;

/// <summary>Query-string filters for the job listing.</summary>
/// <param name="Cursor">Opaque token from the previous page.</param>
/// <param name="Size">Page size.</param>
internal sealed record CatalogJobListFilter(string? Cursor, int? Size);

/// <summary>
/// Bulk import and export, and the job report that follows them
/// (docs/04-api-specification.md §4).
/// </summary>
/// <remarks>
/// Asynchronous by design. A thousand-SKU file is minutes of work, and a request that holds a
/// connection open for minutes is a request that dies behind a proxy with no record of how far it
/// got — so the upload returns a job id and <c>GET /admin/jobs/{id}</c> is where the progress and
/// the validation report live.
/// </remarks>
internal static class AdminCatalogJobEndpoints
{
    /// <summary>Maps the bulk surface beneath <c>/admin</c>.</summary>
    /// <param name="admin">The <c>/admin</c> group.</param>
    public static IEndpointRouteBuilder MapAdminCatalogJobEndpoints(this IEndpointRouteBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var products = admin.MapGroup("/products").WithTags("Catalog");

        products.MapPost("/import", ImportAsync)
            .WithName("adminProductImport")
            .WithSummary("Queues a CSV import and returns the job id to poll.")
            .RequirePermission(CatalogPermissions.ImportRun)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .DisableAntiforgery()
            .Produces<CatalogJobResponse>(StatusCodes.Status202Accepted);

        products.MapPost("/export", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new QueueProductExportCommand(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.Match(
                    job => Results.Accepted($"/api/v1/admin/jobs/{job.Id}", job),
                    error => error.ToProblemResult(context));
            })
            .WithName("adminProductExport")
            .WithSummary("Queues an export of the catalogue in the import's own column layout.")
            .RequirePermission(CatalogPermissions.ImportRun)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<CatalogJobResponse>(StatusCodes.Status202Accepted);

        products.MapGet("/import-template", (HttpContext context) =>
            {
                // The header row on its own. A merchandiser starting from the real column list makes
                // fewer mistakes than one starting from the documentation, and this is the file the
                // exporter and the importer both agree on.
                var csv = Csv.Write([[.. ImportColumns.All]]);

                return Results.File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", "import-template.csv");
            })
            .WithName("adminProductImportTemplate")
            .WithSummary("Downloads an empty import file with the correct column headers.")
            .RequirePermission(CatalogPermissions.ImportRun);

        var jobs = admin.MapGroup("/jobs").WithTags("Catalog");

        jobs.MapGet("/", async (
                [AsParameters] CatalogJobListFilter filter,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new ListCatalogJobsQuery(filter.Cursor, filter.Size);
                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCatalogJobsList")
            .WithSummary("Lists bulk jobs, newest first. A vendor caller sees only their own.")
            .RequirePermission(CatalogPermissions.ImportRun)
            .Produces<PagedResult<CatalogJobResponse>>();

        jobs.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetCatalogJobQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCatalogJobGet")
            .WithSummary("Reads a job's progress, its validation report, and an export's download link.")
            .RequirePermission(CatalogPermissions.ImportRun)
            .Produces<CatalogJobResponse>();

        return admin;
    }

    /// <summary>
    /// Reads the multipart body into memory and queues the import.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In memory, and the cap is enforced before the body is read rather than after: a file that
    /// turns out to be 900 MB must not have been buffered before anybody noticed. The declared
    /// length is a claim, so the copy is bounded too.
    /// </para>
    /// <para>
    /// <c>DisableAntiforgery</c> is on the endpoint because this is a bearer-token API with no
    /// cookie credential to forge — the same reasoning as the media upload.
    /// </para>
    /// </remarks>
    private static async Task<IResult> ImportAsync(
        IFormFile file,
        HttpContext context,
        IDispatcher dispatcher,
        IOptions<CatalogOptions> options)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (file is null || file.Length == 0)
        {
            return Application.CatalogErrors.BadImportFile("No file was uploaded.").ToProblemResult(context);
        }

        var limit = options.Value.MaxImportBytes;

        if (file.Length > limit)
        {
            return Application.CatalogErrors
                .BadImportFile($"The file is larger than the {limit} byte limit.")
                .ToProblemResult(context);
        }

        using var buffer = new MemoryStream((int)file.Length);

        await using (var upload = file.OpenReadStream())
        {
            await upload.CopyToAsync(buffer, context.RequestAborted).ConfigureAwait(false);
        }

        var command = new QueueProductImportCommand(file.FileName, buffer.ToArray());
        var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

        return result.Match(
            job => Results.Accepted($"/api/v1/admin/jobs/{job.Id}", job),
            error => error.ToProblemResult(context));
    }
}
