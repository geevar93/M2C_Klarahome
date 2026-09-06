using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Content.Domain;

/// <summary>
/// One block on a page: a declared type, a position, and a configuration document the type's
/// schema decides the shape of (docs/03-database-design.md §4.14).
/// </summary>
/// <remarks>
/// <para>
/// A row rather than an element of the page's own JSON, and the reason is scheduling. A block
/// carries its own window — <see cref="StartsAt"/> and <see cref="EndsAt"/> — because a campaign
/// banner has to appear and disappear on its own timetable without anybody republishing the page
/// around it. A window on an element inside a JSON document would be a window nothing could index,
/// and the storefront would be filtering the whole home page in memory on every request.
/// </para>
/// <para>
/// <see cref="Config"/> is a JSON object held as text and validated against
/// <see cref="BlockCatalog"/> before it is ever stored. Validating on the way in is what keeps a
/// malformed block an editor's error message rather than a shopper's blank page — the storefront
/// receives only documents that have already been checked against the schema its components were
/// written for.
/// </para>
/// </remarks>
internal sealed class ContentBlock : Entity<Guid>, ITenantScoped
{
    /// <summary>The largest configuration document a single block may carry.</summary>
    /// <remarks>
    /// Generous for everything except custom HTML, which is the one type that can genuinely grow
    /// without limit, and the limit is here so that it cannot.
    /// </remarks>
    public const int MaxConfigLength = 64 * 1024;

