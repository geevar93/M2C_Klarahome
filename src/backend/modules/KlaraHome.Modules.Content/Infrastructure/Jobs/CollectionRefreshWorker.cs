using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Infrastructure.Collections;
using KlaraHome.Modules.Content.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Content.Infrastructure.Jobs;

/// <summary>
/// Rebuilds the rule-based collections that have fallen behind.
/// </summary>
/// <remarks>
/// <para>
/// The safety net under the event handlers, and one thing more. The handlers keep a collection
/// current for products that <em>change</em>; this covers the two cases no event will ever fire for.
/// A rule that was edited is one — the products it now matches have not changed, the rule has. And a
/// "published in the last thirty days" condition is the other: a product stops satisfying it purely
/// because time passed, and nothing in the catalogue happened at all.
/// </para>
/// <para>
/// It is also what restores <em>order</em>. The event path appends, because re-sorting a collection
/// means re-evaluating it, and a product sitting at the end of a carousel for up to half an hour is a
/// far better outcome than one that does not appear until the sweep runs.
/// </para>
/// <para>
/// Bounded and resumable: each pass refreshes a handful of the least recently refreshed collections,
/// and the next pass takes the rest. A sweep that rebuilt every collection at once would walk the
/// catalogue once per collection while the storefront was trying to read it.
/// </para>
/// </remarks>
/// <param name="services">The root provider; a scope is taken per pass.</param>
/// <param name="options">The interval, the batch size and the staleness threshold.</param>
/// <param name="logger">Reports what was refreshed.</param>
internal sealed partial class CollectionRefreshWorker(
    IServiceProvider services,
    IOptionsMonitor<ContentOptions> options,
    ILogger<CollectionRefreshWorker> logger)
    : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.CollectionRefreshEnabled)
        {
            return;
        }

        WorkerStarted(logger, options.CurrentValue.CollectionRefreshIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = options.CurrentValue;

            try
            {
                await RefreshStaleAsync(settings, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                SweepFailed(logger, exception);
            }

            await Task.Delay(TimeSpan.FromMinutes(settings.CollectionRefreshIntervalMinutes), stoppingToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Refreshes the least recently refreshed rule-based collections.</summary>
    private async Task RefreshStaleAsync(ContentOptions settings, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        var materializer = scope.ServiceProvider.GetRequiredService<CollectionMaterializer>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var threshold = clock.UtcNow.AddHours(-settings.CollectionStaleAfterHours);

        var stale = await context.Collections
            .Where(collection => collection.Kind == CollectionKind.Rule)
            .Where(collection => collection.RefreshedAt == null || collection.RefreshedAt < threshold)
            // Nulls first: a collection whose rule was just written and has never been evaluated is
            // the one somebody is waiting on.
            .OrderBy(collection => collection.RefreshedAt ?? DateTimeOffset.MinValue)
            .Take(settings.CollectionRefreshBatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var collection in stale)
        {
            await materializer.RefreshAsync(collection, cancellationToken).ConfigureAwait(false);
        }

        if (stale.Count > 0)
        {
            SweepCompleted(logger, stale.Count);
        }
    }

    [LoggerMessage(
        EventId = 8040,
        Level = LogLevel.Information,
        Message = "Collection refresh sweep started; running every {IntervalMinutes} minute(s)")]
    private static partial void WorkerStarted(ILogger logger, int intervalMinutes);

    [LoggerMessage(
        EventId = 8041,
        Level = LogLevel.Information,
        Message = "Collection refresh sweep rebuilt {Count} collection(s)")]
    private static partial void SweepCompleted(ILogger logger, int count);

    [LoggerMessage(
        EventId = 8042,
        Level = LogLevel.Error,
        Message = "Collection refresh sweep failed")]
    private static partial void SweepFailed(ILogger logger, Exception exception);
}
