using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Content.Domain;

/// <summary>
/// Where on the storefront a banner appears.
/// </summary>
/// <remarks>
/// A closed list for the same reason the block types are one: the storefront has to know in advance
/// where it is going to put something. A placement the Angular shell has no slot for is a banner an
/// editor schedules, pays for and never sees.
/// </remarks>
internal enum BannerPlacement
{
    /// <summary>The strip above the header. Text, not an image — see <see cref="Banner.Message"/>.</summary>
    AnnouncementBar = 0,

    /// <summary>The rotating hero on the home page.</summary>
    HomeHero = 10,

    /// <summary>A strip partway down the home page.</summary>
    HomeStrip = 20,

    /// <summary>The masthead on a category listing page.</summary>
    CategoryHeader = 30,

    /// <summary>A panel beside the results on a listing page.</summary>
    ListingSidebar = 40,

    /// <summary>A strip on the product page, beneath the buy box.</summary>
    ProductStrip = 50,

    /// <summary>A strip in the basket — delivery promises, spend-more prompts.</summary>
    CartStrip = 60,
}

/// <summary>
/// Who a banner is shown to.
/// </summary>
/// <remarks>
/// Deliberately three coarse buckets rather than a segment engine. The targeting a store genuinely
/// needs on day one is "first-time visitors see the welcome offer and nobody else does", and that is
/// answerable from whether the caller is signed in. Anything finer needs the segment vocabulary the
/// feature-flag audience already has, and inventing a second one here would guarantee they disagreed.
/// </remarks>
[Flags]
internal enum BannerAudience
{
    /// <summary>Nobody. Never stored.</summary>
    None = 0,

    /// <summary>Visitors who are not signed in.</summary>
    Anonymous = 1,

    /// <summary>Signed-in customers.</summary>
    SignedIn = 2,

    /// <summary>Everybody. The default, and what almost every banner should be.</summary>
    Everyone = Anonymous | SignedIn,
}

