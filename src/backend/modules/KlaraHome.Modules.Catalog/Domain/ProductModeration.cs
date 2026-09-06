using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Catalog.Domain;

/// <summary>What became of one submission to the moderation queue.</summary>
internal enum ModerationStatus
{
    /// <summary>Waiting for somebody to look at it.</summary>
    Pending = 0,

    /// <summary>Accepted. The product was published.</summary>
    Approved = 1,

    /// <summary>Refused, with a reason. The product went back to draft.</summary>
    Rejected = 2,

    /// <summary>Withdrawn by the seller before anybody reviewed it.</summary>
    Withdrawn = 3,
}

/// <summary>
/// One trip through the moderation queue (docs/03-database-design.md §4.4).
/// </summary>
/// <remarks>
/// <para>
/// A row per submission, not a column on the product. A product that has been rejected twice and
/// approved on the third attempt is the normal case, and the two rejections and their reasons are
/// exactly what an operator needs when the seller asks why. A status column would keep only the
/// last one.
/// </para>
/// <para>
/// Append-only in spirit but not marked <see cref="IAppendOnly"/>: the pending row is updated once,
/// when it is decided, and the optimistic concurrency token is what stops two moderators deciding
/// the same submission differently.
/// </para>
/// </remarks>
internal sealed class ProductModeration : Entity<Guid>, ITenantScoped, IVendorScoped
{
    private ProductModeration(Guid id, Guid productId)
        : base(id)
    {
        ProductId = Guard.NotEmpty(productId);
        Status = ModerationStatus.Pending;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private ProductModeration()
    {
    }

    /// <summary>The product being reviewed.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>The seller whose product it is, or null for a platform-owned one.</summary>
    public Guid? VendorId { get; private set; }

    /// <summary>Who submitted it.</summary>
    public Guid? SubmittedBy { get; private set; }

    /// <summary>When they submitted it.</summary>
    public DateTimeOffset SubmittedAt { get; private set; }

    /// <summary>What became of the submission.</summary>
    public ModerationStatus Status { get; private set; }

    /// <summary>Who decided it.</summary>
    public Guid? ReviewerId { get; private set; }

    /// <summary>When they decided.</summary>
    public DateTimeOffset? ReviewedAt { get; private set; }

    /// <summary>The reviewer's note. Required for a rejection, and shown to the seller.</summary>
    public string? Notes { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Puts a product in the queue.</summary>
    /// <param name="productId">The product.</param>
    /// <param name="vendorId">Its seller, or null.</param>
    /// <param name="submittedBy">Who submitted it.</param>
    /// <param name="at">When.</param>
    public static ProductModeration Submit(Guid productId, Guid? vendorId, Guid? submittedBy, DateTimeOffset at)
        => new(UuidV7.New(), productId)
        {
            VendorId = vendorId,
            SubmittedBy = submittedBy,
            SubmittedAt = at,
        };

    /// <summary>
    /// Records a decision, or returns false if this submission has already been decided.
    /// </summary>
    /// <param name="status">Approved, rejected or withdrawn.</param>
    /// <param name="reviewerId">Who decided.</param>
    /// <param name="notes">Their note.</param>
    /// <param name="at">When.</param>
    public bool Decide(ModerationStatus status, Guid? reviewerId, string? notes, DateTimeOffset at)
    {
        if (Status != ModerationStatus.Pending || status == ModerationStatus.Pending)
        {
            return false;
        }

        Status = status;
        ReviewerId = reviewerId;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        ReviewedAt = at;

        return true;
    }
}
