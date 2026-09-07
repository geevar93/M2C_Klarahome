using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Modules.Orders.Domain;
using KlaraHome.Modules.Orders.Infrastructure.Lifecycle;
using KlaraHome.Modules.Orders.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Orders.Infrastructure.Jobs;

/// <summary>
/// Closes the two transitions that happen because time passed rather than because somebody acted.
/// </summary>
/// <remarks>
/// <para>
/// A delivered sub-order becomes <c>Completed</c> when its return window shuts, which is the moment
/// settlement becomes payable (docs/02-domain-model.md §5.1). And an order that has waited for a
/// payment past the timeout is cancelled, which puts its held units back on sale and stops a dead
/// order sitting in somebody's account.
/// </para>
/// <para>
/// Both are the platform acting as itself, so the transitions are taken as
/// <see cref="OrderActor.System"/> — the one actor no HTTP caller can claim to be.
/// </para>
/// <para>
/// The same shape as the outbox, notification, reservation and abandoned-cart sweepers, and for the
/// same reasons. Rows are claimed with <c>FOR UPDATE SKIP LOCKED</c>, so running more than one worker
/// is safe by construction rather than by convention, and the loop is disabled by configuration in
/// the API, where every replica polling would multiply the contention for no gain.
/// </para>
/// </remarks>
internal sealed partial class OrderLifecycleSweeper : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<OrdersOptions> _options;
    private readonly IClock _clock;
    private readonly ILogger<OrderLifecycleSweeper> _logger;

    /// <param name="services">Resolves a scoped context per sweep.</param>
    /// <param name="options">Poll interval, batch size and the unpaid timeout, re-read each cycle.</param>
    /// <param name="clock">The sanctioned clock.</param>
    /// <param name="logger">Reports how much moved.</param>
    public OrderLifecycleSweeper(
        IServiceProvider services,
        IOptionsMonitor<OrdersOptions> options,
        IClock clock,
        ILogger<OrderLifecycleSweeper> logger)
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
                // while a day's worth of completions waits for a settlement that is already due.
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
                // this service would strand every later completion too.
                SweepFailed(_logger, exception);
            }

            await Task.Delay(TimeSpan.FromSeconds(options.SweepIntervalSeconds), stoppingToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Claims a batch and moves it. Returns how many sub-orders were touched.</summary>
    private async Task<int> SweepAsync(OrdersOptions options, CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var workflow = scope.ServiceProvider.GetRequiredService<SubOrderWorkflow>();

        var strategy = context.Database.CreateExecutionStrategy();
        var touched = 0;

        await strategy.ExecuteAsync(async () =>
        {
            touched = 0;

            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var now = _clock.UtcNow;
            var unpaidBefore = now.AddMinutes(-options.UnpaidOrderTimeoutMinutes);

            // The query filters are bypassed on purpose. This loop has no tenant and no caller — it
            // is the platform tidying up after itself — and a filtered query would sweep only
            // whichever tenant the ambient context happened to name. It is also why the transitions
            // below are taken as System: there is no principal to attribute them to.
            var due = await context
                .Claim<SubOrder>(
                    "orders.sub_orders",
                    $"""
                     (status = 'Delivered' AND return_window_ends_at IS NOT NULL
                            AND return_window_ends_at <= {now})
                        OR (status = 'PendingPayment' AND created_at <= {unpaidBefore})
                     """,
                    "created_at",
                    options.SweepBatchSize)
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (due.Count == 0)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            // Loaded whole rather than per sub-order: the workflow re-derives the parent's status
            // from every sibling, and a partially loaded order would derive it from half of them.
            var orders = await context.Orders
                .IgnoreQueryFilters()
                .Include(order => order.SubOrders)
                .ThenInclude(subOrder => subOrder.Lines)
                .Where(order => due.Select(subOrder => subOrder.OrderId).Contains(order.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var order in orders)
            {
                foreach (var subOrder in order.SubOrders.Where(subOrder => due.Any(row => row.Id == subOrder.Id)))
                {
                    var moved = subOrder.Status switch
                    {
                        SubOrderStatus.Delivered => await workflow
                            .TransitionAsync(
                                order,
                                subOrder,
                                SubOrderStatus.Completed,
                                OrderActor.System,
                                actorId: null,
                                "The return window has closed.",
                                cancellationToken)
                            .ConfigureAwait(false),
                        _ => await workflow
                            .CancelAsync(
                                order,
                                subOrder,
                                lines: null,
                                CancellationInitiator.System,
                                actorId: null,
                                "Payment was not completed in time.",
                                cancellationToken)
                            .ConfigureAwait(false),
                    };

                    if (moved.IsSuccess)
                    {
                        touched++;
                    }
                }
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        if (touched > 0)
        {
            SubOrdersSwept(_logger, touched);
        }

        return touched;
    }

    [LoggerMessage(
        EventId = 7320,
        Level = LogLevel.Information,
        Message = "Order lifecycle sweeper started; polling every {IntervalSeconds}s")]
    private static partial void SweeperStarted(ILogger logger, int intervalSeconds);

    [LoggerMessage(EventId = 7321, Level = LogLevel.Error, Message = "Order lifecycle sweep failed")]
    private static partial void SweepFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 7322,
        Level = LogLevel.Information,
        Message = "{Count} sub-orders completed or timed out")]
    private static partial void SubOrdersSwept(ILogger logger, int count);
}
