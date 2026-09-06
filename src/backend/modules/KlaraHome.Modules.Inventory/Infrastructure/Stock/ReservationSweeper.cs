using KlaraHome.Modules.Inventory.Domain;
using KlaraHome.Modules.Inventory.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Inventory.Infrastructure.Stock;

/// <summary>
/// Puts expired holds back on sale.
/// </summary>
/// <remarks>
/// <para>
/// Without this, an abandoned checkout keeps the last unit unsellable for ever, and the only way
/// anybody would find out is a customer asking why a product that is clearly in the warehouse says
/// it is out of stock. The domain model requires it in as many words: "reservations expire; expiry
/// release is itself a ledger entry" (docs/02-domain-model.md §4.2).
/// </para>
/// <para>
/// The same shape as the outbox and notification dispatchers, and for the same reasons. Rows are
/// claimed with <c>FOR UPDATE SKIP LOCKED</c>, so running more than one worker is safe by
/// construction rather than by convention, and the loop is disabled by configuration in the API,
/// where every replica polling would multiply the contention for no gain.
/// </para>
/// <para>
/// The release is a ledger entry with a note, not a silent decrement. An operator asking where four
/// units went at 2 a.m. gets an answer.
/// </para>
/// </remarks>
internal sealed partial class ReservationSweeper : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<InventoryOptions> _options;
    private readonly IClock _clock;
    private readonly ILogger<ReservationSweeper> _logger;

    /// <param name="services">Resolves a scoped context and ledger per sweep.</param>
    /// <param name="options">Poll interval and batch size, re-read each cycle.</param>
    /// <param name="clock">The sanctioned clock.</param>
    /// <param name="logger">Reports how much stock came back.</param>
    public ReservationSweeper(
        IServiceProvider services,
        IOptionsMonitor<InventoryOptions> options,
        IClock clock,
        ILogger<ReservationSweeper> logger)
    {
        _services = services;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CurrentValue.ReservationSweeperEnabled)
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
                // while stock nobody can buy sits on the floor.
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

    /// <summary>Claims a batch of lapsed holds and releases them. Returns how many were released.</summary>
    private async Task<int> SweepAsync(InventoryOptions options, CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var ledger = scope.ServiceProvider.GetRequiredService<StockLedgerService>();

        var strategy = context.Database.CreateExecutionStrategy();
        var released = 0;

        await strategy.ExecuteAsync(async () =>
        {
            released = 0;

            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var now = _clock.UtcNow;

            // FOR UPDATE SKIP LOCKED: a second worker polling at the same instant takes the next
            // batch rather than blocking on this one, which is what makes horizontal scaling safe
            // without a distributed lock.
            //
            // The query filters are bypassed on purpose. This loop has no tenant and no caller —
            // it is the platform tidying up after itself — and a filtered query would sweep only
            // whichever tenant the ambient context happened to name.
            var lapsed = await context.Reservations
                .FromSql(
                    $"""
                     SELECT * FROM inventory.stock_reservations
                     WHERE status = 'Held' AND expires_at <= {now}
                     ORDER BY expires_at
                     LIMIT {options.SweepBatchSize}
                     FOR UPDATE SKIP LOCKED
                     """)
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var reservation in lapsed)
            {
                var item = await context.StockItems
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(
                        candidate => candidate.Id == reservation.StockItemId,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (item is not null)
                {
                    await ledger
                        .ReleaseAsync(
                            item,
                            reservation.Quantity,
                            reservation.ReferenceType,
                            reservation.ReferenceId,
                            "The hold expired before it was settled.",
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                if (reservation.Settle(ReservationStatus.Expired, now))
                {
                    released++;
                }
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        if (released > 0)
        {
            HoldsExpired(_logger, released);
        }

        return released;
    }

    [LoggerMessage(
        EventId = 7110,
        Level = LogLevel.Information,
        Message = "Reservation sweeper started; polling every {IntervalSeconds}s")]
    private static partial void SweeperStarted(ILogger logger, int intervalSeconds);

    [LoggerMessage(EventId = 7111, Level = LogLevel.Error, Message = "Reservation sweep failed")]
    private static partial void SweepFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 7112,
        Level = LogLevel.Information,
        Message = "{Count} expired stock holds released back into supply")]
    private static partial void HoldsExpired(ILogger logger, int count);
}
