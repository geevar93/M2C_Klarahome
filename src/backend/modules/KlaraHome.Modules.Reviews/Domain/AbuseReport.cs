using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Reviews.Domain;

/// <summary>What was reported.</summary>
/// <remarks>
/// Three things, and they are the three things this module lets a stranger publish. A report against
/// anything else would be a report about a product or an order, which is a support ticket rather
/// than a moderation queue.
/// </remarks>
internal enum ReportTarget
{
    /// <summary>A review.</summary>
    Review = 0,

    /// <summary>A question.</summary>
    Question = 1,

    /// <summary>An answer.</summary>
    Answer = 2,
}

/// <summary>
/// Why somebody reported it.
/// </summary>
/// <remarks>
/// A closed list rather than free text, because the reason is what the queue is sorted and triaged
/// by. <see cref="Illegal"/> is deliberately its own value and not a kind of "offensive": in India
/// an intermediary's obligation to act on unlawful content is a statutory timeline
/// (docs/07-security-compliance.md §5), and a queue that could not separate those reports from
/// somebody objecting to bad language could not be worked to it.
/// </remarks>
internal enum ReportReason
{
    /// <summary>Advertising, a link farm, or the same text posted repeatedly.</summary>
    Spam = 0,

    /// <summary>Abusive, obscene or hateful.</summary>
    Offensive = 1,

    /// <summary>Nothing to do with the product it is attached to.</summary>
    Irrelevant = 2,

    /// <summary>States something about the product that is not true.</summary>
    Misleading = 3,

    /// <summary>Contains somebody's personal information.</summary>
    PersonalData = 4,

    /// <summary>Unlawful. Triaged first, and on a statutory clock.</summary>
    Illegal = 5,

    /// <summary>Something else, explained in the note.</summary>
    Other = 6,
}

/// <summary>Where a report stands.</summary>
internal enum ReportStatus
{
    /// <summary>Waiting to be looked at.</summary>
    Open = 0,

    /// <summary>Looked at, and the content was taken down.</summary>
    Upheld = 1,

    /// <summary>Looked at, and the content stays.</summary>
    Dismissed = 2,
}

/// <summary>
/// Somebody's complaint about something another shopper wrote
/// (docs/03-database-design.md §4.15).
/// </summary>
/// <remarks>
/// <para>
/// A row rather than a flag on the thing reported, and the distinction is what makes the queue
/// workable. Ten people reporting one review is one review and ten complaints; a boolean would lose
/// nine of them, and the count is the strongest signal a moderator has about what to look at first.
/// </para>
/// <para>
/// It is deliberately not a state of the content. Upholding a report is what rejects the review; the
/// report itself only records that somebody objected and what was decided. Keeping them apart means
/// a moderator can dismiss a complaint without touching the review, and reinstate a review without
/// erasing the history of it having been reported.
/// </para>
/// <para>
/// <see cref="ReporterId"/> is nullable because a shopper who is not signed in may still report
/// something. The trade is deliberate: unauthenticated reports can be submitted in volume, so they
/// are rate limited at the endpoint and the uniqueness rule that stops one person reporting the same
/// thing twice applies only to the signed-in case.
/// </para>
/// </remarks>
internal sealed class AbuseReport : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    /// <summary>The longest explanation a reporter may give.</summary>
    public const int MaxNoteLength = 1_000;

    private AbuseReport(Guid id, ReportTarget target, Guid targetId, ReportReason reason)
        : base(id)
    {
        Target = target;
        TargetId = targetId;
        Reason = reason;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private AbuseReport()
    {
    }

    /// <summary>What kind of thing was reported.</summary>
    public ReportTarget Target { get; private set; }

    /// <summary>Which one.</summary>
    public Guid TargetId { get; private set; }

    /// <summary>Why.</summary>
    public ReportReason Reason { get; private set; }

    /// <summary>What the reporter said, or null.</summary>
    public string? Note { get; private set; }

    /// <summary>Who reported it, or null when they were not signed in.</summary>
    public Guid? ReporterId { get; private set; }

    /// <summary>Where the report stands.</summary>
    public ReportStatus Status { get; private set; } = ReportStatus.Open;

    /// <summary>Who resolved it.</summary>
    public Guid? ResolvedBy { get; private set; }

    /// <summary>When it was resolved, in UTC.</summary>
    public DateTimeOffset? ResolvedAt { get; private set; }

    /// <summary>What the moderator concluded.</summary>
    public string? Resolution { get; private set; }

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

    /// <summary>Records a complaint.</summary>
    /// <param name="target">What kind of thing.</param>
    /// <param name="targetId">Which one.</param>
    /// <param name="reason">Why.</param>
    /// <param name="note">What the reporter said.</param>
    /// <param name="reporterId">Who reported it, when they were signed in.</param>
    public static AbuseReport Raise(
        ReportTarget target,
        Guid targetId,
        ReportReason reason,
        string? note,
        Guid? reporterId)
        => new(UuidV7.New(), target, Guard.NotEmpty(targetId), reason)
        {
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            ReporterId = reporterId,
        };

    /// <summary>Closes the report.</summary>
    /// <param name="upheld">Whether the complaint was accepted.</param>
    /// <param name="moderatorId">Who decided.</param>
    /// <param name="at">When.</param>
    /// <param name="resolution">What they concluded.</param>
    public void Resolve(bool upheld, Guid? moderatorId, DateTimeOffset at, string? resolution)
    {
        Status = upheld ? ReportStatus.Upheld : ReportStatus.Dismissed;
        ResolvedBy = moderatorId;
        ResolvedAt = at;
        Resolution = string.IsNullOrWhiteSpace(resolution) ? null : resolution.Trim();
    }
}
