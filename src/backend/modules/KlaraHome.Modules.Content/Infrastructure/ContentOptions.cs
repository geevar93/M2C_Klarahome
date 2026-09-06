using System.ComponentModel.DataAnnotations;

namespace KlaraHome.Modules.Content.Infrastructure;

/// <summary>
/// What the Content module reads from configuration.
/// </summary>
/// <remarks>
/// Deliberately none of it is merchandising policy, and none of it is SEO policy either. The
/// canonical host, the robots directives, the title template and the organisation's identity all live
/// in the <c>seo</c> settings section, where an operator edits them without a deploy. What is left
/// here belongs to a deployment: how hard the two background sweeps are allowed to work, and the
/// ceilings that stop one caller making one request expensive for everybody.
/// </remarks>
internal sealed class ContentOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Content";

    /// <summary>Whether this process runs the publish scheduler. On in the worker, off in the API.</summary>
    /// <remarks>
    /// Two processes publishing the same page would not corrupt anything — the transition is
    /// idempotent and the second one finds the page already published — but it would double the work
    /// and make the audit trail ambiguous about which run did it.
    /// </remarks>
    public bool SchedulerEnabled { get; set; }

    /// <summary>
    /// How often the scheduler looks for pages that are due, in seconds.
    /// </summary>
    /// <remarks>
    /// A minute. It is the resolution a merchandiser thinks in — a sale that starts at midnight
    /// starting within sixty seconds of midnight is what "at midnight" means to everybody involved —
    /// and the query behind it is an index seek on a filtered index that is almost always empty.
    /// </remarks>
    [Range(15, 3_600)]
    public int SchedulerIntervalSeconds { get; set; } = 60;

    /// <summary>The most pages one scheduler pass will publish.</summary>
    /// <remarks>
    /// A campaign launch really can schedule forty pages for the same instant. Bounded so that one
    /// pass cannot hold a write transaction over the whole table, and resumable because the next pass
    /// is a minute away.
    /// </remarks>
    [Range(1, 500)]
    public int SchedulerBatchSize { get; set; } = 50;

    /// <summary>Whether this process refreshes rule-based collections. On in the worker.</summary>
    public bool CollectionRefreshEnabled { get; set; }

    /// <summary>How often the refresh sweep runs, in minutes.</summary>
    public int CollectionRefreshIntervalMinutes { get; set; } = 30;

    /// <summary>How many collections one sweep refreshes.</summary>
    [Range(1, 100)]
    public int CollectionRefreshBatchSize { get; set; } = 5;

    /// <summary>How old a collection's last refresh must be before the sweep picks it up, in hours.</summary>
    /// <remarks>
    /// The sweep is the safety net under the event handlers, not the way collections are kept
    /// current: a product going live updates the collections it belongs in within seconds. This
    /// catches the rule whose event was dropped and the rule that was edited while the worker was
    /// being deployed.
    /// </remarks>
    [Range(1, 720)]
    public int CollectionStaleAfterHours { get; set; } = 6;

    /// <summary>How many variants one page of the catalogue walk reads during a rule evaluation.</summary>
    /// <remarks>
    /// Evaluating a rule means walking the catalogue, because the conditions are about facts the
    /// catalogue owns and this module cannot query. A larger page is fewer round trips and a longer
    /// read; five hundred is the size the projection source itself clamps to.
    /// </remarks>
    [Range(50, 500)]
    public int CatalogWalkPageSize { get; set; } = 250;

    /// <summary>
    /// The most variants one rule evaluation will walk before it stops.
    /// </summary>
    /// <remarks>
    /// A ceiling rather than a promise. A rule that has matched its limit stops early anyway; this is
    /// what stops a rule that matches almost nothing from reading a two-hundred-thousand-row catalogue
    /// every half hour for ever.
    /// </remarks>
    [Range(1_000, 1_000_000)]
    public int MaxWalkedVariants { get; set; } = 50_000;

    /// <summary>The largest page of collection products this API will serve.</summary>
    [Range(1, 200)]
    public int MaxPageSize { get; set; } = 48;

    /// <summary>The most products one carousel block will resolve.</summary>
    /// <remarks>
    /// Every one of them is a card the storefront renders and an image the media library resolves. A
    /// carousel with a hundred products in it is a home page nobody scrolls to the end of and a
    /// payload everybody downloads.
    /// </remarks>
    [Range(1, 60)]
    public int MaxCarouselProducts { get; set; } = 24;

    /// <summary>
    /// Whether a redirect's hit counter is written when it fires.
    /// </summary>
    /// <remarks>
    /// On. It is one <c>UPDATE</c> on a row that is already in the buffer cache, on a path that only
    /// runs for a URL that would otherwise have been a 404 — and without it a merchandiser has no way
    /// to tell which of four hundred rules still matter. It is a switch because a store under a
    /// scanner sees a very large number of 404s.
    /// </remarks>
    public bool TrackRedirectHits { get; set; } = true;

    /// <summary>How long the storefront may cache the robots document, in seconds.</summary>
    [Range(0, 86_400)]
    public int RobotsCacheSeconds { get; set; } = 900;
}
