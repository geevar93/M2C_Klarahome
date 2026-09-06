namespace KlaraHome.Modules.Content.Domain;

/// <summary>
/// What a page is for (docs/03-database-design.md §4.14).
/// </summary>
/// <remarks>
/// The type is not decoration: it decides which slug space the page lives in, whether the sitemap
/// lists it, and whether a merchandiser may delete it. <see cref="Home"/> is the one type of which
/// exactly one may be published at a time, and that is enforced rather than trusted — two published
/// home pages is a storefront that renders whichever row came back first.
/// </remarks>
internal enum PageType
{
    /// <summary>The storefront's front page. Exactly one may be published.</summary>
    Home = 0,

    /// <summary>A campaign or category landing page, reached by its own slug.</summary>
    Landing = 10,

    /// <summary>An ordinary informational page — about us, delivery, size guide.</summary>
    Static = 20,

    /// <summary>
    /// A policy page the law requires to be published: terms, privacy, returns, shipping.
    /// </summary>
    /// <remarks>
    /// Told apart from <see cref="Static"/> because it is treated differently in two places. It may
    /// not be archived while it is the only published copy of its slug — the Consumer Protection
    /// (E-Commerce) Rules 2020 require these to exist — and the storefront footer links them by slug
    /// rather than through a menu somebody might edit.
    /// </remarks>
    Legal = 30,

    /// <summary>A blog or lookbook article. Behind <c>content.blog</c>, which ships off.</summary>
    Blog = 40,
}

/// <summary>
/// Where a page stands (docs/02-domain-model.md §5.3).
/// </summary>
/// <remarks>
/// <para>
/// Numbered with gaps for the reason every other lifecycle in this platform is: a state inserted
/// between two others must not renumber the ones after it. The values are written to the database as
/// words, and the ordering is what an editor's worklist sorts by.
/// </para>
/// <para>
/// <see cref="Scheduled"/> is a real state rather than a published page with a future date, and the
/// difference matters. A scheduled page has been approved, its content is frozen, and the only thing
/// left is the clock; a published page with a future date would be a page the storefront had to
/// filter on at every read, and the one query that forgot to would put a campaign live early.
/// </para>
/// </remarks>
internal enum PageStatus
{
    /// <summary>Being written. Reachable only through the preview.</summary>
    Draft = 0,

    /// <summary>Written, and waiting for somebody who may publish to look at it.</summary>
    InReview = 10,

    /// <summary>Approved, frozen, and waiting for <c>scheduled_at</c> to pass.</summary>
    Scheduled = 20,

    /// <summary>Live. The storefront serves it.</summary>
    Published = 30,

    /// <summary>Taken down. It keeps its slug and its history, and it can go back up.</summary>
    Unpublished = 40,

    /// <summary>Retired. Terminal, and the slug is released for reuse.</summary>
    Archived = 90,
}

/// <summary>Who is asking for a page to move. The permission half of the transition table.</summary>
/// <remarks>
/// Two roles and a clock. Content has no vendor surface at all — a seller does not merchandise the
/// platform's storefront — so unlike the return machine there is no vendor actor here, and adding
/// one later would have to be a deliberate decision rather than a silent widening.
/// </remarks>
[Flags]
internal enum PageActor
{
    /// <summary>Nobody. Never granted.</summary>
    None = 0,

    /// <summary>Somebody who may write a page but not put it live.</summary>
    Editor = 1,

    /// <summary>Somebody who may put a page live and take it down again.</summary>
    Publisher = 2,

    /// <summary>The scheduler. No HTTP caller can claim it.</summary>
    System = 4,

    /// <summary>Anybody who works on content.</summary>
    Anyone = Editor | Publisher | System,
}

/// <summary>
/// The one place a page's transition table lives.
/// </summary>
/// <remarks>
/// <para>
/// One table saying both which edges exist and who may take them, exactly as the order, shipment and
/// return machines do. Nothing in this module moves a page by assigning a status: an editor's
/// submission, a publisher's approval and the scheduler's clock all come through here, which is what
/// stops the three drifting about what "scheduled" means.
/// </para>
/// <para>
/// The table is also what an admin screen draws its buttons from. A button for an edge that does not
/// exist is a support call, and a button for an edge the caller may not take is worse — it reads as
/// a permission fault when it is a workflow one.
/// </para>
/// <para>
/// The edge worth explaining is <see cref="PageStatus.Scheduled"/> back to
/// <see cref="PageStatus.Draft"/>. A scheduled page is frozen, so the only way to change one is to
/// unschedule it first; allowing an edit in place would mean the content that goes live is not the
/// content that was approved, and the approval would be worth nothing.
/// </para>
/// </remarks>
internal static class PageLifecycle
{
    /// <summary>The states in which the storefront serves the page.</summary>
    public static readonly IReadOnlyList<PageStatus> Live = [PageStatus.Published];

