using KlaraHome.Modules.Carts.Domain;
using KlaraHome.Modules.Carts.Infrastructure.Events;
using KlaraHome.Modules.Carts.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Carts.Infrastructure.Jobs;

/// <summary>
/// Writes off baskets and checkouts nobody came back to.
/// </summary>
/// <remarks>
/// <para>
/// Three jobs in one loop, because they read the same rows and run on the same cadence: a live
/// basket that has sat untouched becomes <c>Abandoned</c> and announces itself, which is the whole
/// of <b>abandoned-cart capture</b>; an abandoned basket past the retention window becomes
/// <c>Expired</c>; and a checkout session past its own expiry is closed so the shopper's next visit
/// starts cleanly rather than resuming an address they chose last month.
/// </para>
/// <para>
/// The same shape as the outbox, notification and reservation sweepers, and for the same reasons.
/// Rows are claimed with <c>FOR UPDATE SKIP LOCKED</c>, so running more than one worker is safe by
/// construction rather than by convention, and the loop is disabled by configuration in the API,
/// where every replica polling would multiply the contention for no gain.
/// </para>
/// <para>
/// It releases no stock, and it does not need to: a basket never holds any. Stock is held between
/// placement and confirmation, against the cart id, and Inventory's own sweeper is what puts an
/// abandoned hold back on sale (docs/03-database-design.md §4.5).
/// </para>
/// </remarks>
internal sealed partial class AbandonedCartSweeper : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<CartsOptions> _options;
    private readonly IClock _clock;
    private readonly ILogger<AbandonedCartSweeper> _logger;

    /// <param name="services">Resolves a scoped context per sweep.</param>
    /// <param name="options">Poll interval, batch size and the two windows, re-read each cycle.</param>
    /// <param name="clock">The sanctioned clock.</param>
    /// <param name="logger">Reports how much was written off.</param>
    public AbandonedCartSweeper(
        IServiceProvider services,
        IOptionsMonitor<CartsOptions> options,
        IClock clock,
        ILogger<AbandonedCartSweeper> logger)
    {
        _services = services;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CurrentValue.SweeperEnabled)
        {
            return;
        }

        SweeperStarted(_logger, _options.CurrentValue.SweepIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;

            try
            {
                // A full batch means there may be more waiting; go round again rather than idling
                // while a day's worth of abandoned baskets waits for a reminder that is only
                // worth sending soon.
                if (await SweepAsync(options, stoppingToken).ConfigureAwait(false) >= options.SweepBatchSize)
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // A sweep failure is infrastructure-level. Log it and keep the loop alive; killing
                // this service would strand every later expiry too.
                SweepFailed(_logger, exception);
            }

            await Task.Delay(TimeSpan.FromSeconds(options.SweepIntervalSeconds), stoppingToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Claims a batch and writes it off. Returns how many rows were touched.</summary>
    private async Task<int> SweepAsync(CartsOptions options, CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<CartsDbContext>();
        var events = scope.ServiceProvider.GetRequiredService<CartsEventPublisher>();

        var strategy = context.Database.CreateExecutionStrategy();
        var touched = 0;

        await strategy.ExecuteAsync(async () =>
        {
            touched = 0;

            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var now = _clock.UtcNow;
            var abandonBefore = now.AddHours(-options.AbandonAfterHours);
            var retireBefore = now.AddDays(-options.AbandonedRetentionDays);

            // The query filters are bypassed on purpose. This loop has no tenant and no caller — it
            // is the platform tidying up after itself — and a filtered query would sweep only
            // whichever tenant the ambient context happened to name.
            var stale = await context.Carts
                .FromSql(
                    $"""
                     SELECT * FROM carts.carts
                     WHERE (status = 'Active' AND last_activity_at <= {abandonBefore})
                        OR (status = 'Abandoned' AND abandoned_at <= {retireBefore})
                        OR (status = 'Active' AND expires_at <= {now})
                     ORDER BY last_activity_at
                     LIMIT {options.SweepBatchSize}
                     FOR UPDATE SKIP LOCKED
                     """)
                .IgnoreQueryFilters()
                .Include(cart => cart.Lines)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var cart in stale)
            {
                touched++;

                if (cart.Status == CartStatus.Abandoned || cart.ExpiresAt <= now)
                {
                    // Past retention, or past its own expiry without ever being worth chasing.
                    cart.MarkExpired(now);
                    continue;
                }

                // An empty basket is not an abandoned sale, and telling somebody they left nothing
                // behind is the kind of message that gets a sender marked as spam.
                if (cart.LineCount == 0)
                {
                    cart.MarkExpired(now);
                    continue;
                }

                if (cart.MarkAbandoned(now))
                {
                    events.Abandoned(cart, EstimateValue(cart));
                }
            }

            var sessions = await context.CheckoutSessions
                .FromSql(
                    $"""
                     SELECT * FROM carts.checkout_sessions
                     WHERE status IN ('Draft', 'AddressSet', 'ShippingSet', 'PaymentSet')
                       AND expires_at <= {now}
                     ORDER BY expires_at
                     LIMIT {options.SweepBatchSize}
                     FOR UPDATE SKIP LOCKED
                     """)
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var session in sessions)
            {
                if (session.MarkExpired())
                {
                    touched++;
                }
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        if (touched > 0)
        {
            CartsSwept(_logger, touched);
        }

        return touched;
    }

    /// <summary>
    /// What the basket was worth, from the prices the cart last saw.
    /// </summary>
    /// <remarks>
    /// Deliberately not a quote. This figure exists to rank a marketing worklist, it is computed for
    /// hundreds of baskets in one sweep, and pricing each of them properly would mean running the
    /// promotion engine over every abandoned basket on the platform every five minutes.
    /// </remarks>
    private static decimal EstimateValue(Cart cart)
        => cart.Lines
            .Where(line => !line.SavedForLater)
            .Sum(line => line.UnitPriceAtAdd * line.Quantity);

    [LoggerMessage(
        EventId = 7210,
        Level = LogLevel.Information,
        Message = "Abandoned-cart sweeper started; polling every {IntervalSeconds}s")]
    private static partial void SweeperStarted(ILogger logger, int intervalSeconds);

    [LoggerMessage(EventId = 7211, Level = LogLevel.Error, Message = "Abandoned-cart sweep failed")]
    private static partial void SweepFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 7212,
        Level = LogLevel.Information,
        Message = "{Count} baskets and checkout sessions written off")]
    private static partial void CartsSwept(ILogger logger, int count);
}
