using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Catalog.Domain;

/// <summary>What a catalogue job does.</summary>
internal enum CatalogJobKind
{
    /// <summary>Loads products, variants and listings from an uploaded file.</summary>
    ProductImport = 0,

    /// <summary>Writes the catalogue out to a file the caller downloads.</summary>
    ProductExport = 1,
}

/// <summary>Where a catalogue job is in its run.</summary>
internal enum CatalogJobStatus
{
    /// <summary>Accepted and waiting for the worker to pick it up.</summary>
    Queued = 0,

    /// <summary>Being processed. Claimed by exactly one worker.</summary>
    Running = 1,

    /// <summary>Finished with every row applied.</summary>
    Succeeded = 2,

    /// <summary>Finished, but some rows were rejected. The report says which.</summary>
    PartiallySucceeded = 3,

    /// <summary>Could not run, or was abandoned after too many failed attempts.</summary>
    Failed = 4,
}

/// <summary>One rejected row of an import, as the validation report states it.</summary>
/// <param name="RowNumber">The line in the uploaded file, counting the header as line 1.</param>
/// <param name="Column">The column at fault, or null for a whole-row problem.</param>
/// <param name="Sku">The SKU on that row, when there was a readable one.</param>
/// <param name="Message">What is wrong, in words the person who wrote the file can act on.</param>
internal sealed record ImportRowError(int RowNumber, string? Column, string? Sku, string Message);

/// <summary>
/// A bulk import or export, and the report it produced (docs/04-api-specification.md §4).
/// </summary>
/// <remarks>
/// <para>
/// A row rather than a request that blocks: a thousand-SKU file is minutes of work, and an HTTP
/// request that holds a connection open for minutes is a request that dies behind a proxy with no
/// record of how far it got. The endpoint stores the upload and returns this id; the worker drains
/// the queue and writes progress here, and <c>GET /admin/jobs/{id}</c> reads it.
/// </para>
/// <para>
/// The file itself is not in this row. It goes to object storage under a key this row holds, which
/// keeps a 40 MB spreadsheet out of every query that lists jobs.
/// </para>
/// </remarks>
internal sealed class CatalogJob : AggregateRoot<Guid>, ITenantScoped, IAuditable, IVendorScoped
{
    private CatalogJob(Guid id, CatalogJobKind kind, string fileName)
        : base(id)
    {
        Kind = kind;
        FileName = Guard.NotNullOrWhiteSpace(fileName);
        Status = CatalogJobStatus.Queued;
        Errors = [];
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private CatalogJob()
    {
        FileName = string.Empty;
        Errors = [];
    }

    /// <summary>What the job does.</summary>
    public CatalogJobKind Kind { get; private set; }

    /// <summary>Where it is in its run.</summary>
    public CatalogJobStatus Status { get; private set; }

    /// <summary>The seller the job belongs to, or null when platform staff ran it.</summary>
    public Guid? VendorId { get; private set; }

    /// <summary>The name of the uploaded or produced file, as a human sees it.</summary>
    public string FileName { get; private set; }

    /// <summary>The object-storage key of the input file. Null for an export.</summary>
    public string? SourceKey { get; private set; }

    /// <summary>The object-storage key of the produced file. Null until an export finishes.</summary>
    public string? ResultKey { get; private set; }

    /// <summary>How many data rows the file has, once it has been counted.</summary>
    public int TotalRows { get; private set; }

    /// <summary>How many have been looked at.</summary>
    public int ProcessedRows { get; private set; }

    /// <summary>How many were applied.</summary>
    public int SucceededRows { get; private set; }

    /// <summary>How many were rejected.</summary>
    public int FailedRows { get; private set; }

    /// <summary>
    /// The validation report, capped at <see cref="MaxReportedErrors"/> entries.
    /// </summary>
    /// <remarks>
    /// Capped because a file with the wrong column order produces one error per row, and a report
    /// with ten thousand identical lines helps nobody and is a <c>jsonb</c> value nothing wants to
    /// read. <see cref="FailedRows"/> still counts them all.
    /// </remarks>
    public IReadOnlyList<ImportRowError> Errors
    {
        get => _errors;
        private set => _errors = [.. value];
    }

    private List<ImportRowError> _errors = [];

    /// <summary>A whole-job failure — an unreadable file, a storage outage.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>How many times a worker has picked this job up.</summary>
    public int Attempts { get; private set; }

    /// <summary>When a worker last claimed it.</summary>
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>When it stopped, whatever the outcome.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; private set; }

