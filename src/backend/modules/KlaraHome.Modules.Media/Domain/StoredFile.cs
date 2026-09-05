using KlaraHome.Contracts.Media;
using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Media.Domain;

/// <summary>Whether a file may be served, and why not when it may not.</summary>
internal enum StoredFileStatus
{
    /// <summary>Uploaded, validated and available.</summary>
    Ready = 0,

    /// <summary>A scanner reported it as infected. The object stays, unreachable, for evidence.</summary>
    Quarantined = 1,

    /// <summary>Retired. The object is gone from the bucket; the row remains so ids resolve to nothing.</summary>
    Deleted = 2,
}

/// <summary>What is known about whether a file is safe.</summary>
internal enum ScanState
{
    /// <summary>Nothing scanned it. The honest v1 answer (docs/08-integrations.md §4).</summary>
    Skipped = 0,

    /// <summary>Queued for a scanner that has not answered yet.</summary>
    Pending = 1,

    /// <summary>A scanner examined it and found nothing.</summary>
    Clean = 2,

    /// <summary>A scanner found something.</summary>
    Infected = 3,
}

/// <summary>
/// A file this platform stores (docs/03-database-design.md §4.17).
/// </summary>
/// <remarks>
/// <para>
/// The registry row, not the bytes: the object lives in MinIO under <see cref="StorageKey"/>, and
/// everything that has to be known about it without fetching it lives here — what it is, how big,
/// how it was verified, whether it may be served and what it belongs to.
/// </para>
/// <para>
/// Nothing points at this row with a foreign key. Six other schemas hold a file id and none of them
/// may join to it, so <see cref="OwnerType"/> and <see cref="OwnerId"/> are the only trace of what
/// a file is for — recorded for the orphan sweep at Step 31, and never trusted as integrity.
/// </para>
/// </remarks>
internal sealed class StoredFile : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private StoredFile(
        Guid id,
        string storageKey,
        MediaVisibility visibility,
        string fileName,
        string contentType,
        long byteSize,
        string checksum)
        : base(id)
    {
        StorageKey = Guard.NotNullOrWhiteSpace(storageKey);
        Visibility = visibility;
        FileName = Guard.NotNullOrWhiteSpace(fileName);
        ContentType = Guard.NotNullOrWhiteSpace(contentType);
        ByteSize = byteSize;
        ChecksumSha256 = Guard.NotNullOrWhiteSpace(checksum);
        Status = StoredFileStatus.Ready;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private StoredFile()
    {
        StorageKey = string.Empty;
        FileName = string.Empty;
        ContentType = string.Empty;
        ChecksumSha256 = string.Empty;
    }

    /// <summary>The object key within its bucket. Unique per tenant per bucket.</summary>
    public string StorageKey { get; private set; }

    /// <summary>Which bucket it is in, and therefore who may read it.</summary>
    public MediaVisibility Visibility { get; private set; }

    /// <summary>The name it was uploaded under, offered back on download.</summary>
    public string FileName { get; private set; }

    /// <summary>The verified MIME type. What the bytes are, not what the uploader claimed.</summary>
    public string ContentType { get; private set; }

    /// <summary>Size in bytes.</summary>
    public long ByteSize { get; private set; }

    /// <summary>
    /// SHA-256 of the content, lowercase hex. Enough to prove a stored document is the one that
    /// was rendered — which is the question a disputed invoice eventually asks.
    /// </summary>
    public string ChecksumSha256 { get; private set; }

    /// <summary>Pixel width, for a raster image.</summary>
    public int? Width { get; private set; }

    /// <summary>Pixel height, for a raster image.</summary>
    public int? Height { get; private set; }

    /// <summary>Whether the file may be served.</summary>
    public StoredFileStatus Status { get; private set; }

    /// <summary>What is known about whether it is safe.</summary>
    public ScanState ScanState { get; private set; }

    /// <summary>When a scanner last answered.</summary>
    public DateTimeOffset? ScannedAt { get; private set; }

    /// <summary>What the file belongs to — <c>Product</c>, <c>Invoice</c>, <c>VendorKyc</c>.</summary>
    public string? OwnerType { get; private set; }

    /// <summary>The id of the thing it belongs to. A soft reference; never a foreign key.</summary>
    public Guid? OwnerId { get; private set; }

    /// <summary>The user who uploaded it, when a person did.</summary>
    public Guid? UploadedBy { get; private set; }

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

    /// <summary>Whether this file can currently be served to anybody.</summary>
    public bool IsServable => Status == StoredFileStatus.Ready;

    /// <summary>Registers a file that has been written to a bucket.</summary>
    /// <param name="storageKey">The object key.</param>
    /// <param name="visibility">Which bucket it went to.</param>
    /// <param name="fileName">The upload name.</param>
    /// <param name="contentType">The verified MIME type.</param>
    /// <param name="byteSize">Size in bytes.</param>
    /// <param name="checksum">Lowercase hex SHA-256 of the content.</param>
    public static StoredFile Register(
        string storageKey,
        MediaVisibility visibility,
        string fileName,
        string contentType,
        long byteSize,
        string checksum)
        => new(UuidV7.New(), storageKey, visibility, fileName, contentType, byteSize, checksum);

    /// <summary>Records the pixel dimensions read from the file's own header.</summary>
    /// <param name="width">Pixel width.</param>
    /// <param name="height">Pixel height.</param>
    public void DescribeImage(int width, int height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>Records what the file is for.</summary>
    /// <param name="ownerType">The owning entity type.</param>
    /// <param name="ownerId">The owning entity id.</param>
    public void AttachTo(string? ownerType, Guid? ownerId)
    {
        OwnerType = string.IsNullOrWhiteSpace(ownerType) ? null : ownerType;
        OwnerId = ownerId;
    }

    /// <summary>Records who uploaded it.</summary>
    /// <param name="userId">The uploading user.</param>
    public void UploadedByUser(Guid? userId) => UploadedBy = userId;

    /// <summary>Records a scanner's verdict.</summary>
    /// <param name="state">What the scanner found.</param>
    /// <param name="at">When it answered.</param>
    /// <remarks>
    /// An infected verdict quarantines the file rather than deleting it: the bytes are evidence,
    /// and somebody has to be able to see what was uploaded and by whom.
    /// </remarks>
    public void Scanned(ScanState state, DateTimeOffset at)
    {
        ScanState = state;
        ScannedAt = at;

        if (state == ScanState.Infected)
        {
            Status = StoredFileStatus.Quarantined;
        }
    }

    /// <summary>Retires the file. The row stays so that stored ids resolve to nothing, not to an error.</summary>
    public void Retire() => Status = StoredFileStatus.Deleted;
}
