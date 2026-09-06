using System.ComponentModel.DataAnnotations;

namespace KlaraHome.Modules.Search.Infrastructure;

/// <summary>
/// What the Search module reads from configuration.
/// </summary>
/// <remarks>
/// Deliberately none of it is merchandising policy. The ranking weights, the price bands, the fuzzy
/// threshold and whether queries are logged all live in the <c>search</c> settings section, where an
/// operator edits them without a deploy. What is left here belongs to a deployment: which engine
/// this build talks to, how hard the background rebuild is allowed to work, and the ceilings that
/// stop one caller making one request expensive for everybody.
/// </remarks>
internal sealed class SearchOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Search";

    /// <summary>
    /// Which engine answers a query, by its key.
    /// </summary>
    /// <remarks>
    /// Blank — the default and the only supported value today — means PostgreSQL's own full text,
    /// which needs nothing configured and is what ADR-007 chose. A dedicated engine is named here
    /// <em>and</em> switched on with the <c>search.external-engine</c> flag; naming one whose
    /// credentials are blank falls back to PostgreSQL and says so in the log, rather than serving a
    /// store with no search at all (ADR-019).
    /// </remarks>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The dedicated engine's base URL, when one is configured.</summary>
    /// <remarks>
    /// Blank is the normal case — no dedicated engine — so the rule has to admit it. <c>[Url]</c>
    /// does not: it rejects the empty string, which would fail options validation on startup for
    /// every deployment that runs on PostgreSQL's own full text, which is all of them today.
    /// </remarks>
    [RegularExpression(
        @"^$|^https?://\S+$",
        ErrorMessage = "Search:BaseUrl must be blank, or an absolute http/https URL.")]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Its API key.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The index within it that holds this store's products.</summary>
    public string IndexName { get; set; } = "products";

    /// <summary>How long to wait on it before giving up, in seconds.</summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>The largest page of results this API will serve.</summary>
    [Range(1, 200)]
    public int MaxPageSize { get; set; } = 48;

    /// <summary>The most filter values one request may name for one facet.</summary>
    /// <remarks>
    /// A filter with two hundred brand ids in it is not a shopper, and each one is an element of an
    /// array the database has to match against. Bounded rather than refused: the extra values are
    /// dropped and the search still answers.
    /// </remarks>
    [Range(1, 200)]
    public int MaxFilterValues { get; set; } = 50;

    /// <summary>The most attributes one request may filter on at once.</summary>
    /// <remarks>
    /// Each filtered attribute adds a clause to the results query and a whole branch to the facet
    /// query, because that facet's counts have to be computed with its own filter lifted. Eight is
    /// far more than any storefront filter panel offers.
    /// </remarks>
    [Range(1, 32)]
    public int MaxAttributeFilters { get; set; } = 8;

    /// <summary>How long a loaded copy of the store's synonyms and stop words is kept, in seconds.</summary>
    /// <remarks>
    /// Sixty. Long enough that the vocabulary is not the most-read table in the platform, short
    /// enough that a merchandiser who adds a synonym sees it work before they have finished
    /// wondering whether it did.
    /// </remarks>
    [Range(1, 3600)]
    public int VocabularyCacheSeconds { get; set; } = 60;

    /// <summary>Whether the background reindex sweep runs in this process.</summary>
    /// <remarks>
    /// Off in the API and on in the worker, exactly as every sweeper before it. Two processes
    /// rebuilding the same rows would not corrupt anything — the write is an upsert keyed on the
    /// variant — but it would double the load on the catalogue for no benefit at all.
    /// </remarks>
    public bool ReindexEnabled { get; set; }

    /// <summary>How often the sweep looks for rows that have fallen behind, in minutes.</summary>
    public int ReindexIntervalMinutes { get; set; } = 15;

    /// <summary>How many variants one sweep rebuilds.</summary>
    /// <remarks>
    /// A bound rather than a target. The sweep runs again shortly; a pass that tried to rebuild a
    /// fifty-thousand-product catalogue in one go would hold a read on the whole of it.
    /// </remarks>
    [Range(1, 5000)]
    public int ReindexBatchSize { get; set; } = 200;

    /// <summary>
    /// How stale a row may be before the sweep rebuilds it, in hours.
    /// </summary>
    /// <remarks>
    /// The safety net under the event pipeline rather than the way the index is normally kept
    /// current. Events do that, within seconds; this catches the row whose event was dropped, whose
    /// consumer threw, or that was written while a module was being deployed.
    /// </remarks>
    [Range(1, 720)]
    public int StaleAfterHours { get; set; } = 24;

    /// <summary>The largest number of rows one full rebuild will walk before stopping.</summary>
    /// <remarks>
    /// A rebuild is resumable — it walks the catalogue by variant and records where it reached — so
    /// this is a ceiling on one run rather than on the job.
    /// </remarks>
    [Range(1, 1_000_000)]
    public int MaxRebuildVariants { get; set; } = 100_000;

    /// <summary>How many query-log rows an admin report will return.</summary>
    [Range(1, 500)]
    public int MaxReportRows { get; set; } = 100;
}