    /// <summary>The states in which a page's content may still be changed.</summary>
    /// <remarks>
    /// A published page is editable, and deliberately so: a typo on a legal page is fixed by fixing
    /// it, not by taking the page down first. What an edit to a published page does <em>not</em> do
    /// is publish itself — the change lands on the working copy, and going live is still a publish.
    /// </remarks>
    public static readonly IReadOnlyList<PageStatus> Editable =
    [
        PageStatus.Draft,
        PageStatus.InReview,
        PageStatus.Published,
        PageStatus.Unpublished,
    ];

    /// <summary>The states from which nothing further happens.</summary>
    public static readonly IReadOnlyList<PageStatus> Terminal = [PageStatus.Archived];

    /// <summary>The transition table: every edge, and who may take it.</summary>
    /// <remarks>
    /// A dictionary rather than a switch, so the machine can be read as data — the admin screen
    /// deciding which buttons to draw asks <see cref="NextFrom"/>, and gets the same answer the
    /// handler will give when the button is pressed.
    /// </remarks>
    private static readonly Dictionary<(PageStatus From, PageStatus To), PageActor> Edges = new()
    {
        // Writing it, and asking for it to be looked at. A publisher may skip the review of their
        // own page: requiring somebody to review their own work is a queue, not a control.
        [(PageStatus.Draft, PageStatus.InReview)] = PageActor.Editor | PageActor.Publisher,
        [(PageStatus.Draft, PageStatus.Published)] = PageActor.Publisher,
        [(PageStatus.Draft, PageStatus.Scheduled)] = PageActor.Publisher,
        [(PageStatus.Draft, PageStatus.Archived)] = PageActor.Publisher,

        // A review ends one of three ways: approved now, approved for later, or sent back.
        [(PageStatus.InReview, PageStatus.Published)] = PageActor.Publisher,
        [(PageStatus.InReview, PageStatus.Scheduled)] = PageActor.Publisher,
        [(PageStatus.InReview, PageStatus.Draft)] = PageActor.Editor | PageActor.Publisher,
        [(PageStatus.InReview, PageStatus.Archived)] = PageActor.Publisher,

        // The clock, and the change of mind. Only the scheduler may take the first: a publisher who
        // wants it live now publishes it, which is a different edge and a different audit line.
        [(PageStatus.Scheduled, PageStatus.Published)] = PageActor.System,
        [(PageStatus.Scheduled, PageStatus.Draft)] = PageActor.Publisher,

        // Live, and coming down again.
        [(PageStatus.Published, PageStatus.Unpublished)] = PageActor.Publisher,
        [(PageStatus.Published, PageStatus.Archived)] = PageActor.Publisher,

        // Down, and going back up — directly, or after another look.
        [(PageStatus.Unpublished, PageStatus.Published)] = PageActor.Publisher,
        [(PageStatus.Unpublished, PageStatus.Draft)] = PageActor.Editor | PageActor.Publisher,
        [(PageStatus.Unpublished, PageStatus.Scheduled)] = PageActor.Publisher,
        [(PageStatus.Unpublished, PageStatus.Archived)] = PageActor.Publisher,
    };

    /// <summary>Whether the storefront serves a page in this state.</summary>
    /// <param name="status">The status.</param>
    public static bool IsLive(PageStatus status) => Live.Contains(status);

    /// <summary>Whether a page in this state may have its content changed.</summary>
    /// <param name="status">The status.</param>
    public static bool IsEditable(PageStatus status) => Editable.Contains(status);

    /// <summary>Whether nothing further can happen to a page in this state.</summary>
    /// <param name="status">The status.</param>
    public static bool IsTerminal(PageStatus status) => Terminal.Contains(status);

    /// <summary>Whether the machine has this edge at all, whoever is asking.</summary>
    /// <param name="from">Where it is.</param>
    /// <param name="to">Where it is being moved to.</param>
    public static bool Exists(PageStatus from, PageStatus to) => Edges.ContainsKey((from, to));

    /// <summary>Whether this actor may take this edge.</summary>
    /// <param name="from">Where it is.</param>
    /// <param name="to">Where it is being moved to.</param>
    /// <param name="actor">Who is asking.</param>
    public static bool Allows(PageStatus from, PageStatus to, PageActor actor)
        => Edges.TryGetValue((from, to), out var allowed) && (allowed & actor) != PageActor.None;

    /// <summary>
    /// Every state this one can be moved to by this actor, which is what an admin screen draws.
    /// </summary>
    /// <param name="from">Where it is.</param>
    /// <param name="actor">Who is looking.</param>
    public static IReadOnlyList<PageStatus> NextFrom(PageStatus from, PageActor actor)
        =>
        [
            .. Edges
                .Where(edge => edge.Key.From == from && (edge.Value & actor) != PageActor.None)
                .Select(edge => edge.Key.To)
                .Order(),
        ];
}
