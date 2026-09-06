using KlaraHome.Modules.Reviews.Domain;
using KlaraHome.Modules.Reviews.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Reviews.Infrastructure.Jobs;

/// <summary>
/// Closes the stock alerts nobody is waiting for any more.
/// </summary>
/// <remarks>
/// <para>
/// A subscription whose expiry has passed is closed rather than deleted, and the difference matters
/// for the anonymous ones: the row carries an email address somebody gave for one purpose, and
/// expiring it is what stops that address being used for a message three months late. The row itself
/// is kept so support can answer "why did I never hear back", and the personal data on it is what
/// Step 31's retention sweep is for.
/// </para>
/// <para>
/// This is the only background work the module has. Everything else it does is driven by an event or
/// by a request, which is the right shape for a module whose whole content is written by shoppers —
/// there is nothing here that becomes true because time passed, except an interest going stale.
/// </para>
/// <para>
/// Bounded and resumable: each pass closes a batch of the oldest due subscriptions, and the next
/// pass takes the rest. A sweep that expired every due row at once would hold a write transaction
/// over an unbounded set on the morning after a large restock.
/// </para>
/// </remarks>
/// <param name="services">The root provider; a scope is taken per pass.</param>
/// <param name="options">The interval and the batch size.</param>
/// <param name="logger">Reports what was closed.</param>
internal sealed partial class SubscriptionSweepWorker(
    IServiceProvider services,
    IOptionsMonitor<ReviewsOptions> options,
    ILogger<SubscriptionSweepWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.SweepEnabled)
        {
            return;
        }

        WorkerStarted(logger, options.CurrentValue.SweepIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = options.CurrentValue;

            try
            {
                await ExpireDueAsync(settings, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                SweepFailed(logger, exception);
            }

            await Task.Delay(TimeSpan.FromMinutes(settings.SweepIntervalMinutes), stoppingToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Closes the oldest batch of subscriptions whose time is up.</summary>
    private async Task ExpireDueAsync(ReviewsOptions settings, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ReviewsDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var now = clock.UtcNow;

        var due = await context.StockSubscriptions
            .Where(subscription => subscription.Status == SubscriptionStatus.Active)
            .Where(subscription => subscription.ExpiresAt != null && subscription.ExpiresAt < now)
            .OrderBy(subscription => subscription.ExpiresAt)
            .Take(settings.SweepBatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (due.Count == 0)
        {
            return;
        }

        foreach (var subscription in due)
        {
            subscription.Expire();
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        SweepCompleted(logger, due.Count);
    }

    [LoggerMessage(
        EventId = 8120,
        Level = LogLevel.Information,
        Message = "Stock-alert sweep started; running every {IntervalMinutes} minute(s)")]
    private static partial void WorkerStarted(ILogger logger, int intervalMinutes);

    [LoggerMessage(
        EventId = 8121,
        Level = LogLevel.Information,
        Message = "Stock-alert sweep expired {Count} subscription(s)")]
    private static partial void SweepCompleted(ILogger logger, int count);

    [LoggerMessage(
        EventId = 8122,
        Level = LogLevel.Error,
        Message = "Stock-alert sweep failed")]
    private static partial void SweepFailed(ILogger logger, Exception exception);
}
