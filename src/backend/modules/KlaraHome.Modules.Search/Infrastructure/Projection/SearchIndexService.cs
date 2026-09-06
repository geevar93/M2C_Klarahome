using KlaraHome.Contracts.Catalog;
using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Search.Application;
using KlaraHome.Modules.Search.Infrastructure.Engine;
using KlaraHome.Modules.Search.Infrastructure.Features;
using KlaraHome.Modules.Search.Infrastructure.Persistence;
using KlaraHome.Modules.Search.Infrastructure.Query;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Search.Infrastructure.Projection;

/// <summary>
/// Rebuilds the index, and says what state it is in.
/// </summary>
/// <remarks>
/// <para>
/// The event pipeline is how the index is normally kept current; this is the safety net under it and
/// the tool an operator uses when something has plainly gone wrong. Both operations it offers are
/// bounded and resumable, because the alternative — one statement that rebuilds a fifty-thousand
/// product catalogue — is a job that cannot be stopped, cannot be watched and holds a read on the
/// whole catalogue while it runs.
/// </para>
/// <para>
/// A rebuild is safe to run against a live index. Every write is an upsert keyed on the variant, so a
/// rebuild racing the event pipeline produces the row one of them wrote rather than two rows or a
/// half-written one, and whichever wrote last read the same sources.
/// </para>
/// </remarks>
/// <param name="context">The Search data context.</param>
/// <param name="catalogue">The catalogue walk.</param>
/// <param name="writer">The single place an index row is written.</param>
/// <param name="engines">Says which engine is currently answering.</param>
/// <param name="flags">Reads the dedicated-engine switch, for the status report.</param>
/// <param name="vocabulary">Supplies the synonym and stop-word counts.</param>
/// <param name="options">Supplies the batch sizes and the staleness window.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what a rebuild did.</param>
internal sealed partial class SearchIndexService(
    SearchDbContext context,
    IProductProjectionSource catalogue,
    SearchProjectionWriter writer,
    SearchEngineRegistry engines,
    IFeatureFlags flags,
    SearchVocabularyReader vocabulary,
    IOptions<SearchOptions> options,
    IClock clock,
    ILogger<SearchIndexService> logger)
{
    /// <summary>
    /// Walks the catalogue from a point and rebuilds every variant it finds.
    /// </summary>
    /// <remarks>
    /// Resumable by design: it answers with the variant it reached, and passing that back continues
    /// the walk. That is what lets a full rebuild of a large catalogue be run as a series of bounded
    /// requests an operator can watch, rather than one that either finishes or does not.
    /// </remarks>
    /// <param name="afterVariantId">Resume after this variant, or null to start at the beginning.</param>
    /// <param name="maxVariants">The most variants this run will walk.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ReindexResponse> RebuildAsync(
        Guid? afterVariantId,
        int? maxVariants,
        CancellationToken cancellationToken)
    {
        var ceiling = Math.Clamp(
            maxVariants ?? options.Value.MaxRebuildVariants,
            1,
            options.Value.MaxRebuildVariants);

        var batchSize = options.Value.ReindexBatchSize;
        var cursor = afterVariantId;
        var walked = 0;
        var outcome = default(ProjectionOutcome);

        while (walked < ceiling && !cancellationToken.IsCancellationRequested)
        {
            var take = Math.Min(batchSize, ceiling - walked);

            var page = await catalogue.EnumerateAsync(cursor, take, cancellationToken).ConfigureAwait(false);

            if (page.NextVariantCursor is null)
            {
                RebuildFinished(logger, walked, outcome.Written, outcome.Retired);

                return new ReindexResponse(walked, outcome.Written, outcome.Retired, true, null);
            }

            var variantIds = page.VariantIds;

            // Every variant the page covered, not only those that produced offers. A variant whose
            // last offer was withdrawn returns nothing from the catalogue, and it is precisely the
            // row that has to be retired — skipping it would leave a dead product in results for ever.
            if (variantIds.Count > 0)
            {
                outcome += await writer
                    .RefreshAsync(variantIds, page.Items, cancellationToken)
                    .ConfigureAwait(false);
            }

            walked += variantIds.Count;
            cursor = page.NextVariantCursor;
        }

        RebuildPaused(logger, walked, outcome.Written, outcome.Retired);

        return new ReindexResponse(walked, outcome.Written, outcome.Retired, false, cursor);
    }

    /// <summary>
    /// Rebuilds the rows that have fallen furthest behind.
    /// </summary>
    /// <remarks>
    /// The sweep the background worker runs. It is not how the index is kept current — events are —
    /// and it exists for the row whose event was dropped, whose handler threw, or that was written
    /// while a module was being deployed. On a healthy deployment it finds nothing, and that is the
    /// point.
    /// </remarks>
    /// <param name="batchSize">The most rows to rebuild.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ProjectionOutcome> RefreshStaleAsync(int batchSize, CancellationToken cancellationToken)
    {
        var cutoff = clock.UtcNow.AddHours(-options.Value.StaleAfterHours);

        var variantIds = await context.Documents
            .AsNoTracking()
            .Where(document => document.IsActive && document.IndexedAt < cutoff)
            .OrderBy(document => document.IndexedAt)
            .Select(document => document.VariantId)
            .Take(Math.Clamp(batchSize, 1, options.Value.ReindexBatchSize))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (variantIds.Count == 0)
        {
            return default;
        }

        var outcome = await writer.RefreshVariantsAsync(variantIds, cancellationToken).ConfigureAwait(false);

        StaleRefreshed(logger, variantIds.Count, outcome.Written, outcome.Retired);

        return outcome;
    }

    /// <summary>What the index looks like right now.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SearchIndexStatus> StatusAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.UtcNow.AddHours(-options.Value.StaleAfterHours);

        // One pass over the table with four conditional aggregates, rather than four counts. The
        // table has a row per sellable thing, and this is an operator's diagnostic screen — it should
        // cost one scan, not four.
        var counts = await context.Documents
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Total = group.LongCount(),
                Active = group.LongCount(document => document.IsActive),
                Available = group.LongCount(document => document.IsActive && document.IsAvailable),
                Stale = group.LongCount(document => document.IsActive && document.IndexedAt < cutoff),
                Oldest = group.Min(document => (DateTimeOffset?)document.IndexedAt),
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var engine = await engines.ResolveAsync(cancellationToken).ConfigureAwait(false);

        var externalEnabled = await flags
            .IsEnabledAsync(SearchFeatures.ExternalEngine, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var words = await vocabulary.GetAsync(cancellationToken).ConfigureAwait(false);

        return new SearchIndexStatus(
            engine.Key,
            externalEnabled,
            counts?.Total ?? 0,
            counts?.Active ?? 0,
            counts?.Available ?? 0,
            counts?.Stale ?? 0,
            counts?.Oldest,
            words.SynonymCount,
            words.StopWordCount);
    }

    [LoggerMessage(
        EventId = 7940,
        Level = LogLevel.Information,
        Message = "Search rebuild reached the end of the catalogue after {Walked} variant(s): "
                  + "{Written} row(s) written, {Retired} retired")]
    private static partial void RebuildFinished(ILogger logger, int walked, int written, int retired);

    [LoggerMessage(
        EventId = 7941,
        Level = LogLevel.Information,
        Message = "Search rebuild paused after {Walked} variant(s): {Written} row(s) written, "
                  + "{Retired} retired. Resume with the returned cursor")]
    private static partial void RebuildPaused(ILogger logger, int walked, int written, int retired);

    [LoggerMessage(
        EventId = 7942,
        Level = LogLevel.Information,
        Message = "Search refreshed {Candidates} stale row(s): {Written} written, {Retired} retired")]
    private static partial void StaleRefreshed(ILogger logger, int candidates, int written, int retired);
}