/// <summary>
/// A scheduled, targeted promotional slot (docs/03-database-design.md §4.14).
/// </summary>
/// <remarks>
/// <para>
/// Not a block, and the distinction is worth stating because the two overlap. A block belongs to one
/// page and is published with it; a banner belongs to a <em>placement</em> and appears on every page
/// that has one, on its own timetable, without anybody republishing anything. A campaign that runs
/// for the last four days of a month is a banner. A hero that is part of what the home page
/// permanently is, is a block.
/// </para>
/// <para>
/// The announcement bar is a banner with a <see cref="Message"/> and no image, at the
/// <see cref="BannerPlacement.AnnouncementBar"/> placement. It is the same record because it is the
/// same three questions — what does it say, when does it run, who sees it — and a second table would
/// have needed its own scheduling, its own targeting and its own admin screen to answer them again.
/// </para>
/// <para>
/// <see cref="Priority"/> decides which of several eligible banners wins a placement, highest first,
/// and ties break on the most recently started. A placement that rotates renders them all in that
/// order; a placement that does not renders the first.
/// </para>
/// </remarks>
internal sealed class Banner : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    /// <summary>The longest internal name.</summary>
    public const int MaxNameLength = 160;

    /// <summary>The longest announcement message.</summary>
    /// <remarks>
    /// One line on a 360-pixel screen. Longer than this and the bar wraps to three lines and pushes
    /// the entire storefront down the page, which is how an announcement becomes a bounce.
    /// </remarks>
    public const int MaxMessageLength = 200;

    /// <summary>The longest alt text.</summary>
    public const int MaxAltTextLength = 240;

    /// <summary>The longest link target.</summary>
    public const int MaxLinkLength = 2_048;

    private Banner(Guid id, string name, BannerPlacement placement)
        : base(id)
    {
        Name = name;
        Placement = placement;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Banner() => Name = string.Empty;

    /// <summary>What an editor calls it. Never rendered.</summary>
    public string Name { get; private set; }

    /// <summary>Where it appears.</summary>
    public BannerPlacement Placement { get; private set; }

    /// <summary>The desktop image. Null on an announcement bar, required everywhere else.</summary>
    public Guid? MediaFileId { get; private set; }

    /// <summary>
    /// The image used below the mobile breakpoint, or null to use the desktop one.
    /// </summary>
    /// <remarks>
    /// A separate file rather than a crop. This platform is mobile-first and a 3:1 desktop banner
    /// scaled to a phone is a band of illegible text — the second file is how a designer says what
    /// the banner is at that width.
    /// </remarks>
    public Guid? MobileMediaFileId { get; private set; }

    /// <summary>The words, for an announcement bar. Null on an image banner.</summary>
    public string? Message { get; private set; }

    /// <summary>The alt text. Required whenever there is an image — it is an accessibility failure otherwise.</summary>
    public string? AltText { get; private set; }

    /// <summary>Where clicking it goes, or null for a banner that is not a link.</summary>
    public string? Link { get; private set; }

    /// <summary>The call-to-action wording, when the placement renders a button.</summary>
    public string? CtaLabel { get; private set; }

    /// <summary>Which of several eligible banners wins the placement. Highest first.</summary>
    public int Priority { get; private set; }

    /// <summary>When it starts, or null for "as soon as it is switched on".</summary>
    public DateTimeOffset? StartsAt { get; private set; }

    /// <summary>When it stops, or null for "until somebody switches it off".</summary>
    public DateTimeOffset? EndsAt { get; private set; }

    /// <summary>Who sees it.</summary>
    public BannerAudience Audience { get; private set; } = BannerAudience.Everyone;

    /// <summary>The editor's switch, independent of the window.</summary>
    public bool IsActive { get; private set; } = true;

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

    /// <summary>Opens a banner.</summary>
    /// <param name="name">What an editor calls it.</param>
    /// <param name="placement">Where it appears.</param>
    public static Banner Create(string name, BannerPlacement placement)
        => new(
            UuidV7.New(),
            Guard.MaxLength(Guard.NotNullOrWhiteSpace(name), MaxNameLength),
            placement);

    /// <summary>Rewrites everything about the banner except which placement it is in.</summary>
    /// <param name="name">What an editor calls it.</param>
    /// <param name="mediaFileId">The desktop image.</param>
    /// <param name="mobileMediaFileId">The mobile image.</param>
    /// <param name="message">The words, for an announcement bar.</param>
    /// <param name="altText">The alt text.</param>
    /// <param name="link">Where clicking it goes.</param>
    /// <param name="ctaLabel">The button's wording.</param>
    /// <param name="priority">Which banner wins the placement.</param>
    /// <param name="startsAt">When it starts.</param>
    /// <param name="endsAt">When it stops.</param>
    /// <param name="audience">Who sees it.</param>
    /// <param name="isActive">Whether it is switched on.</param>
    public void Describe(
        string name,
        Guid? mediaFileId,
        Guid? mobileMediaFileId,
        string? message,
        string? altText,
        string? link,
        string? ctaLabel,
        int priority,
        DateTimeOffset? startsAt,
        DateTimeOffset? endsAt,
        BannerAudience audience,
        bool isActive)
    {
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name), MaxNameLength);
        MediaFileId = mediaFileId;
        MobileMediaFileId = mobileMediaFileId;
        Message = message;
        AltText = altText;
        Link = link;
        CtaLabel = ctaLabel;
        Priority = priority;
        StartsAt = startsAt;
        EndsAt = endsAt;
        Audience = audience;
        IsActive = isActive;
    }

    /// <summary>Switches the banner on or off without touching its schedule.</summary>
    /// <param name="isActive">Whether it is switched on.</param>
    public void SetActive(bool isActive) => IsActive = isActive;

    /// <summary>Whether this banner should render for this caller at this instant.</summary>
    /// <param name="now">The instant to test.</param>
    /// <param name="audience">The caller's audience.</param>
    public bool IsLiveFor(DateTimeOffset now, BannerAudience audience)
        => IsActive
           && (StartsAt is null || StartsAt <= now)
           && (EndsAt is null || EndsAt > now)
           && (Audience & audience) != BannerAudience.None;
}
