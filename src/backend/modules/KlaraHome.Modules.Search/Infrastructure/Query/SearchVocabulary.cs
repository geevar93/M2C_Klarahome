using System.Collections.Concurrent;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Search.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Search.Infrastructure.Query;

/// <summary>
/// The store's own words, as the parser needs them: two lookups and nothing else.
/// </summary>
/// <remarks>
/// Immutable and safe to share between requests, which is what makes it cacheable. The parser takes
/// one of these rather than a repository so that parsing a query is a pure function — the alternative
/// is a normaliser that opens a connection, and therefore a normaliser no test can call.
/// </remarks>
internal sealed class SearchVocabularySnapshot
{
    private readonly HashSet<string> _stopWords;
    private readonly Dictionary<string, IReadOnlyList<string>> _synonyms;

    /// <param name="stopWords">Words to drop, already folded.</param>
    /// <param name="synonyms">Term to expansions, already folded.</param>
    public SearchVocabularySnapshot(
        IEnumerable<string> stopWords,
        IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> synonyms)
    {
        ArgumentNullException.ThrowIfNull(stopWords);
        ArgumentNullException.ThrowIfNull(synonyms);

        _stopWords = new HashSet<string>(stopWords, StringComparer.Ordinal);
        _synonyms = new Dictionary<string, IReadOnlyList<string>>(synonyms, StringComparer.Ordinal);
    }

    /// <summary>A store that has configured nothing. PostgreSQL's own stop words still apply.</summary>
    public static SearchVocabularySnapshot Empty { get; } = new([], []);

    /// <summary>How many synonym rules are in force, for the diagnostics endpoint.</summary>
    public int SynonymCount => _synonyms.Count;

    /// <summary>How many stop words are in force.</summary>
    public int StopWordCount => _stopWords.Count;

    /// <summary>Whether this store ignores the word.</summary>
    /// <param name="token">A folded token.</param>
    public bool IsStopWord(string token) => _stopWords.Contains(token);

    /// <summary>What else the word is taken to mean. Empty when nothing.</summary>
    /// <param name="token">A folded token.</param>
    public IReadOnlyList<string> Expand(string token)
        => _synonyms.TryGetValue(token, out var expansions) ? expansions : [];
}

/// <summary>
/// Loads the store's vocabulary and keeps it for a short while.
/// </summary>
/// <remarks>
/// <para>
/// A singleton with a time-to-live rather than a per-request read. Every search parses a query and
/// every keystroke in the suggestion box is a search, so reading two tables on each one would make
/// the vocabulary — which changes when a merchandiser edits it, perhaps weekly — the most-read data
/// in the platform.
/// </para>
/// <para>
/// The TTL is the whole cache-invalidation strategy, deliberately. The alternative is a
/// notification from the write path to every process holding a copy, which is a distributed cache
/// invalidation problem bought for a benefit measured in seconds of staleness on a merchandising
/// change. An admin who wants their new synonym now is told, on the screen, how long it takes.
/// </para>
/// <para>
/// Keyed by tenant even though a deployment serves one, for the same reason every query filter is:
/// the day that stops being true, the thing that breaks must not be a cache silently serving one
/// store's synonyms to another.
/// </para>
/// </remarks>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="options">Supplies the time to live.</param>
internal sealed class SearchVocabularyCache(IClock clock, IOptions<SearchOptions> options)
{
    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();

    /// <summary>The cached vocabulary for a tenant, or null when there is none or it has expired.</summary>
    /// <param name="tenantId">The store.</param>
    public SearchVocabularySnapshot? Find(Guid tenantId)
    {
        if (!_entries.TryGetValue(tenantId, out var entry))
        {
            return null;
        }

        return entry.ExpiresAt > clock.UtcNow ? entry.Snapshot : null;
    }

    /// <summary>Replaces the cached vocabulary for a tenant.</summary>
    /// <param name="tenantId">The store.</param>
    /// <param name="snapshot">What was loaded.</param>
    public void Store(Guid tenantId, SearchVocabularySnapshot snapshot)
    {
        var ttl = TimeSpan.FromSeconds(Math.Max(options.Value.VocabularyCacheSeconds, 1));

        _entries[tenantId] = new Entry(snapshot, clock.UtcNow.Add(ttl));
    }

    /// <summary>Drops the cached vocabulary, so the next query reloads it.</summary>
    /// <remarks>
    /// Called by the write path in this process after a synonym or stop word changes. It is not a
    /// distributed invalidation and does not pretend to be: another process keeps its copy until the
    /// TTL expires, which is the trade this cache exists to make.
    /// </remarks>
    /// <param name="tenantId">The store.</param>
    public void Invalidate(Guid tenantId) => _entries.TryRemove(tenantId, out _);

    private sealed record Entry(SearchVocabularySnapshot Snapshot, DateTimeOffset ExpiresAt);
}

/// <summary>Reads the store's vocabulary, through the cache.</summary>
/// <param name="context">The Search data context.</param>
/// <param name="cache">The shared, short-lived copy.</param>
/// <param name="tenant">The ambient store.</param>
internal sealed class SearchVocabularyReader(
    SearchDbContext context,
    SearchVocabularyCache cache,
    ITenantContext tenant)
{
    /// <summary>The store's words, loading them if the cached copy has expired.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SearchVocabularySnapshot> GetAsync(CancellationToken cancellationToken)
    {
        if (cache.Find(tenant.TenantId) is { } cached)
        {
            return cached;
        }

        var stopWords = await context.StopWords
            .AsNoTracking()
            .Where(word => word.IsActive)
            .Select(word => word.Word)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var synonyms = await context.Synonyms
            .AsNoTracking()
            .Where(synonym => synonym.IsActive)
            .Select(synonym => new
            {
                synonym.Term,
                synonym.Expansions,
                synonym.IsBidirectional,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var expansions = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var synonym in synonyms)
        {
            Add(expansions, synonym.Term, synonym.Expansions);

            if (!synonym.IsBidirectional)
            {
                continue;
            }

            // Both directions, and each expansion also gains its siblings: if sofa means couch and
            // settee, then a shopper who types "couch" means settee too. Without the siblings, a
            // bidirectional rule would be three rules that half agree with each other.
            foreach (var expansion in synonym.Expansions)
            {
                Add(
                    expansions,
                    expansion,
                    [synonym.Term, .. synonym.Expansions.Where(other => !string.Equals(other, expansion, StringComparison.Ordinal))]);
            }
        }

        var snapshot = new SearchVocabularySnapshot(
            stopWords,
            expansions.Select(pair =>
                new KeyValuePair<string, IReadOnlyList<string>>(pair.Key, pair.Value)));

        cache.Store(tenant.TenantId, snapshot);

        return snapshot;
    }

    /// <summary>Drops this store's cached copy after a write.</summary>
    public void Invalidate() => cache.Invalidate(tenant.TenantId);

    /// <summary>Merges expansions for a term, keeping the set distinct and free of self-reference.</summary>
    /// <param name="target">The map being built.</param>
    /// <param name="term">The term.</param>
    /// <param name="values">What it expands to.</param>
    private static void Add(Dictionary<string, List<string>> target, string term, IEnumerable<string> values)
    {
        if (!target.TryGetValue(term, out var list))
        {
            list = [];
            target[term] = list;
        }

        foreach (var value in values)
        {
            // A term that expands to itself would put the same lexeme in the query twice, which is
            // harmless and looks like a bug in every explain plan anybody ever reads.
            if (!string.Equals(value, term, StringComparison.Ordinal) && !list.Contains(value, StringComparer.Ordinal))
            {
                list.Add(value);
            }
        }
    }
}