    /// <summary>Whether the job has stopped, whatever the outcome.</summary>
    public bool IsFinished => Status is CatalogJobStatus.Succeeded
        or CatalogJobStatus.PartiallySucceeded
        or CatalogJobStatus.Failed;

    /// <summary>The most rejected rows a report will name individually.</summary>
    public const int MaxReportedErrors = 500;

    /// <summary>Queues an import of an already-stored file.</summary>
    /// <param name="fileName">The uploaded file's name.</param>
    /// <param name="sourceKey">Its object-storage key.</param>
    /// <param name="vendorId">The seller who uploaded it, or null for platform staff.</param>
    public static CatalogJob QueueImport(string fileName, string sourceKey, Guid? vendorId)
        => new(UuidV7.New(), CatalogJobKind.ProductImport, fileName)
        {
            SourceKey = Guard.NotNullOrWhiteSpace(sourceKey),
            VendorId = vendorId,
        };

    /// <summary>Queues an export.</summary>
    /// <param name="fileName">The name the produced file will be offered under.</param>
    /// <param name="vendorId">The seller it is scoped to, or null for the whole catalogue.</param>
    public static CatalogJob QueueExport(string fileName, Guid? vendorId)
        => new(UuidV7.New(), CatalogJobKind.ProductExport, fileName) { VendorId = vendorId };

    /// <summary>
    /// Records where the uploaded file was stored, once the object is actually there.
    /// </summary>
    /// <remarks>
    /// The row is written before the object, because the key contains the row's own id and its
    /// tenant. A crash between the two therefore leaves an orphaned object rather than a job
    /// pointing at nothing — and an orphan is the cheaper of those to live with.
    /// </remarks>
    /// <param name="sourceKey">The object-storage key.</param>
    public void AttachSource(string sourceKey) => SourceKey = Guard.NotNullOrWhiteSpace(sourceKey);

    /// <summary>Claims the job for a worker.</summary>
    /// <param name="at">When.</param>
    public void Start(DateTimeOffset at)
    {
        Status = CatalogJobStatus.Running;
        Attempts++;
        StartedAt = at;
    }

    /// <summary>
    /// Puts the job back in the queue after an attempt failed for a reason that may not recur.
    /// </summary>
    /// <remarks>
    /// The counters are reset with it. An import that got a third of the way through before storage
    /// went away would otherwise start its retry with the earlier third already counted, and the
    /// report would say it applied twice as many rows as it did. The rows themselves are idempotent
    /// — a SKU that exists is updated — so re-running from the top is correct.
    /// </remarks>
    public void Requeue()
    {
        Status = CatalogJobStatus.Queued;
        ProcessedRows = 0;
        SucceededRows = 0;
        FailedRows = 0;
        _errors.Clear();
        StartedAt = null;
    }

    /// <summary>Records how many data rows the file turned out to have.</summary>
    /// <param name="total">The count.</param>
    public void CountRows(int total) => TotalRows = Math.Max(total, 0);

    /// <summary>Records that one row was applied.</summary>
    public void RowSucceeded()
    {
        ProcessedRows++;
        SucceededRows++;
    }

    /// <summary>Records that one row was rejected, and why.</summary>
    /// <param name="error">What was wrong with it.</param>
    public void RowFailed(ImportRowError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        ProcessedRows++;
        FailedRows++;

        if (_errors.Count < MaxReportedErrors)
        {
            _errors.Add(error);
        }
    }

    /// <summary>Finishes the job, choosing the outcome from what actually happened.</summary>
    /// <param name="at">When.</param>
    /// <param name="resultKey">The produced file's key, for an export.</param>
    public void Complete(DateTimeOffset at, string? resultKey = null)
    {
        Status = FailedRows == 0
            ? CatalogJobStatus.Succeeded
            : SucceededRows == 0 ? CatalogJobStatus.Failed : CatalogJobStatus.PartiallySucceeded;

        ResultKey = resultKey;
        CompletedAt = at;
    }

    /// <summary>Abandons the job.</summary>
    /// <param name="reason">Why it could not run.</param>
    /// <param name="at">When.</param>
    public void Fail(string reason, DateTimeOffset at)
    {
        Status = CatalogJobStatus.Failed;
        FailureReason = Guard.NotNullOrWhiteSpace(reason);
        CompletedAt = at;
    }
}
