using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Reviews.Domain;

/// <summary>
/// Where a question or an answer stands (docs/03-database-design.md §4.15).
/// </summary>
/// <remarks>
/// The same three states a review has, and deliberately the same word for each. A store that
/// moderated reviews with one vocabulary and Q&amp;A with another would need two admin screens and
/// two mental models for one job.
/// </remarks>
internal enum PostStatus
{
    /// <summary>Written and waiting for a moderator.</summary>
    Pending = 0,

    /// <summary>Visible on the product page.</summary>
    Approved = 1,

    /// <summary>Refused. Invisible and kept.</summary>
    Rejected = 2,
}

/// <summary>Who wrote an answer, which is what the storefront labels it with.</summary>
/// <remarks>
/// The label matters more than it looks. "Answered by the seller" and "answered by another customer"
/// carry very different authority, and a page that did not distinguish them would let a shopper read
/// a guess as a specification.
/// </remarks>
internal enum AnswerAuthor
{
    /// <summary>Another shopper.</summary>
    Customer = 0,

    /// <summary>The seller who offers the product.</summary>
    Vendor = 1,

    /// <summary>The store's own staff.</summary>
    Store = 2,
}

/// <summary>
/// A shopper's question about a product (docs/03-database-design.md §4.15).
/// </summary>
/// <remarks>
/// <para>
/// Unlike a review, a question needs no purchase. The whole point of it is that somebody has not
/// bought the thing yet and wants to know something before they do, and a store that demanded a
/// receipt first would be answering questions nobody still had.
/// </para>
/// <para>
/// It is against a product rather than a variant, and against no seller at all. "Is the fabric
/// washable" has one answer whoever is selling it; where the answer genuinely differs by seller, the
/// answer says so.
/// </para>
/// <para>
/// <see cref="AnswerCount"/> is a cache of the approved answers, kept so a product page can order
/// questions by how well answered they are without joining. It is recomputed from the answers rather
/// than incremented, for the same reason the rating summary is.
/// </para>
/// </remarks>
internal sealed class Question : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    /// <summary>The longest question.</summary>
    public const int MaxBodyLength = 1_000;

    /// <summary>The longest note a moderator may leave.</summary>
    public const int MaxNoteLength = 500;

    private readonly List<Answer> _answers = [];

    private Question(Guid id, Guid productId, Guid customerId, string body)
        : base(id)
    {
        ProductId = productId;
        CustomerId = customerId;
        Body = body;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Question() => Body = string.Empty;

    /// <summary>The product asked about.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>Who asked.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>The name to display, captured when it was asked.</summary>
    public string? AuthorName { get; private set; }

    /// <summary>What they asked.</summary>
    public string Body { get; private set; }

    /// <summary>Where it stands.</summary>
    public PostStatus Status { get; private set; } = PostStatus.Pending;

    /// <summary>Who decided.</summary>
    public Guid? ModeratedBy { get; private set; }

    /// <summary>When they decided, in UTC.</summary>
    public DateTimeOffset? ModeratedAt { get; private set; }

    /// <summary>Why.</summary>
    public string? ModerationNote { get; private set; }

    /// <summary>When it became visible, in UTC.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>How many approved answers it has.</summary>
    public int AnswerCount { get; private set; }

    /// <summary>How many people have reported it.</summary>
    public int ReportCount { get; private set; }

    /// <summary>The answers, newest last.</summary>
    public IReadOnlyList<Answer> Answers => _answers;

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

    /// <summary>Asks a question.</summary>
    /// <param name="productId">The product.</param>
    /// <param name="customerId">Who is asking.</param>
    /// <param name="body">What they want to know.</param>
    /// <param name="authorName">The name to display.</param>
    public static Question Ask(Guid productId, Guid customerId, string body, string? authorName)
        => new(
            UuidV7.New(),
            Guard.NotEmpty(productId),
            Guard.NotEmpty(customerId),
            Guard.MaxLength(Guard.NotNullOrWhiteSpace(body).Trim(), MaxBodyLength))
        {
            AuthorName = authorName?.Trim(),
        };

    /// <summary>Makes it visible.</summary>
    /// <param name="moderatorId">Who approved it, or null for automatic approval.</param>
    /// <param name="at">When.</param>
    public void Approve(Guid? moderatorId, DateTimeOffset at)
    {
        Status = PostStatus.Approved;
        ModeratedBy = moderatorId;
        ModeratedAt = at;
        ModerationNote = null;
        PublishedAt ??= at;
    }

    /// <summary>Refuses it.</summary>
    /// <param name="moderatorId">Who refused it.</param>
    /// <param name="at">When.</param>
    /// <param name="note">Why.</param>
    public void Reject(Guid? moderatorId, DateTimeOffset at, string? note)
    {
        Status = PostStatus.Rejected;
        ModeratedBy = moderatorId;
        ModeratedAt = at;
        ModerationNote = note?.Trim();
    }

    /// <summary>Adds an answer.</summary>
    /// <param name="answer">The answer.</param>
    public void AddAnswer(Answer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        _answers.Add(answer);
    }

    /// <summary>Refreshes the cached count of approved answers.</summary>
    /// <param name="answers">How many there are.</param>
    public void RecordAnswerCount(int answers) => AnswerCount = Math.Max(0, answers);

    /// <summary>Refreshes the abuse-report cache.</summary>
    /// <param name="reports">How many open reports there are.</param>
    public void RecordReports(int reports) => ReportCount = Math.Max(0, reports);
}

