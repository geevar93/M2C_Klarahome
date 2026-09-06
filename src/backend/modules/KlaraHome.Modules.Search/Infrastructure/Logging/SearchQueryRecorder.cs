using System.Globalization;
using System.Text.Json;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Modules.Search.Domain;
using KlaraHome.Modules.Search.Infrastructure.Engine;
using KlaraHome.Modules.Search.Infrastructure.Features;
using KlaraHome.Modules.Search.Infrastructure.Persistence;
using KlaraHome.Modules.Search.Infrastructure.Query;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Search.Infrastructure.Logging;

/// <summary>
/// Records what shoppers asked for, and what they clicked.
/// </summary>
/// <remarks>
/// <para>
/// The only original record this module keeps. Everything else in the schema is a copy of the
/// catalogue and can be rebuilt from it; what a shopper typed exists nowhere else, and the queries
/// that returned nothing are the most valuable rows in the table — they are the most direct evidence
/// a buying team has of what the store does not stock.
/// </para>
/// <para>
/// Two switches guard it, and both have to be on. <c>SearchSettings.LogQueries</c> is the store's
/// standing policy; the <c>search.query-logging</c> flag is the switch to throw during an incident or
/// a subject request. A shopper's search terms are personal data under the DPDP Act, and neither
/// switch turns search off — only the recording of it.
/// </para>
/// <para>
/// Writing the log never fails a search. A shopper whose results are fine does not care that the
/// analytics row did not save, and an exception here would turn a working search into a 500 for the
/// sake of a number on a report.
/// </para>
/// </remarks>
/// <param name="context">The Search data context.</param>
/// <param name="flags">Reads the logging switch.</param>
/// <param name="caller">Supplies the shopper and their session, when they are signed in.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports a log write that failed.</param>
internal sealed partial class SearchQueryRecorder(
    SearchDbContext context,
    IFeatureFlags flags,
    ICallerContext caller,
    IClock clock,
    ILogger<SearchQueryRecorder> logger)
{
    /// <summary>
    /// Records a search and returns the handle a click reports against.
    /// </summary>
    /// <param name="query">The query, as it was interpreted.</param>
    /// <param name="source">Whether it was a search or a keystroke.</param>
    /// <param name="request">The request, for the filters that were applied. Null for a suggestion.</param>
    /// <param name="resultCount">How many rows matched.</param>
    /// <param name="durationMs">How long it took.</param>
    /// <param name="settings">The store's standing policy on logging.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<string?> RecordAsync(
        NormalizedQuery query,
        string source,
        SearchRequest? request,
        long resultCount,
        long durationMs,
        SearchSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.LogQueries || string.IsNullOrWhiteSpace(query.Raw))
        {
            return null;
        }

        var enabled = await flags
            .IsEnabledAsync(SearchFeatures.QueryLogging, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!enabled)
        {
            return null;
        }

        try
        {
            var now = clock.UtcNow;

            var entry = SearchQueryLogEntry.Record(
                query.Raw,
                query.Normalised,
                source,
                (int)Math.Min(resultCount, int.MaxValue),
                (int)Math.Min(durationMs, int.MaxValue),
                request is null ? null : Filters(request),
                caller.UserId,
                caller.SessionId?.ToString(),
                now);

            context.Queries.Add(entry);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Token(entry.CreatedAt, entry.Id);
        }
        catch (DbUpdateException exception)
        {
            // Never fails the search. A shopper whose results are fine does not care that the
            // analytics row did not save, and the alternative is a 500 for the sake of a report.
            LogWriteFailed(logger, exception);
            return null;
        }
    }

    /// <summary>
    /// Records that a shopper clicked a result.
    /// </summary>
    /// <remarks>
    /// Answers whether the row was found rather than throwing. The log is retained for a year and
    /// partitioned by month, so a handle from a page somebody left open over a long holiday
    /// eventually names a partition that has been detached — and that is not a shopper's problem.
    /// </remarks>
    /// <param name="token">The handle the search answered with.</param>
    /// <param name="position">Which result, one-based.</param>
    /// <param name="variantId">What they clicked.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> RecordClickAsync(
        string token,
        int position,
        Guid variantId,
        CancellationToken cancellationToken)
    {
        if (!TryReadToken(token, out var createdAt, out var id))
        {
            return false;
        }

        // Both halves of the key. The table is partitioned on the timestamp, so a lookup without it
        // would search every month the log holds to find one row.
        var entry = await context.Queries
            .FirstOrDefaultAsync(
                candidate => candidate.CreatedAt == createdAt && candidate.Id == id,
                cancellationToken)
            .ConfigureAwait(false);

        if (entry is null)
        {
            return false;
        }

        entry.RecordClick(position, variantId, clock.UtcNow);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Encodes the handle a click reports against.
    /// </summary>
    /// <remarks>
    /// It carries the partition key as well as the id, which is the whole reason it exists rather
    /// than the bare id being returned. Encoded with the same opaque encoding as a page cursor: it is
    /// an encoding and not a signature, and it reveals nothing the caller did not just receive.
    /// </remarks>
    /// <param name="createdAt">When the query was recorded.</param>
    /// <param name="id">Its id.</param>
    public static string Token(DateTimeOffset createdAt, Guid id)
        => Cursor.Encode(string.Create(
            CultureInfo.InvariantCulture,
            $"{createdAt.UtcTicks}:{id}"));

    /// <summary>Reads a handle back, or answers false.</summary>
    /// <param name="token">The handle.</param>
    /// <param name="createdAt">When the query was recorded.</param>
    /// <param name="id">Its id.</param>
    public static bool TryReadToken(string? token, out DateTimeOffset createdAt, out Guid id)
    {
        createdAt = default;
        id = default;

        if (!Cursor.TryDecode(token, out var key))
        {
            return false;
        }

        var separator = key.IndexOf(':', StringComparison.Ordinal);

        if (separator <= 0
            || !long.TryParse(key[..separator], CultureInfo.InvariantCulture, out var ticks)
            || !Guid.TryParse(key[(separator + 1)..], out id)
            || ticks < DateTimeOffset.MinValue.UtcTicks
            || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        createdAt = new DateTimeOffset(ticks, TimeSpan.Zero);
        return true;
    }

    /// <summary>
    /// The filters a search was run with, as a small <c>jsonb</c> document.
    /// </summary>
    /// <remarks>
    /// Kept because a zero-result search with three filters applied is a different problem from a
    /// zero-result search with none: the first is a combination nothing satisfies, and the second is
    /// a product the store does not carry. Only the shape is recorded — which dimensions were used
    /// and what was chosen — and nothing about the shopper.
    /// </remarks>
    /// <param name="request">The parsed search.</param>
    private static string? Filters(SearchRequest request)
    {
        if (!request.HasFilters)
        {
            return null;
        }

        var filters = new Dictionary<string, object>(StringComparer.Ordinal);

        if (request.CategoryId is { } categoryId)
        {
            filters["category"] = categoryId;
        }

        if (request.BrandIds.Count > 0)
        {
            filters["brand"] = request.BrandIds;
        }

        if (request.VendorIds.Count > 0)
        {
            filters["vendor"] = request.VendorIds;
        }

        if (request.MinPrice is { } min)
        {
            filters["minPrice"] = min;
        }

        if (request.MaxPrice is { } max)
        {
            filters["maxPrice"] = max;
        }

        if (request.MinRating is { } rating)
        {
            filters["minRating"] = rating;
        }

        if (request.MinDiscount is { } discount)
        {
            filters["minDiscount"] = discount;
        }

        if (request.InStockOnly)
        {
            filters["inStock"] = true;
        }

        foreach (var attribute in request.Attributes)
        {
            filters["attr." + attribute.Code] = attribute.Values;
        }

        return JsonSerializer.Serialize(filters);
    }

    [LoggerMessage(
        EventId = 7960,
        Level = LogLevel.Warning,
        Message = "A search query could not be recorded; the search itself was unaffected")]
    private static partial void LogWriteFailed(ILogger logger, Exception exception);
}
