using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Search.Domain;
using KlaraHome.Modules.Search.Infrastructure;
using KlaraHome.Modules.Search.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Search.Application.Insights;

/// <summary>
/// What shoppers searched for over a window (docs/03-database-design.md §4.13).
/// </summary>
/// <remarks>
/// One query serves both reports a merchandising team asks for. Left alone it is "what are people
/// looking for"; with <paramref name="ZeroResultsOnly"/> set it is "what are people looking for that
/// we do not sell", which is the one that changes what the business buys.
/// </remarks>
/// <param name="From">The first instant covered, or null for the last thirty days.</param>
/// <param name="To">The first instant not covered, or null for now.</param>
/// <param name="ZeroResultsOnly">Only queries that returned nothing.</param>
/// <param name="Source">Full searches or suggestion keystrokes, or null for searches.</param>
/// <param name="Size">How many rows.</param>
internal sealed record SearchQueryReportQuery(
    DateTimeOffset? From,
    DateTimeOffset? To,
    bool? ZeroResultsOnly,
    string? Source,
    int? Size) : IQuery<IReadOnlyList<SearchQueryReportRow>>;

/// <summary>
/// Aggregates the query log.
/// </summary>
/// <remarks>
/// <para>
/// Grouped on the normalised query rather than on what was typed, so that "Cushion", "cushions" and
/// "  cushion " are one row rather than three. The verbatim text is carried alongside, because a
/// report about what people type is only useful if it says what they typed.
/// </para>
/// <para>
/// Defaults to full searches. Suggestion keystrokes are recorded too and are worth reading
/// separately — they are how you find the word people abandon half-way through — but folding them
/// into the same report would make the most-searched list a list of three-letter prefixes.
/// </para>
/// <para>
/// The window defaults to thirty days rather than to everything. The table is partitioned by month
/// and retained for a year, and an unbounded aggregate over it is a scan of every partition to
/// answer a question nobody asked in those terms.
/// </para>
/// </remarks>
/// <param name="context">The Search data context.</param>
/// <param name="options">Supplies the row ceiling.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class SearchQueryReportQueryHandler(
    SearchDbContext context,
    IOptions<SearchOptions> options,
    IClock clock)
    : IQueryHandler<SearchQueryReportQuery, IReadOnlyList<SearchQueryReportRow>>
{
    public async Task<Result<IReadOnlyList<SearchQueryReportRow>>> HandleAsync(
        SearchQueryReportQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var now = clock.UtcNow;
        var from = query.From ?? now.AddDays(-30);
        var to = query.To ?? now;
        var source = query.Source is { Length: > 0 } named ? named : SearchQuerySources.Search;
        var size = Math.Clamp(query.Size ?? options.Value.MaxReportRows, 1, options.Value.MaxReportRows);

        var rows = context.Queries
            .AsNoTracking()
            .Where(entry => entry.CreatedAt >= from && entry.CreatedAt < to && entry.Source == source);

        if (query.ZeroResultsOnly == true)
        {
            rows = rows.Where(entry => entry.ResultCount == 0);
        }

        var grouped = await rows
            .GroupBy(entry => entry.NormalisedQuery)
            .Select(group => new
            {
                Normalised = group.Key,
                Verbatim = group.Max(entry => entry.QueryText),
                Count = group.LongCount(),
                ZeroResults = group.LongCount(entry => entry.ResultCount == 0),
                AverageResults = group.Average(entry => (double)entry.ResultCount),
                Clicks = group.LongCount(entry => entry.ClickedPosition != null),
                AverageClickPosition = group.Average(entry => (double?)entry.ClickedPosition),
                LastSearchedAt = group.Max(entry => entry.CreatedAt),
            })
            .OrderByDescending(row => row.Count)
            .ThenBy(row => row.Normalised)
            .Take(size)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var report = grouped.ConvertAll(row => new SearchQueryReportRow(
            row.Verbatim ?? row.Normalised,
            row.Normalised,
            row.Count,
            row.ZeroResults,
            Math.Round(row.AverageResults, 2),
            row.Count == 0 ? 0d : Math.Round((double)row.Clicks / row.Count, 4),
            row.AverageClickPosition is { } position ? Math.Round(position, 2) : null,
            row.LastSearchedAt));

        return Result.Success<IReadOnlyList<SearchQueryReportRow>>(report);
    }
}
