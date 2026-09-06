using System.Text;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.Storage;
using KlaraHome.Modules.Catalog.Domain;
using KlaraHome.Modules.Catalog.Infrastructure;
using KlaraHome.Modules.Catalog.Infrastructure.Import;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Catalog.Application.Import;

/// <summary>One rejected row, as the API states it.</summary>
/// <param name="RowNumber">The line in the uploaded file, counting the header as line 1.</param>
/// <param name="Column">The column at fault, or null for a whole-row problem.</param>
/// <param name="Sku">The SKU on that row, when there was a readable one.</param>
/// <param name="Message">What is wrong, in words the person who wrote the file can act on.</param>
internal sealed record ImportErrorResponse(int RowNumber, string? Column, string? Sku, string Message);

/// <summary>A bulk job and its progress.</summary>
/// <param name="Id">The job.</param>
/// <param name="Kind">What it does.</param>
/// <param name="Status">Where it is in its run.</param>
/// <param name="FileName">The file's name, as a human sees it.</param>
/// <param name="TotalRows">How many data rows the file has.</param>
/// <param name="ProcessedRows">How many have been looked at.</param>
/// <param name="SucceededRows">How many were applied.</param>
/// <param name="FailedRows">How many were rejected.</param>
/// <param name="Errors">The validation report, capped.</param>
/// <param name="FailureReason">Why the whole job could not run.</param>
/// <param name="DownloadUrl">A short-lived link to the produced file, for a finished export.</param>
/// <param name="StartedAt">When a worker last claimed it.</param>
/// <param name="CompletedAt">When it stopped.</param>
/// <param name="CreatedAt">When it was queued.</param>
internal sealed record CatalogJobResponse(
    Guid Id,
    CatalogJobKind Kind,
    CatalogJobStatus Status,
    string FileName,
    int TotalRows,
    int ProcessedRows,
    int SucceededRows,
    int FailedRows,
    IReadOnlyList<ImportErrorResponse> Errors,
    string? FailureReason,
    string? DownloadUrl,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt);

/// <summary>Lists bulk jobs, newest first.</summary>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListCatalogJobsQuery(string? Cursor, int? Size) : IQuery<PagedResult<CatalogJobResponse>>;

/// <summary>Reads one job and its report.</summary>
/// <param name="JobId">The job.</param>
internal sealed record GetCatalogJobQuery(Guid JobId) : IQuery<CatalogJobResponse>;

/// <summary>Queues an import of an uploaded file.</summary>
/// <param name="FileName">The uploaded file's name.</param>
/// <param name="Content">Its bytes.</param>
internal sealed record QueueProductImportCommand(string FileName, byte[] Content) : ICommand<CatalogJobResponse>;

/// <summary>Queues an export of the catalogue.</summary>
internal sealed record QueueProductExportCommand : ICommand<CatalogJobResponse>;

/// <summary>Lists bulk jobs.</summary>
/// <param name="context">The Catalog data context.</param>
internal sealed class ListCatalogJobsQueryHandler(CatalogDbContext context)
    : IQueryHandler<ListCatalogJobsQuery, PagedResult<CatalogJobResponse>>
{
    public async Task<Result<PagedResult<CatalogJobResponse>>> HandleAsync(
        ListCatalogJobsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);

        // The global vendor filter has already confined a vendor caller to their own jobs.
        var jobs = context.Jobs.AsNoTracking().AsQueryable();

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            jobs = jobs.Where(job => job.Id.CompareTo(after) < 0);
        }

        var page = await jobs
            .OrderByDescending(job => job.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        // No download links in a list: minting one is an authorisation grant, and doing it for
        // twenty-five rows the caller may not open is twenty-five signatures nobody asked for.
        return Result.Success(new PagedResult<CatalogJobResponse>(
            [.. page.Select(job => CatalogJobProjection.ToResponse(job, null))],
            new PageInfo(size, hasMore ? Cursor.Encode(page[^1].Id.ToString()) : null)));
    }
}

/// <summary>Reads one job.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="projection">States the job and mints its download link.</param>
internal sealed class GetCatalogJobQueryHandler(CatalogDbContext context, CatalogJobProjection projection)
    : IQueryHandler<GetCatalogJobQuery, CatalogJobResponse>
{
    public async Task<Result<CatalogJobResponse>> HandleAsync(
        GetCatalogJobQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var job = await context.Jobs
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == query.JobId, cancellationToken)
            .ConfigureAwait(false);

        return job is null
            ? CatalogErrors.NotFound("job")
            : Result.Success(projection.WithLink(job));
    }
}

