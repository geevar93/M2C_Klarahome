using System.Data;
using System.Data.Common;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Search.Application;
using KlaraHome.Modules.Search.Infrastructure.Persistence;
using KlaraHome.Modules.Search.Infrastructure.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Search.Infrastructure.Engine;

/// <summary>
/// The engine this platform ships with: PostgreSQL's own full text, with trigrams behind it
/// (ADR-007, ADR-019).
/// </summary>
/// <remarks>
/// <para>
/// Raw SQL rather than LINQ, and that is the one place in this codebase where that is the right
/// answer. The statement's shape depends on which filters were asked for; it uses <c>tsvector</c>
/// containment, <c>ts_rank_cd</c>, trigram similarity, lateral <c>jsonb</c> expansion and a
/// seven-branch union — none of which EF can express, and all of which are the reason the search is
/// fast. docs/01-architecture.md §4 anticipates exactly this: EF for writes and aggregates, raw SQL
/// where the query shape matters.
/// </para>
/// <para>
/// It reads through the EF context's own connection, so a search inside a transaction sees that
/// transaction, and no second connection is opened per request. The tenant is a parameter rather
/// than a query filter, for the obvious reason: raw SQL does not get one, so it is written out and
/// it is the first predicate on every statement here.
/// </para>
/// <para>
/// Three statements per page and one per subsequent page. The results, the total and the facets are
/// separate because they have genuinely different shapes; the facets are computed only when asked
/// for, which the application layer does on the first page and never again while the shopper pages
/// through it.
/// </para>
/// </remarks>
/// <param name="context">The Search data context, for its connection.</param>
/// <param name="tenant">The ambient store.</param>
/// <param name="logger">Reports slow and empty searches.</param>
internal sealed partial class PostgresSearchEngine(
    SearchDbContext context,
    ITenantContext tenant,
    ILogger<PostgresSearchEngine> logger) : ISearchEngine
{
    /// <summary>The key this engine is selected by. Also the fallback when nothing is configured.</summary>
    public const string EngineKey = "postgres";

    /// <inheritdoc />
    public string Key => EngineKey;

    /// <inheritdoc />
    /// <remarks>
    /// Always. It needs no credentials, no second container and no network hop — which is the whole
    /// argument ADR-007 made for starting here, and the reason a deployment can never end up with no
    /// search at all.
    /// </remarks>
    public bool IsConfigured => true;

    /// <inheritdoc />
    public async Task<SearchResults> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parameters = new SqlParameters();
        var predicates = SearchSqlBuilder.Predicates(request, tenant.TenantId, parameters);
        var score = SearchSqlBuilder.Score(request, parameters);
        var hitsSql = SearchSqlBuilder.Hits(request, predicates, score, parameters);

        var connection = context.Database.GetDbConnection();
        var opened = false;

        if (connection.State != ConnectionState.Open)
        {
            await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            opened = true;
        }

        try
        {
            if (request.Fuzzy)
            {
                await ApplySimilarityThresholdAsync(connection, request.Settings, cancellationToken)
                    .ConfigureAwait(false);
            }

            var (items, cursor) = await ReadHitsAsync(connection, hitsSql, parameters, request, cancellationToken)
                .ConfigureAwait(false);

            var total = await ReadTotalAsync(
                    connection,
                    SearchSqlBuilder.Total(predicates),
                    parameters,
                    cancellationToken)
                .ConfigureAwait(false);

            var facets = request.IncludeFacets
                ? await ReadFacetsAsync(connection, request, predicates, parameters, cancellationToken)
                    .ConfigureAwait(false)
                : [];

            Matched(logger, items.Count, total, request.Fuzzy ? "fuzzy" : "exact");

            return new SearchResults(items, total, facets, cursor);
        }
        finally
        {
            if (opened)
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchSuggestion>> SuggestAsync(
        NormalizedQuery query,
        int limit,
        SearchSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(settings);

        var parameters = new SqlParameters();
        var sql = SearchSqlBuilder.Suggestions(query, tenant.TenantId, limit, parameters);

        var connection = context.Database.GetDbConnection();
        var opened = false;

        if (connection.State != ConnectionState.Open)
        {
            await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            opened = true;
        }

        try
        {
            await using var command = Command(connection, sql, parameters);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            var suggestions = new List<SearchSuggestion>(limit);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var text = reader.IsDBNull(1) ? null : reader.GetString(1);

                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                suggestions.Add(new SearchSuggestion(
                    reader.GetString(0),
                    text,
                    ReadNullable(reader, 2, static (row, index) => row.GetString(index)),
                    ReadNullable(reader, 3, static (row, index) => row.GetGuid(index)),
                    ReadNullable(reader, 4, static (row, index) => row.GetGuid(index)),
                    ReadNullable(reader, 5, static (row, index) => row.GetDecimal(index))));
            }

            return suggestions;
        }
        finally
        {
            if (opened)
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Sets pg_trgm's similarity threshold on this connection to the store's configured value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>%</c> operator is what uses the trigram index, and it compares against a session
    /// setting rather than a value in the query. Without this, an operator who lowered the threshold
    /// to catch more typos would find the index quietly pruning at the server default and nothing
    /// changing.
    /// </para>
    /// <para>
    /// It is session state on a pooled connection, which is normally worth avoiding. It is safe here
    /// because it is set immediately before every query that reads it, and because no other query in
    /// this platform uses the operator that consults it — so a connection carrying a leftover value
    /// changes nothing.
    /// </para>
    /// </remarks>
    /// <param name="connection">The open connection.</param>
    /// <param name="settings">The store's ranking policy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task ApplySimilarityThresholdAsync(
        DbConnection connection,
        SearchSettings settings,
        CancellationToken cancellationToken)
    {
        var parameters = new SqlParameters();
        var sql = $"SELECT set_limit({parameters.Add(settings.FuzzyThreshold)}::real)";

        await using var command = Command(connection, sql, parameters);

        await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads a page of results, and the cursor that follows it.</summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="sql">The statement.</param>
    /// <param name="parameters">Its values.</param>
    /// <param name="request">The parsed search, for the page size.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task<(IReadOnlyList<ProductSearchItem> Items, SearchCursor? Next)> ReadHitsAsync(
        DbConnection connection,
        string sql,
        SqlParameters parameters,
        SearchRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = Command(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var items = new List<ProductSearchItem>(request.Size);
        SearchCursor? cursor = null;

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new ProductSearchItem(
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                ReadNullable(reader, 7, static (row, index) => row.GetGuid(index)),
                ReadNullable(reader, 8, static (row, index) => row.GetString(index)),
                reader.GetGuid(9),
                reader.GetString(10),
                reader.GetGuid(11),
                reader.GetString(12),
                reader.GetDecimal(13),
                reader.GetDecimal(14),
                reader.GetString(15).Trim(),
                reader.GetInt32(16),
                ReadNullable(reader, 17, static (row, index) => row.GetDecimal(index)),
                reader.GetInt32(18),
                reader.GetBoolean(19),
                reader.GetBoolean(20),
                reader.GetInt32(21),
                ReadNullable(reader, 22, static (row, index) => row.GetGuid(index))));

            // Taken from the last row read rather than computed, because the value the next page has
            // to resume after is the value this page actually sorted by — recomputing it in C# would
            // be a second implementation of the score, and the two would disagree the first time a
            // weight changed.
            cursor = new SearchCursor(reader.GetDecimal(23), reader.GetGuid(0));
        }

        // A short page is the last page. Returning a cursor for it would cost the storefront one
        // empty round trip on every search that fits on a single page, which is most of them.
        return (items, items.Count < request.Size ? null : cursor);
    }

    /// <summary>Counts everything the filters match.</summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="sql">The statement.</param>
    /// <param name="parameters">Its values.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task<long> ReadTotalAsync(
        DbConnection connection,
        string sql,
        SqlParameters parameters,
        CancellationToken cancellationToken)
    {
        await using var command = Command(connection, sql, parameters);

        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return value is long total ? total : 0L;
    }

    /// <summary>Reads every facet's counts and folds them into groups.</summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="request">The parsed search.</param>
    /// <param name="predicates">Every predicate.</param>
    /// <param name="parameters">Collects the values.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task<IReadOnlyList<FacetGroup>> ReadFacetsAsync(
        DbConnection connection,
        SearchRequest request,
        IReadOnlyList<SearchPredicate> predicates,
        SqlParameters parameters,
        CancellationToken cancellationToken)
    {
        var sql = SearchSqlBuilder.Facets(request, predicates, parameters);

        await using var command = Command(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var groups = new Dictionary<string, (string Label, List<FacetValue> Values)>(StringComparer.Ordinal);
        var order = new List<string>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var key = reader.GetString(0);
            var value = reader.IsDBNull(2) ? null : reader.GetString(2);

            if (value is null)
            {
                continue;
            }

            var count = reader.GetInt64(4);

            // A facet value nothing matches is not offered. It is a filter that would empty the page,
            // and a sidebar full of zeroes is the most common way a faceted search tells a shopper
            // to give up.
            if (count == 0)
            {
                continue;
            }

            if (!groups.TryGetValue(key, out var group))
            {
                group = (reader.IsDBNull(1) ? key : reader.GetString(1), []);
                groups[key] = group;
                order.Add(key);
            }

            group.Values.Add(new FacetValue(
                value,
                reader.IsDBNull(3) ? value : reader.GetString(3),
                count,
                ReadNullable(reader, 5, static (row, index) => row.GetDecimal(index)),
                ReadNullable(reader, 6, static (row, index) => row.GetDecimal(index))));
        }

        var limit = Math.Max(request.Settings.MaxFacetValues, 1);

        // The attribute branch is limited in SQL across every attribute at once, because the
        // statement cannot know how many distinct keys it will find. Trimming per group here is what
        // turns that global cap into the per-facet cap the operator configured.
        return
        [
            .. order.Select(key => new FacetGroup(
                key,
                groups[key].Label,
                [.. groups[key].Values.Take(limit)])),
        ];
    }

    /// <summary>Builds a command on the context's connection, joined to its transaction if it has one.</summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="sql">The statement.</param>
    /// <param name="parameters">Its values.</param>
    private static DbCommand Command(DbConnection connection, string sql, SqlParameters parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var parameter in parameters.All)
        {
            command.Parameters.Add(parameter);
        }

        return command;
    }

    /// <summary>Reads a column that may be null.</summary>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="reader">The row.</param>
    /// <param name="ordinal">The column.</param>
    /// <param name="read">How to read it when it is not null.</param>
    private static TValue? ReadNullable<TValue>(DbDataReader reader, int ordinal, Func<DbDataReader, int, TValue> read)
        where TValue : struct
        => reader.IsDBNull(ordinal) ? null : read(reader, ordinal);

    /// <summary>Reads a reference-typed column that may be null.</summary>
    /// <param name="reader">The row.</param>
    /// <param name="ordinal">The column.</param>
    /// <param name="read">How to read it when it is not null.</param>
    private static string? ReadNullable(DbDataReader reader, int ordinal, Func<DbDataReader, int, string> read)
        => reader.IsDBNull(ordinal) ? null : read(reader, ordinal);

    [LoggerMessage(
        EventId = 7900,
        Level = LogLevel.Debug,
        Message = "Search matched {MatchCount} of {Total} rows for a {Pass} pass")]
    private static partial void Matched(ILogger logger, int matchCount, long total, string pass);
}
