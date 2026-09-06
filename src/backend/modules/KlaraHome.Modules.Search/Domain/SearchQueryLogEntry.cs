using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Search.Domain;

/// <summary>Where a query came from.</summary>
/// <remarks>
/// The two are counted separately because they mean different things. A search is a shopper telling
/// you what they want; a suggestion request is three letters of one, and mixing them would make the
/// most-searched-for report a list of prefixes.
/// </remarks>
internal static class SearchQuerySources
{
    /// <summary>A full result page.</summary>
    public const string Search = "search";

    /// <summary>A keystroke in the autocomplete box.</summary>
    public const string Suggest = "suggest";

    /// <summary>Every source this module records.</summary>
    public static readonly IReadOnlyList<string> All = [Search, Suggest];
}

/// <summary>
/// One thing a shopper asked for, and what came back (docs/03-database-design.md §4.13).
/// </summary>
/// <remarks>
/// <para>
/// The only table in this schema that holds a fact nobody else has. Everything else here is a copy
/// of the catalogue and can be rebuilt from it; what a shopper typed exists nowhere but this row,
/// and the queries that returned nothing are the most valuable rows in it — they are a buying team's
/// most direct evidence of what the store does not stock.
/// </para>
/// <para>
/// Partitioned monthly by <see cref="CreatedAt"/> and retained for a year
/// (docs/03-database-design.md §9). It is the highest-volume table outside the stock ledger — every
/// keystroke in the suggestion box is a candidate row — and a partitioned table is created
/// partitioned or not at all, so the shape is settled now rather than when the volume makes it
/// obvious.
/// </para>
/// <para>
/// <see cref="CustomerId"/> is recorded only when a shopper is signed in, and no anonymous
/// identifier beyond a session token is kept. What was searched for is personal data under the DPDP
/// Act, which is why the whole log is behind a switch an operator can throw
/// (<c>SearchSettings.LogQueries</c>) without turning search off.
/// </para>
/// </remarks>
internal sealed class SearchQueryLogEntry : Entity<Guid>, ITenantScoped, IPartitioned
{
    /// <summary>The longest query text stored. Longer ones are truncated rather than refused.</summary>
    public const int MaxQueryLength = 200;

    private SearchQueryLogEntry(
        Guid id,
        string queryText,
        string normalisedQuery,
        string source,
        DateTimeOffset createdAt)
        : base(id)
    {
        QueryText = queryText;
        NormalisedQuery = normalisedQuery;
        Source = source;
        CreatedAt = createdAt;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private SearchQueryLogEntry()
    {
        QueryText = string.Empty;
        NormalisedQuery = string.Empty;
        Source = SearchQuerySources.Search;
    }

    /// <summary>When it was asked. The partition key, and the first half of the primary key.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>What the shopper actually typed, trimmed and truncated but not otherwise altered.</summary>
    /// <remarks>
    /// Kept verbatim beside the normalised form because a report on what people type is only useful
    /// if it says what they typed. The normalised form is what the counts group by.
    /// </remarks>
    public string QueryText { get; private set; }

    /// <summary>The query after case folding, stop words and synonyms. What reports group on.</summary>
    public string NormalisedQuery { get; private set; }

    /// <summary>Whether it was a full search or a keystroke: one of <see cref="SearchQuerySources"/>.</summary>
    public string Source { get; private set; }

    /// <summary>How many rows matched. Zero is the interesting value.</summary>
    public int ResultCount { get; private set; }

    /// <summary>The filters that were applied with it, as a <c>jsonb</c> document, or null.</summary>
    /// <remarks>
    /// A zero-result search with three filters applied is a different problem from a zero-result
    /// search with none: the first is a filter combination nothing satisfies, and the second is a
    /// product the store does not carry.
    /// </remarks>
    public string? Filters { get; private set; }

    /// <summary>How long the search took, in milliseconds.</summary>
    public int DurationMs { get; private set; }

    /// <summary>The shopper, when they were signed in.</summary>
    public Guid? CustomerId { get; private set; }

    /// <summary>Their session, so a sequence of refinements can be read as one journey.</summary>
    public string? SessionId { get; private set; }

    /// <summary>
    /// Which result they clicked, one-based, or null when they clicked none.
    /// </summary>
    /// <remarks>
    /// The single most useful number in this table. A query whose clicks are all on the eighth
    /// result is a query whose ranking is wrong, and no amount of looking at the results by hand
    /// tells you that.
    /// </remarks>
    public int? ClickedPosition { get; private set; }

    /// <summary>What they clicked.</summary>
    public Guid? ClickedVariantId { get; private set; }

    /// <summary>When they clicked it.</summary>
    public DateTimeOffset? ClickedAt { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Records a query that has just been answered.</summary>
    /// <param name="queryText">What was typed.</param>
    /// <param name="normalisedQuery">What it became.</param>
    /// <param name="source">Search or suggestion.</param>
    /// <param name="resultCount">How many rows matched.</param>
    /// <param name="durationMs">How long it took.</param>
    /// <param name="filters">The filters applied, as <c>jsonb</c>, or null.</param>
    /// <param name="customerId">The shopper, when signed in.</param>
    /// <param name="sessionId">Their session.</param>
    /// <param name="createdAt">When it was asked.</param>
    public static SearchQueryLogEntry Record(
        string queryText,
        string normalisedQuery,
        string source,
        int resultCount,
        int durationMs,
        string? filters,
        Guid? customerId,
        string? sessionId,
        DateTimeOffset createdAt)
    {
        var trimmed = Guard.NotNull(queryText).Trim();

        var entry = new SearchQueryLogEntry(
            UuidV7.NewAt(createdAt),
            trimmed.Length > MaxQueryLength ? trimmed[..MaxQueryLength] : trimmed,
            normalisedQuery.Length > MaxQueryLength ? normalisedQuery[..MaxQueryLength] : normalisedQuery,
            source,
            createdAt)
        {
            ResultCount = Math.Max(resultCount, 0),
            DurationMs = Math.Max(durationMs, 0),
            Filters = filters,
            CustomerId = customerId,
            SessionId = sessionId,
        };

        return entry;
    }

    /// <summary>Records that the shopper clicked a result.</summary>
    /// <remarks>
    /// Last click wins, deliberately. A shopper who opens the third result, comes back and opens the
    /// first has told you the first was the better answer, and keeping the earlier click would say
    /// the opposite.
    /// </remarks>
    /// <param name="position">Which result, one-based.</param>
    /// <param name="variantId">What they clicked.</param>
    /// <param name="clickedAt">When.</param>
    public void RecordClick(int position, Guid variantId, DateTimeOffset clickedAt)
    {
        ClickedPosition = Math.Max(position, 1);
        ClickedVariantId = variantId;
        ClickedAt = clickedAt;
    }
}