    private ContentBlock(Guid id, Guid pageId, BlockType type, int position, string config)
        : base(id)
    {
        PageId = pageId;
        Type = type;
        Position = position;
        Config = config;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private ContentBlock() => Config = "{}";

    /// <summary>The page this block sits on.</summary>
    public Guid PageId { get; private set; }

    /// <summary>What kind of block it is. Decides how <see cref="Config"/> is read and rendered.</summary>
    public BlockType Type { get; private set; }

    /// <summary>Where it sits, ascending. Contiguous from zero after every reorder.</summary>
    public int Position { get; private set; }

    /// <summary>The block's configuration, a JSON object matching its type's schema.</summary>
    public string Config { get; private set; }

    /// <summary>Whether the block is rendered at all. An editor's switch, independent of the window.</summary>
    public bool IsVisible { get; private set; } = true;

    /// <summary>When the block starts appearing, or null for "as soon as the page is live".</summary>
    public DateTimeOffset? StartsAt { get; private set; }

    /// <summary>When it stops, or null for "until somebody removes it".</summary>
    public DateTimeOffset? EndsAt { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Adds a block to a page.</summary>
    /// <param name="pageId">The page.</param>
    /// <param name="type">The block type.</param>
    /// <param name="position">Where it sits.</param>
    /// <param name="config">Its configuration, already validated against the type's schema.</param>
    /// <param name="isVisible">Whether it is rendered.</param>
    /// <param name="startsAt">When it starts appearing.</param>
    /// <param name="endsAt">When it stops.</param>
    public static ContentBlock Create(
        Guid pageId,
        BlockType type,
        int position,
        string config,
        bool isVisible = true,
        DateTimeOffset? startsAt = null,
        DateTimeOffset? endsAt = null)
    {
        var block = new ContentBlock(
            UuidV7.New(),
            Guard.NotEmpty(pageId),
            type,
            position,
            Guard.MaxLength(Guard.NotNullOrWhiteSpace(config), MaxConfigLength))
        {
            IsVisible = isVisible,
            StartsAt = startsAt,
            EndsAt = endsAt,
        };

        return block;
    }

    /// <summary>Rewrites the block's configuration and its window.</summary>
    /// <param name="config">The new configuration, already validated.</param>
    /// <param name="isVisible">Whether it is rendered.</param>
    /// <param name="startsAt">When it starts appearing.</param>
    /// <param name="endsAt">When it stops.</param>
    public void Update(string config, bool isVisible, DateTimeOffset? startsAt, DateTimeOffset? endsAt)
    {
        Config = Guard.MaxLength(Guard.NotNullOrWhiteSpace(config), MaxConfigLength);
        IsVisible = isVisible;
        StartsAt = startsAt;
        EndsAt = endsAt;
    }

    /// <summary>Moves the block.</summary>
    /// <param name="position">Its new position.</param>
    public void MoveTo(int position) => Position = position;

    /// <summary>Whether the block should render at this instant.</summary>
    /// <remarks>
    /// A half-open window, like every other window on this platform: a block that starts at nine and
    /// ends at ten is gone at ten exactly. Two campaigns handing over at midnight must not both be
    /// live for the instant they share.
    /// </remarks>
    /// <param name="now">The instant to test.</param>
    public bool IsLiveAt(DateTimeOffset now)
        => IsVisible && (StartsAt is null || StartsAt <= now) && (EndsAt is null || EndsAt > now);
}

/// <summary>
/// One block as a caller means it, before the page has decided whether it is new or an edit.
/// </summary>
/// <remarks>
/// The type the page's own <see cref="ContentPage.SyncBlocks"/> speaks, and the reason it exists is
/// change tracking. A save sends the whole block list; the page has to work out which of those are
/// the blocks it already has and update those in place, because clearing the collection and adding
/// freshly built entities with the same keys is a delete and an insert of the same primary key in one
/// transaction — which the change tracker refuses outright.
/// </remarks>
/// <param name="Id">The block, when the caller is editing one that exists.</param>
/// <param name="Type">Its type.</param>
/// <param name="Config">Its configuration, already validated against the type's schema.</param>
/// <param name="IsVisible">Whether it is rendered.</param>
/// <param name="StartsAt">When it starts appearing.</param>
/// <param name="EndsAt">When it stops.</param>
internal sealed record BlockDraft(
    Guid? Id,
    BlockType Type,
    string Config,
    bool IsVisible,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt);

/// <summary>
/// A page, exactly as it stood at one publish (docs/03-database-design.md §4.14).
/// </summary>
/// <remarks>
/// <para>
/// The whole page, not a diff. A diff would be smaller and would make the one operation this table
/// exists for — restoring a page somebody broke, at four o'clock, quickly — depend on replaying
/// every version since the one wanted. A snapshot is a single read and a single write.
/// </para>
/// <para>
/// Append-only, which is why it carries <see cref="IAppendOnly"/>: a version that could be edited
/// would be a history that could be rewritten, and the only reason to keep a history is that it
/// cannot be. Rolling back does not delete anything either — it writes a <em>new</em> version whose
/// content is an old one's, so the rollback is itself in the history.
/// </para>
/// </remarks>
internal sealed class PageVersion : Entity<Guid>, ITenantScoped, IAppendOnly
{
    /// <summary>The largest snapshot a version may hold.</summary>
    public const int MaxSnapshotLength = 512 * 1024;

    private PageVersion(Guid id, Guid pageId, int version, string title, SeoMetadata seo, string blocks)
        : base(id)
    {
        PageId = pageId;
        Version = version;
        Title = title;
        Seo = seo;
        Blocks = blocks;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private PageVersion()
    {
        Title = string.Empty;
        Seo = new SeoMetadata();
        Blocks = "[]";
    }

    /// <summary>The page this is a version of.</summary>
    public Guid PageId { get; private set; }

    /// <summary>Which version it is. One-based, and unique within the page.</summary>
    public int Version { get; private set; }

    /// <summary>The page's title as it stood.</summary>
    public string Title { get; private set; }

    /// <summary>The page's SEO block as it stood.</summary>
    public SeoMetadata Seo { get; private set; }

    /// <summary>Every block as it stood, as a JSON array.</summary>
    public string Blocks { get; private set; }

    /// <summary>Why this version exists, in the words of whoever made it.</summary>
    public string? Note { get; private set; }

    /// <summary>The version this one was restored from, when it was a rollback.</summary>
    public int? RestoredFrom { get; private set; }

    /// <summary>When it was taken, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Who took it. Null for the scheduler.</summary>
    public Guid? CreatedBy { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Takes a snapshot.</summary>
    /// <param name="pageId">The page.</param>
    /// <param name="version">Which version this is.</param>
    /// <param name="title">Its title.</param>
    /// <param name="seo">Its SEO block. Cloned, so later edits cannot reach into the snapshot.</param>
    /// <param name="blocks">Its blocks, serialised as a JSON array.</param>
    /// <param name="note">Why.</param>
    /// <param name="restoredFrom">The version restored, when this snapshot is a rollback.</param>
    /// <param name="takenAt">When.</param>
    public static PageVersion Capture(
        Guid pageId,
        int version,
        string title,
        SeoMetadata seo,
        string blocks,
        string? note,
        int? restoredFrom,
        DateTimeOffset takenAt)
    {
        ArgumentNullException.ThrowIfNull(seo);

        return new PageVersion(
            UuidV7.New(),
            Guard.NotEmpty(pageId),
            Guard.Positive(version),
            Guard.MaxLength(Guard.NotNullOrWhiteSpace(title), ContentPage.MaxTitleLength),
            seo.Clone(),
            Guard.MaxLength(Guard.NotNullOrWhiteSpace(blocks), MaxSnapshotLength))
        {
            Note = note,
            RestoredFrom = restoredFrom,
            CreatedAt = takenAt,
        };
    }

    /// <summary>Records who took the snapshot.</summary>
    /// <param name="userId">The editor, or null for the scheduler.</param>
    public void By(Guid? userId) => CreatedBy = userId;
}

/// <summary>
/// A page of the storefront a merchandiser composes (docs/03-database-design.md §4.14).
/// </summary>
/// <remarks>
/// <para>
/// The aggregate is the page and its blocks together, and that boundary is the one thing worth
/// getting right here. A block is meaningless without its page — it has no URL, nothing links to it,
/// and its position is only defined relative to its siblings — so blocks are never loaded, saved or
/// reordered on their own. Every write goes through the page, which is what makes "the positions are
/// contiguous from zero" a fact rather than a hope.
/// </para>
/// <para>
/// A page carries two things that look like the same idea and are not.
/// <see cref="Status"/> is where it stands in the workflow; <see cref="ISoftDeletable.DeletedAt"/>
/// is whether the row exists at all. <see cref="PageStatus.Archived"/> is the ordinary end of a
/// page's life and leaves it visible in the admin history; deletion is for the draft somebody
/// created by mistake ten minutes ago, is refused once a page has ever been published, and hides the
/// row from every query including the admin's.
/// </para>
/// <para>
/// <see cref="Version"/> counts publishes, not edits. It is incremented when a snapshot is taken, so
/// the number on the page always names the newest row in <c>page_versions</c> — a counter that moved
/// on every keystroke would make "roll back to version 4" a question nobody could answer.
/// </para>
/// </remarks>
internal sealed class ContentPage : AggregateRoot<Guid>, ITenantScoped, IAuditable, ISoftDeletable
{
    /// <summary>The longest slug this table holds.</summary>
    public const int MaxSlugLength = 200;

    /// <summary>The longest title.</summary>
    public const int MaxTitleLength = 250;

    /// <summary>The longest summary — the blog excerpt and the listing card's copy.</summary>
    public const int MaxSummaryLength = 500;

    /// <summary>The most blocks one page may hold.</summary>
    /// <remarks>
    /// A page with more than this is not a page, it is a site, and every one of these blocks is
    /// resolved — media, products, categories — when the storefront renders it.
    /// </remarks>
    public const int MaxBlocks = 60;

    private readonly List<ContentBlock> _blocks = [];

    private ContentPage(Guid id, string slug, PageType type, string title)
        : base(id)
    {
        Slug = slug;
        Type = type;
        Title = title;
        Seo = new SeoMetadata();
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private ContentPage()
    {
        Slug = string.Empty;
        Title = string.Empty;
        Seo = new SeoMetadata();
    }

    /// <summary>The URL segment. Unique per tenant among pages that are not deleted.</summary>
    public string Slug { get; private set; }

    /// <summary>What the page is for.</summary>
    public PageType Type { get; private set; }

    /// <summary>The page's own title, used as the <c>&lt;h1&gt;</c> and the SEO fallback.</summary>
    public string Title { get; private set; }

    /// <summary>A one-paragraph summary: the blog excerpt, and the card copy on a listing.</summary>
    public string? Summary { get; private set; }

    /// <summary>Where the page stands.</summary>
    public PageStatus Status { get; private set; } = PageStatus.Draft;

    /// <summary>When it first went live, in UTC. Kept across an unpublish, for the sitemap.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>When the scheduler should put it live, in UTC. Set only while it is scheduled.</summary>
    public DateTimeOffset? ScheduledAt { get; private set; }

    /// <summary>When its content last changed, in UTC. What <c>lastmod</c> in the sitemap carries.</summary>
    /// <remarks>
    /// Separate from <see cref="IAuditable.UpdatedAt"/>, which moves whenever any column does. A
    /// crawler told that every page changed because somebody renamed one menu learns to ignore
    /// <c>lastmod</c> entirely, and then it is worth nothing on the day a page really does change.
    /// </remarks>
    public DateTimeOffset? ContentChangedAt { get; private set; }

    /// <summary>How many versions have been snapshotted. Names the newest row in the history.</summary>
    public int Version { get; private set; }

    /// <summary>What a crawler and a social card are told.</summary>
    public SeoMetadata Seo { get; private set; }

    /// <summary>The hero image for a blog card or a social preview, when the page has one.</summary>
    public Guid? CoverImageFileId { get; private set; }

    /// <summary>The blog's byline. Null on every other page type.</summary>
    public string? Author { get; private set; }

    /// <summary>Free tags, for a blog index. Lowercased on the way in.</summary>
    public List<string> Tags { get; private set; } = [];

    /// <summary>The blocks, in position order.</summary>
    public IReadOnlyList<ContentBlock> Blocks => _blocks;

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

    /// <inheritdoc />
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <inheritdoc />
    public Guid? DeletedBy { get; private set; }

    /// <summary>Whether this page has ever been live, and therefore may not be deleted.</summary>
    public bool HasBeenPublished => PublishedAt is not null;

    /// <summary>Opens a page.</summary>
    /// <param name="slug">Its URL segment, already normalised and known to be free.</param>
    /// <param name="type">What it is for.</param>
    /// <param name="title">Its title.</param>
    public static ContentPage Create(string slug, PageType type, string title)
        => new(
            UuidV7.New(),
            Guard.MaxLength(Guard.NotNullOrWhiteSpace(slug), MaxSlugLength),
            type,
            Guard.MaxLength(Guard.NotNullOrWhiteSpace(title), MaxTitleLength));

    /// <summary>Rewrites the page's own details, leaving its blocks and its status alone.</summary>
    /// <param name="slug">Its URL segment, already normalised and known to be free.</param>
    /// <param name="title">Its title.</param>
    /// <param name="summary">Its summary.</param>
    /// <param name="seo">What a crawler is told.</param>
    /// <param name="coverImageFileId">Its cover image.</param>
    /// <param name="author">The byline, for a blog page.</param>
    /// <param name="tags">Its tags, already normalised.</param>
    /// <param name="changedAt">When, so <c>lastmod</c> moves with the content and not with the row.</param>
    public void Describe(
        string slug,
        string title,
        string? summary,
        SeoMetadata seo,
        Guid? coverImageFileId,
        string? author,
        IReadOnlyCollection<string> tags,
        DateTimeOffset changedAt)
    {
        ArgumentNullException.ThrowIfNull(seo);
        ArgumentNullException.ThrowIfNull(tags);

        Slug = Guard.MaxLength(Guard.NotNullOrWhiteSpace(slug), MaxSlugLength);
        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(title), MaxTitleLength);
        Summary = summary;
        Seo = seo;
        CoverImageFileId = coverImageFileId;
        Author = author;
        Tags = [.. tags];
        ContentChangedAt = changedAt;
    }

    /// <summary>
    /// Brings the page's blocks into line with what the caller sent, renumbering them from zero.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The caller sends the whole list, because that is how a block editor works: somebody drags four
    /// blocks around and presses save once. What this method does with it is <em>reconcile</em>, not
    /// replace — a draft carrying the id of a block the page already has updates that block in place,
    /// one carrying an unknown id or none becomes a new block, and anything the list no longer
    /// mentions is dropped.
    /// </para>
    /// <para>
    /// Reconciling rather than clearing and rebuilding is not tidiness. Clearing the collection and
    /// adding freshly constructed entities with the same keys is a delete and an insert of the same
    /// primary key in one transaction, which the change tracker refuses; and even if it did not, every
    /// unchanged block would be rewritten on every save.
    /// </para>
    /// <para>
    /// A draft whose id names a block of a <em>different type</em> is treated as a new block. Changing
    /// a hero into a carousel is not an edit — none of its configuration survives the change — and
    /// letting the id carry across would leave a version snapshot pointing at something it never
    /// described.
    /// </para>
    /// </remarks>
    /// <param name="drafts">The blocks, in the order they should render.</param>
    /// <param name="changedAt">When.</param>
    public void SyncBlocks(IReadOnlyList<BlockDraft> drafts, DateTimeOffset changedAt)
    {
        ArgumentNullException.ThrowIfNull(drafts);

        var kept = new List<ContentBlock>(drafts.Count);

        for (var index = 0; index < drafts.Count; index++)
        {
            var draft = drafts[index];

            var existing = draft.Id is { } id
                ? _blocks.FirstOrDefault(block => block.Id == id && block.Type == draft.Type)
                : null;

            if (existing is null)
            {
                existing = ContentBlock.Create(
                    Id,
                    draft.Type,
                    index,
                    draft.Config,
                    draft.IsVisible,
                    draft.StartsAt,
                    draft.EndsAt);

                _blocks.Add(existing);
            }
            else
            {
                existing.Update(draft.Config, draft.IsVisible, draft.StartsAt, draft.EndsAt);
            }

            existing.MoveTo(index);
            kept.Add(existing);
        }

        _blocks.RemoveAll(block => !kept.Contains(block));

        ContentChangedAt = changedAt;
    }

    /// <summary>
    /// Moves the page, if the machine has the edge and the actor may take it.
    /// </summary>
    /// <remarks>
    /// The single door. Nothing else in this module assigns <see cref="Status"/>, which is what
    /// stops an editor's submission, a publisher's approval and the scheduler's clock drifting apart
    /// about what a state means.
    /// </remarks>
    /// <param name="next">Where it is being moved to.</param>
    /// <param name="actor">Who is asking.</param>
    /// <param name="occurredAt">When.</param>
    /// <param name="scheduledAt">
    /// When it should go live. Required for <see cref="PageStatus.Scheduled"/> and ignored otherwise.
    /// </param>
    /// <returns>Whether the move happened.</returns>
    public bool Transition(
        PageStatus next,
        PageActor actor,
        DateTimeOffset occurredAt,
        DateTimeOffset? scheduledAt = null)
    {
        if (!PageLifecycle.Allows(Status, next, actor))
        {
            return false;
        }

        Status = next;

        switch (next)
        {
            case PageStatus.Scheduled:
                ScheduledAt = scheduledAt;
                break;

            case PageStatus.Published:
                // First publish sets it; a republish does not move it. The sitemap wants the date
                // the URL started existing, and a page edited weekly would otherwise look brand new
                // to a crawler every week.
                PublishedAt ??= occurredAt;
                ScheduledAt = null;
                break;

            default:
                ScheduledAt = null;
                break;
        }

        return true;
    }

    /// <summary>Takes the next version number. Called only when a snapshot is actually written.</summary>
    public int NextVersion() => ++Version;

    /// <summary>Restores a snapshot's content onto the page.</summary>
    /// <remarks>
    /// It does not restore the snapshot's <em>status</em>, and that is deliberate: rolling back the
    /// wording of a live page must not take the page down, and restoring a version captured while
    /// the page was a draft must not silently publish it.
    /// </remarks>
    /// <param name="title">The title as it stood.</param>
    /// <param name="seo">The SEO block as it stood.</param>
    /// <param name="drafts">The blocks as they stood, already validated.</param>
    /// <param name="changedAt">When.</param>
    public void Restore(
        string title,
        SeoMetadata seo,
        IReadOnlyList<BlockDraft> drafts,
        DateTimeOffset changedAt)
    {
        ArgumentNullException.ThrowIfNull(seo);

        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(title), MaxTitleLength);
        Seo = seo;
        SyncBlocks(drafts, changedAt);
    }

    /// <summary>Hides the row from every query. Only ever reached for a page that never went live.</summary>
    /// <param name="deletedAt">When.</param>
    /// <param name="deletedBy">Who.</param>
    public void Delete(DateTimeOffset deletedAt, Guid? deletedBy)
    {
        DeletedAt = deletedAt;
        DeletedBy = deletedBy;
    }
}