/// <summary>
/// A reply to a question (docs/03-database-design.md §4.15).
/// </summary>
/// <remarks>
/// Part of the question's aggregate: an answer is never loaded, listed or moderated on its own, and
/// it has no meaning without the thing it answers. <see cref="AuthorType"/> is decided from the
/// writer's own claims when the answer is written and is not something the request may state — the
/// alternative is a shopper labelling their own guess as the seller's word.
/// </remarks>
internal sealed class Answer : Entity<Guid>, ITenantScoped, IAuditable
{
    /// <summary>The longest answer.</summary>
    public const int MaxBodyLength = 2_000;

    private Answer(Guid id, Guid questionId, string body, AnswerAuthor authorType)
        : base(id)
    {
        QuestionId = questionId;
        Body = body;
        AuthorType = authorType;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Answer() => Body = string.Empty;

    /// <summary>The question answered.</summary>
    public Guid QuestionId { get; private set; }

    /// <summary>Who wrote it, when they have an account.</summary>
    public Guid? UserId { get; private set; }

    /// <summary>The seller, when a seller answered.</summary>
    public Guid? VendorId { get; private set; }

    /// <summary>How the storefront labels the answer.</summary>
    public AnswerAuthor AuthorType { get; private set; }

    /// <summary>The name to display.</summary>
    public string? AuthorName { get; private set; }

    /// <summary>The answer.</summary>
    public string Body { get; private set; }

    /// <summary>Where it stands.</summary>
    public PostStatus Status { get; private set; } = PostStatus.Pending;

    /// <summary>Who decided.</summary>
    public Guid? ModeratedBy { get; private set; }

    /// <summary>When they decided, in UTC.</summary>
    public DateTimeOffset? ModeratedAt { get; private set; }

    /// <summary>When it became visible, in UTC.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>How many people have reported it.</summary>
    public int ReportCount { get; private set; }

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

    /// <summary>Writes an answer.</summary>
    /// <param name="questionId">The question.</param>
    /// <param name="body">The answer.</param>
    /// <param name="authorType">How it is labelled, decided from the writer's own claims.</param>
    /// <param name="userId">Who wrote it.</param>
    /// <param name="vendorId">Their seller, when they answered as one.</param>
    /// <param name="authorName">The name to display.</param>
    public static Answer Write(
        Guid questionId,
        string body,
        AnswerAuthor authorType,
        Guid? userId,
        Guid? vendorId,
        string? authorName)
        => new(
            UuidV7.New(),
            Guard.NotEmpty(questionId),
            Guard.MaxLength(Guard.NotNullOrWhiteSpace(body).Trim(), MaxBodyLength),
            authorType)
        {
            UserId = userId,
            VendorId = vendorId,
            AuthorName = authorName?.Trim(),
        };

    /// <summary>Makes it visible.</summary>
    /// <param name="moderatorId">Who approved it, or null for automatic approval.</param>
    /// <param name="at">When.</param>
    public void Approve(Guid? moderatorId, DateTimeOffset at)
    {
        Status = PostStatus.Approved;
        ModeratedBy = moderatorId;
        ModeratedAt = at;
        PublishedAt ??= at;
    }

    /// <summary>Refuses it.</summary>
    /// <param name="moderatorId">Who refused it.</param>
    /// <param name="at">When.</param>
    public void Reject(Guid? moderatorId, DateTimeOffset at)
    {
        Status = PostStatus.Rejected;
        ModeratedBy = moderatorId;
        ModeratedAt = at;
    }

    /// <summary>Refreshes the abuse-report cache.</summary>
    /// <param name="reports">How many open reports there are.</param>
    public void RecordReports(int reports) => ReportCount = Math.Max(0, reports);
}
