using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Content.Infrastructure.Jobs;

/// <summary>
/// Publishes the pages whose time has come.
/// </summary>
/// <remarks>
/// <para>
/// The clock half of the editorial workflow, and the only actor that may take the
/// <see cref="PageStatus.Scheduled"/> to <see cref="PageStatus.Published"/> edge. That restriction is
/// what makes the scheduled state mean something: a publisher who wants a page live now publishes it,
/// which is a different edge and a different line in the audit trail.
/// </para>
/// <para>
/// A minute is the resolution, because it is the resolution a merchandiser thinks in — a sale that
/// starts at midnight starting within sixty seconds of midnight is what everybody involved means by
/// "at midnight". The query behind it is an index seek on a filtered index that is empty almost all
/// of the time.
/// </para>
/// <para>
/// Off in the API and on in the worker, exactly as every sweeper before it. Two processes publishing
/// the same page would corrupt nothing — the second finds it already published and the transition is
/// refused — but it would make the audit trail ambiguous about which run did it.
/// </para>
/// </remarks>
/// <param name="services">The root provider; a scope is taken per pass.</param>
/// <param name="options">The interval and the batch size.</param>
/// <param name="logger">Reports what was published.</param>
internal sealed partial class ContentSchedulerWorker(
    IServiceProvider services,
    IOptionsMonitor<ContentOptions> options,
    ILogger<ContentSchedulerWorker> logger)
    : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.SchedulerEnabled)
        {
            return;
        }

        WorkerStarted(logger, options.CurrentValue.SchedulerIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = options.CurrentValue;

            try
            {
                await PublishDueAsync(settings.SchedulerBatchSize, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Swallowed and logged rather than allowed to stop the host. A pass that fails because
                // the database is briefly unreachable must try again in a minute, not take the worker
                // process down with it.
                PassFailed(logger, exception);
            }

            await Task.Delay(TimeSpan.FromSeconds(settings.SchedulerIntervalSeconds), stoppingToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Publishes every page whose scheduled time has passed.</summary>
    private async Task PublishDueAsync(int batchSize, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var now = clock.UtcNow;

        var due = await context.Pages
            .Include(page => page.Blocks)
            .Where(page => page.Status == PageStatus.Scheduled && page.ScheduledAt <= now)
            .OrderBy(page => page.ScheduledAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (due.Count == 0)
        {
            return;
        }

        var published = 0;

        foreach (var page in due)
        {
            // The home page is the one case a scheduler can get wrong in a way nobody notices for
            // hours: two published home pages is a storefront rendering whichever row came back
            // first. The incumbent is taken down first, which is what an operator scheduling a new
            // home page for midnight means by it.
            if (page.Type == PageType.Home)
            {
                var incumbent = await context.Pages
                    .FirstOrDefaultAsync(
                        row => row.Type == PageType.Home
                               && row.Status == PageStatus.Published
                               && row.Id != page.Id,
                        cancellationToken)
                    .ConfigureAwait(false);

                incumbent?.Transition(PageStatus.Unpublished, PageActor.System, now);
            }

            if (!page.Transition(PageStatus.Published, PageActor.System, now))
            {
                continue;
            }

            Application.Pages.TransitionPageCommandHandler.Snapshot(
                page,
                "Published by the scheduler.",
                restoredFrom: null,
                now,
                actorId: null,
                context);

            published++;
            PagePublished(logger, page.Slug, page.Id);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        PassCompleted(logger, published);
    }

    [LoggerMessage(
        EventId = 8030,
        Level = LogLevel.Information,
        Message = "Content scheduler started; running every {IntervalSeconds} second(s)")]
    private static partial void WorkerStarted(ILogger logger, int intervalSeconds);

    [LoggerMessage(
        EventId = 8031,
        Level = LogLevel.Information,
        Message = "Scheduled page {Slug} ({PageId}) published")]
    private static partial void PagePublished(ILogger logger, string slug, Guid pageId);

    [LoggerMessage(
        EventId = 8032,
        Level = LogLevel.Information,
        Message = "Content scheduler pass published {Published} page(s)")]
    private static partial void PassCompleted(ILogger logger, int published);

    [LoggerMessage(
        EventId = 8033,
        Level = LogLevel.Error,
        Message = "Content scheduler pass failed")]
    private static partial void PassFailed(ILogger logger, Exception exception);
}
