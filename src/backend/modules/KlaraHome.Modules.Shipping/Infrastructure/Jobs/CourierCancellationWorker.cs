using KlaraHome.Modules.Shipping.Infrastructure.Fulfilment;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Shipping.Infrastructure.Jobs;

/// <summary>
/// Keeps asking a courier to cancel the parcel of a cancelled order until it agrees.
/// </summary>
/// <remarks>
/// <para>
/// The second half of <see cref="CourierCancellation"/>. When a shopper cancels an order whose parcel
/// is already booked, the courier is told at once; if it cannot be reached, the parcel is left booked
/// and marked, and this sweep offers it again every few minutes. It stops when the courier accepts,
/// when a person cancels the parcel by hand, or when the courier collects it first — which turns the
/// cancellation into a return and is logged as an error for somebody to arrange.
/// </para>
/// <para>
/// It also sends the return instruction for a cancelled order's parcel that the courier had already
/// collected (<see cref="CourierReturns"/>): couriers accept it only at a failed delivery attempt, so
/// each pass looks for marked parcels that have reached one.
/// </para>
/// <para>
/// A scope, and therefore a transaction, per parcel, as the tracking poll does: one courier refusing
/// must not undo the cancellations another accepted.
/// </para>
/// </remarks>
internal sealed partial class CourierCancellationWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<ShippingOptions> _options;
    private readonly ILogger<CourierCancellationWorker> _logger;

    /// <param name="services">Resolves a scoped context per parcel.</param>
    /// <param name="options">Interval and batch size, re-read each cycle.</param>
    /// <param name="logger">Reports what the sweep did.</param>
    public CourierCancellationWorker(
        IServiceProvider services,
        IOptionsMonitor<ShippingOptions> options,
        ILogger<CourierCancellationWorker> logger)
    {
        _services = services;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CurrentValue.CourierCancellationRetryEnabled)
        {
            return;
        }

        WorkerStarted(_logger, _options.CurrentValue.CourierCancellationRetryIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;

            try
            {
                await SweepAsync(options, stoppingToken).ConfigureAwait(false);
                await SweepReturnsAsync(options, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                SweepFailed(_logger, exception);
            }

            await Task.Delay(TimeSpan.FromMinutes(options.CourierCancellationRetryIntervalMinutes), stoppingToken)
                .ConfigureAwait(false);
        }
    }

    private async Task SweepAsync(ShippingOptions options, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> pending;

        using (var scope = _services.CreateScope())
        {
            pending = await scope.ServiceProvider
                .GetRequiredService<CourierCancellation>()
                .PendingAsync(options.CourierCancellationRetryBatchSize, cancellationToken)
                .ConfigureAwait(false);
        }

        if (pending.Count == 0)
        {
            return;
        }

        var withdrawn = 0;

        foreach (var id in pending)
        {
            using var scope = _services.CreateScope();

            var outcome = await scope.ServiceProvider
                .GetRequiredService<CourierCancellation>()
                .RetryAsync(id, cancellationToken)
                .ConfigureAwait(false);

            if (outcome == CourierWithdrawal.Withdrawn)
            {
                withdrawn++;
            }
        }

        SweepCompleted(_logger, pending.Count, withdrawn);
    }

    private async Task SweepReturnsAsync(ShippingOptions options, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> pending;

        using (var scope = _services.CreateScope())
        {
            pending = await scope.ServiceProvider
                .GetRequiredService<CourierReturns>()
                .PendingAsync(options.CourierCancellationRetryBatchSize, cancellationToken)
                .ConfigureAwait(false);
        }

        if (pending.Count == 0)
        {
            return;
        }

        var sent = 0;

        foreach (var id in pending)
        {
            using var scope = _services.CreateScope();

            if (await scope.ServiceProvider
                    .GetRequiredService<CourierReturns>()
                    .RetryAsync(id, cancellationToken)
                    .ConfigureAwait(false))
            {
                sent++;
            }
        }

        ReturnSweepCompleted(_logger, pending.Count, sent);
    }

    [LoggerMessage(EventId = 1798, Level = LogLevel.Information,
        Message = "Courier cancellation retry worker started, sweeping every {IntervalMinutes} minutes.")]
    private static partial void WorkerStarted(ILogger logger, int intervalMinutes);

    [LoggerMessage(EventId = 1799, Level = LogLevel.Error,
        Message = "A courier cancellation sweep failed. The loop continues.")]
    private static partial void SweepFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1778, Level = LogLevel.Information,
        Message = "Courier cancellation sweep retried {Count} parcel(s); {Withdrawn} were cancelled.")]
    private static partial void SweepCompleted(ILogger logger, int count, int withdrawn);

    [LoggerMessage(EventId = 1788, Level = LogLevel.Information,
        Message = "Courier return sweep tried {Count} parcel(s); the courier accepted {Sent}.")]
    private static partial void ReturnSweepCompleted(ILogger logger, int count, int sent);
}