/// <summary>Queues an import.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="scope">Decides which seller the job belongs to.</param>
/// <param name="storage">Holds the uploaded file until a worker reads it.</param>
/// <param name="options">Supplies the size cap.</param>
/// <param name="audit">Records the upload.</param>
internal sealed class QueueProductImportCommandHandler(
    CatalogDbContext context,
    CatalogScope scope,
    IFileStorage storage,
    IOptions<CatalogOptions> options,
    IAuditLogger audit) : ICommandHandler<QueueProductImportCommand, CatalogJobResponse>
{
    /// <summary>The audited action for a queued import.</summary>
    public const string AuditAction = "catalog.import.queued";

    /// <summary>The entity type recorded against every job action.</summary>
    public const string AuditEntityType = "CatalogJob";

    public async Task<Result<CatalogJobResponse>> HandleAsync(
        QueueProductImportCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!storage.IsAvailable)
        {
            return CatalogErrors.StorageUnavailable;
        }

        if (command.Content.Length == 0)
        {
            return CatalogErrors.BadImportFile("The uploaded file is empty.");
        }

        if (command.Content.Length > options.Value.MaxImportBytes)
        {
            return CatalogErrors.BadImportFile(
                $"The file is larger than the {options.Value.MaxImportBytes} byte limit.");
        }

        // Read and checked here rather than in the worker: a header that is missing the SKU column
        // is a mistake the person who just pressed upload can fix in ten seconds, and telling them
        // two minutes later through a job report is a worse product.
        var text = Encoding.UTF8.GetString(command.Content);
        var rows = Csv.Parse(text);

        if (rows.Count < 2)
        {
            return CatalogErrors.BadImportFile("The file has no data rows beneath its header.");
        }

        if (!CsvRow.MapColumns(rows[0]).ContainsKey(ImportColumns.Sku))
        {
            return CatalogErrors.BadImportFile(
                $"The file has no '{ImportColumns.Sku}' column, which every row needs.");
        }

        var job = CatalogJob.QueueImport(command.FileName, "pending", scope.CallerVendorId);

        // The tenant is stamped on insert, so the key is settled after the row exists — which also
        // means a crash between the two leaves an orphaned object rather than a job pointing at
        // nothing, and an orphan is the cheaper of those to live with.
        context.Jobs.Add(job);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var key = $"catalog/imports/{job.TenantId:N}/{job.Id:N}.csv";

        using (var content = new MemoryStream(command.Content))
        {
            await storage
                .PutAsync(key, content, "text/csv; charset=utf-8", StorageVisibility.Private, cancellationToken)
                .ConfigureAwait(false);
        }

        job.AttachSource(key);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AuditEntityType,
                EntityId = job.Id.ToString(),
                After = new { job.FileName, Rows = rows.Count - 1, job.VendorId },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(CatalogJobProjection.ToResponse(job, null));
    }
}

/// <summary>Queues an export.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="scope">Decides which seller the export is scoped to.</param>
/// <param name="storage">Refuses when there is nowhere to put the result.</param>
internal sealed class QueueProductExportCommandHandler(
    CatalogDbContext context,
    CatalogScope scope,
    IFileStorage storage) : ICommandHandler<QueueProductExportCommand, CatalogJobResponse>
{
    public async Task<Result<CatalogJobResponse>> HandleAsync(
        QueueProductExportCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!storage.IsAvailable)
        {
            return CatalogErrors.StorageUnavailable;
        }

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var job = CatalogJob.QueueExport($"catalog-{stamp}.csv", scope.CallerVendorId);

        context.Jobs.Add(job);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(CatalogJobProjection.ToResponse(job, null));
    }
}

/// <summary>Turns jobs into responses, and mints the download link a finished export needs.</summary>
/// <param name="storage">Signs the link.</param>
internal sealed class CatalogJobProjection(IFileStorage storage)
{
    /// <summary>How long a download link stays valid.</summary>
    private static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(15);

    /// <summary>States a job.</summary>
    /// <param name="job">The job.</param>
    /// <param name="downloadUrl">A signed link, or null.</param>
    public static CatalogJobResponse ToResponse(CatalogJob job, string? downloadUrl)
    {
        ArgumentNullException.ThrowIfNull(job);

        return new CatalogJobResponse(
            job.Id,
            job.Kind,
            job.Status,
            job.FileName,
            job.TotalRows,
            job.ProcessedRows,
            job.SucceededRows,
            job.FailedRows,
            [.. job.Errors.Select(error => new ImportErrorResponse(
                error.RowNumber,
                error.Column,
                error.Sku,
                error.Message))],
            job.FailureReason,
            downloadUrl,
            job.StartedAt,
            job.CompletedAt,
            job.CreatedAt);
    }

    /// <summary>
    /// States a job, with a short-lived link when there is a file to download.
    /// </summary>
    /// <remarks>
    /// The link is minted only after the caller's authorisation has been settled — reaching this
    /// method means the vendor filter already let them see the job — because the URL carries no
    /// authorisation of its own beyond its expiry.
    /// </remarks>
    /// <param name="job">The job.</param>
    public CatalogJobResponse WithLink(CatalogJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (string.IsNullOrWhiteSpace(job.ResultKey) || !storage.IsAvailable)
        {
            return ToResponse(job, null);
        }

        var url = storage.GetSignedUrl(job.ResultKey!, StorageVisibility.Private, LinkLifetime, job.FileName);

        return ToResponse(job, url);
    }
}
