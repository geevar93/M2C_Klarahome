using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Search.Domain;

/// <summary>
/// A word this store's shoppers use, and the words the catalogue uses instead
/// (docs/03-database-design.md §4.13).
/// </summary>
/// <remarks>
/// <para>
/// Applied when a query is parsed rather than when a document is indexed, and that is the decision
/// worth explaining. PostgreSQL can hold a synonym dictionary inside a text-search configuration,
/// which is faster — and every change to it needs a file on the database server and a reindex of
/// the whole table. Expanding the <em>query</em> instead costs one <c>OR</c> per term and means a
/// merchandiser who notices that nobody finds "settee" can fix it from an admin screen in the time
/// it takes to type the word.
/// </para>
/// <para>
/// <see cref="IsBidirectional"/> is the difference between a synonym and a redirect. "Sofa" and
/// "couch" mean each other, so a search for either should find both; "cushion cover" should expand
/// to "pillowcase", but a search for pillowcases should not drag in every cushion in the store.
/// </para>
/// </remarks>
internal sealed class SearchSynonym : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    /// <summary>The longest term or expansion this table holds.</summary>
    public const int MaxTermLength = 64;

    /// <summary>The most expansions one term may carry.</summary>
    /// <remarks>
    /// Each one is a clause in the parsed query, so a term with fifty expansions is a query with
    /// fifty clauses — a slow search that a merchandiser did not know they were writing.
    /// </remarks>
    public const int MaxExpansions = 20;

    private SearchSynonym(Guid id, string term, List<string> expansions, bool isBidirectional)
        : base(id)
    {
        Term = term;
        Expansions = expansions;
        IsBidirectional = isBidirectional;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private SearchSynonym()
    {
        Term = string.Empty;
        Expansions = [];
    }

    /// <summary>The word a shopper types, normalised to lower case. Unique within a tenant.</summary>
    public string Term { get; private set; }

    /// <summary>The words it is also taken to mean, normalised to lower case.</summary>
    public List<string> Expansions { get; private set; }

    /// <summary>Whether each expansion also expands back to the term.</summary>
    public bool IsBidirectional { get; private set; }

    /// <summary>Whether it is applied at all. Switched off rather than deleted, so it can be tried.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Why a merchandiser added it, for the person who finds it two years later.</summary>
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

    /// <summary>Declares a synonym.</summary>
    /// <param name="term">The word a shopper types, already normalised and known to be free.</param>
    /// <param name="expansions">What it also means, already normalised.</param>
    /// <param name="isBidirectional">Whether the expansions expand back.</param>
    /// <param name="note">Why.</param>
    public static SearchSynonym Create(
        string term,
        IReadOnlyCollection<string> expansions,
        bool isBidirectional,
        string? note)
    {
        ArgumentNullException.ThrowIfNull(expansions);

        var synonym = new SearchSynonym(
            UuidV7.New(),
            Guard.MaxLength(Guard.NotNullOrWhiteSpace(term), MaxTermLength),
            [.. expansions],
            isBidirectional);

        synonym.Note = note;
        return synonym;
    }

    /// <summary>Changes what the term means.</summary>
    /// <param name="expansions">The new expansions, already normalised.</param>
    /// <param name="isBidirectional">Whether they expand back.</param>
    /// <param name="isActive">Whether the rule is applied.</param>
    /// <param name="note">Why.</param>
    public void Update(
        IReadOnlyCollection<string> expansions,
        bool isBidirectional,
        bool isActive,
        string? note)
    {
        ArgumentNullException.ThrowIfNull(expansions);

        Expansions = [.. expansions];
        IsBidirectional = isBidirectional;
        IsActive = isActive;
        Note = note;
    }
}

/// <summary>
/// A word this store wants ignored in a query (docs/03-database-design.md §4.13).
/// </summary>
/// <remarks>
/// <para>
/// PostgreSQL's <c>english</c> configuration already drops the grammatical stop words — "the",
/// "and", "of" — and this table is not a second copy of that list. It is for the words that are
/// noise <em>in this catalogue</em>: a homeware store gains nothing from indexing "buy", "online",
/// "cheap" or its own brand name, all of which appear in a large share of queries and match a large
/// share of the catalogue.
/// </para>
/// <para>
/// Dropped from the query, never from the document. A stop word removed at index time cannot be
/// restored without rebuilding the table, and the day somebody adds a product genuinely called
/// "Online" is the day that becomes a problem.
/// </para>
/// </remarks>
internal sealed class SearchStopWord : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    /// <summary>The longest stop word this table holds.</summary>
    public const int MaxWordLength = 32;

    private SearchStopWord(Guid id, string word)
        : base(id)
        => Word = word;

    /// <summary>Required by EF Core's materialiser.</summary>
    private SearchStopWord() => Word = string.Empty;

    /// <summary>The word, normalised to lower case. Unique within a tenant.</summary>
    public string Word { get; private set; }

    /// <summary>Whether it is applied at all.</summary>
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

    /// <summary>Declares a stop word.</summary>
    /// <param name="word">The word, already normalised and known to be free.</param>
    public static SearchStopWord Create(string word)
        => new(UuidV7.New(), Guard.MaxLength(Guard.NotNullOrWhiteSpace(word), MaxWordLength));

    /// <summary>Switches the rule on or off.</summary>
    /// <param name="isActive">Whether it is applied.</param>
    public void SetActive(bool isActive) => IsActive = isActive;
}
