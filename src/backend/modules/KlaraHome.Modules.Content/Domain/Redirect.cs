using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Content.Domain;

/// <summary>
/// A permanent or temporary move, or a page that is simply gone
/// (docs/03-database-design.md §4.14).
/// </summary>
/// <remarks>
/// Three values and no more. Anything else in the 3xx range means something a storefront has no use
/// for, and 302 is here only because a campaign occasionally needs a URL that will point somewhere
/// else next month — a 301 for that is a redirect a browser caches for a year and an editor cannot
/// take back.
/// </remarks>
internal enum RedirectStatus
{
    /// <summary>Moved permanently. The default, and what a rename should almost always be.</summary>
    MovedPermanently = 301,

    /// <summary>Found. For a destination that is expected to change again.</summary>
    Found = 302,

    /// <summary>
    /// Gone. Not a redirect at all, and the honest answer for a product line that was discontinued.
    /// </summary>
    /// <remarks>
    /// A 404 says "we have no idea what this is"; a 410 says "this existed and it does not any more",
    /// and a crawler drops the URL from its index far faster for the second. It is the difference
    /// between a store whose search results are current and one still advertising last year's range.
    /// </remarks>
    Gone = 410,
}

/// <summary>
/// One rule of the redirect manager (docs/03-database-design.md §4.14).
/// </summary>
/// <remarks>
/// <para>
/// The unglamorous half of SEO and the one that pays for itself. Every slug a merchandiser changes
/// breaks every link to the old one — a crawler's index, a customer's bookmark, an affiliate's post
/// — and a row here is the difference between that traffic arriving and that traffic bouncing.
/// </para>
/// <para>
/// <see cref="FromPath"/> is stored normalised: lower case, leading slash, no trailing slash, no
/// query string. That normalisation is applied identically when a rule is written and when one is
/// looked up, which is the only way <c>/Sale/</c> and <c>/sale</c> can be the same rule. Dropping the
/// query string is deliberate — a redirect keyed on one would silently fail for the same link
/// carrying a campaign tag, which is exactly the link that gets shared.
/// </para>
/// <para>
/// <see cref="HitCount"/> is a counter and it is not transactional. It is incremented by the
/// storefront's own resolution call and it is allowed to lose a write under load, because what it is
/// for is telling a merchandiser which of four hundred rules are still doing anything — and that
/// question does not need the last one to be exact.
/// </para>
/// </remarks>
internal sealed class Redirect : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    /// <summary>The longest path either side of a rule may be.</summary>
    public const int MaxPathLength = 2_048;

    /// <summary>The longest note.</summary>
    public const int MaxNoteLength = 500;

    private Redirect(Guid id, string fromPath, string? toPath, RedirectStatus status)
        : base(id)
    {
        FromPath = fromPath;
        ToPath = toPath;
        Status = status;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Redirect() => FromPath = string.Empty;

    /// <summary>The path being asked for, normalised. Unique per tenant.</summary>
    public string FromPath { get; private set; }

    /// <summary>Where it goes. Null, and only null, for <see cref="RedirectStatus.Gone"/>.</summary>
    public string? ToPath { get; private set; }

    /// <summary>What the storefront answers with.</summary>
    public RedirectStatus Status { get; private set; }

    /// <summary>How many times it has fired. Approximate by design.</summary>
    public long HitCount { get; private set; }

    /// <summary>When it last fired, in UTC. Null for a rule nothing has ever asked for.</summary>
    public DateTimeOffset? LastHitAt { get; private set; }

    /// <summary>Whether it is applied at all. Switched off rather than deleted, so it can be tried.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Why it exists, for whoever finds it two years later.</summary>
    public string? Note { get; private set; }

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

    /// <summary>Declares a rule.</summary>
    /// <param name="fromPath">The path being asked for, already normalised and known to be free.</param>
    /// <param name="toPath">Where it goes, already normalised. Null for a 410.</param>
    /// <param name="status">What the storefront answers with.</param>
    /// <param name="note">Why.</param>
    public static Redirect Create(string fromPath, string? toPath, RedirectStatus status, string? note)
        => new(
            UuidV7.New(),
            Guard.MaxLength(Guard.NotNullOrWhiteSpace(fromPath), MaxPathLength),
            toPath,
            status)
        {
            Note = note,
        };

    /// <summary>Rewrites a rule's destination and its status. The path it matches is not editable.</summary>
    /// <remarks>
    /// Changing what a rule matches would be a different rule with the same id, and the hit count
    /// that came with it would then be counting something else. Delete it and add the other one.
    /// </remarks>
    /// <param name="toPath">Where it goes, already normalised.</param>
    /// <param name="status">What the storefront answers with.</param>
    /// <param name="isActive">Whether it is applied.</param>
    /// <param name="note">Why.</param>
    public void Update(string? toPath, RedirectStatus status, bool isActive, string? note)
    {
        ToPath = toPath;
        Status = status;
        IsActive = isActive;
        Note = note;
    }

    /// <summary>Records that the rule fired.</summary>
    /// <param name="hitAt">When.</param>
    public void RecordHit(DateTimeOffset hitAt)
    {
        HitCount++;
        LastHitAt = hitAt;
    }
}
