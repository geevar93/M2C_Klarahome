using KlaraHome.Modules.Shipping.Infrastructure.Persistence;
using KlaraHome.Modules.Shipping.Infrastructure.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Shipping.Infrastructure.Jobs;

/// <summary>
/// Asks couriers about the parcels nobody has heard from (docs/08-integrations.md §2).
/// </summary>
/// <remarks>
/// <para>
/// The webhook-first design's second half. Webhooks carry almost everything and are not guaranteed
/// to carry anything: an aggregator's queue backs up, a deployment's URL changes, a signature secret
/// is rotated on one side only. Every half hour this sweep asks directly about anything booked,
/// unfinished and silent for a day, which turns "we stopped hearing about your parcel" from a
/// customer's discovery into a job's.
/// </para>
/// <para>
/// It runs in the worker and not in the API, like every other sweep here. A parcel is claimed by
/// updating its last-heard-from stamp as part of the sync, so two workers racing produce a duplicate
/// courier call and no duplicate scan — the tracking event's unique id is what makes that safe.
/// </para>
/// </remarks>
internal sealed partial class TrackingPollWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<ShippingOptions> _options;
    private readonly ILogger<TrackingPollWorker> _logger;

    /// <param name="services">Resolves a scoped context per parcel.</param>
    /// <param name="options">Interval, batch size and the silence threshold, re-read each cycle.</param>
    /// <param name="logger">Reports what the sweep found.</param>
    public TrackingPollWorker(
        IServiceProvider services,
        IOptionsMonitor<ShippingOptions> options,
        ILogger<TrackingPollWorker> logger)
    {
        _services = services;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CurrentValue.TrackingPollEnabled)
        {
            return;
        }

        WorkerStarted(_logger, _options.CurrentValue.TrackingPollIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;

            try
            {
                await SweepAsync(options, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                SweepFailed(_logger, exception);
            }

            await Task.Delay(TimeSpan.FromMinutes(options.TrackingPollIntervalMinutes), stoppingToken)
                .ConfigureAwait(false);
        }
    }

    private async Task SweepAsync(ShippingOptions options, CancellationToken cancellationToken)
    {
        List<Guid> stale;

        using (var scope = _services.CreateScope())
        {
            var synchroniser = scope.ServiceProvider.GetRequiredService<TrackingSynchroniser>();

            stale = [.. await synchroniser
                .StaleAsync(options.TrackingPollBatchSize, cancellationToken)
                .ConfigureAwait(false)];
        }

        if (stale.Count == 0)
        {
            return;
        }

        var asked = 0;

        foreach (var id in stale)
        {
            // A scope, and therefore a transaction, per parcel. One courier refusing must not roll
            // back the scans the previous nine returned.
            using var scope = _services.CreateScope();

            var context = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
            var synchroniser = scope.ServiceProvider.GetRequiredService<TrackingSynchroniser>();

            var shipment = await context.Shipments
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (shipment is null)
            {
                continue;
            }

            await synchroniser.SyncAsync(shipment, cancellationToken).ConfigureAwait(false);
            asked++;
        }

        SweepCompleted(_logger, asked);
    }

    [LoggerMessage(EventId = 1790, Level = LogLevel.Information,
        Message = "Tracking poll worker started, sweeping every {IntervalMinutes} minutes.")]
    private static partial void WorkerStarted(ILogger logger, int intervalMinutes);

    [LoggerMessage(EventId = 1791, Level = LogLevel.Error,
        Message = "A tracking sweep failed. The loop continues.")]
    private static partial void SweepFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1792, Level = LogLevel.Information,
        Message = "Tracking sweep asked couriers about {Count} silent parcel(s).")]
    private static partial void SweepCompleted(ILogger logger, int count);
}

/// <summary>
/// Refreshes the serviceability cache overnight (docs/08-integrations.md §2).
/// </summary>
/// <remarks>
/// <para>
/// The job that keeps the storefront's answers off the aggregator's API. It works the oldest answers
/// first, so an interrupted run resumes where it stopped, and it only refreshes PIN codes somebody
/// has already asked about — the cache covers where this store actually delivers rather than the
/// whole of India's directory.
/// </para>
/// <para>
/// A deployment with no aggregator runs this and does nothing, which is correct: there is nobody to
/// ask, and writing the manual adapter's blanket yes into the cache would turn "we do not know" into
/// "we checked".
/// </para>
/// </remarks>
internal sealed partial class ServiceabilityRefreshWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<ShippingOptions> _options;
    private readonly ILogger<ServiceabilityRefreshWorker> _logger;

    /// <param name="services">Resolves a scoped context per pass.</param>
    /// <param name="options">Interval and batch size, re-read each cycle.</param>
    /// <param name="logger">Reports what was refreshed.</param>
    public ServiceabilityRefreshWorker(
        IServiceProvider services,
        IOptionsMonitor<ShippingOptions> options,
        ILogger<ServiceabilityRefreshWorker> logger)
    {
        _services = services;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CurrentValue.ServiceabilityRefreshEnabled)
        {
            return;
        }

        WorkerStarted(_logger, _options.CurrentValue.ServiceabilityRefreshIntervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;

            try
            {
                await RefreshAsync(options, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                RefreshFailed(_logger, exception);
            }

            await Task.Delay(
                    TimeSpan.FromHours(options.ServiceabilityRefreshIntervalHours),
                    stoppingToken)
                .ConfigureAwait(false);
        }
    }

    private async Task RefreshAsync(ShippingOptions options, CancellationToken cancellationToken)
    {
        List<string> stale;

        using (var scope = _services.CreateScope())
        {
            var service = scope.ServiceProvider
                .GetRequiredService<Serviceability.ServiceabilityService>();

            stale = [.. await service
                .StalestAsync(options.ServiceabilityRefreshBatchSize, cancellationToken)
                .ConfigureAwait(false)];
        }

        foreach (var pincode in stale)
        {
            using var scope = _services.CreateScope();

            var service = scope.ServiceProvider
                .GetRequiredService<Serviceability.ServiceabilityService>();

            await service.RefreshAsync(pincode, pickupPincode: null, cancellationToken).ConfigureAwait(false);
        }

        if (stale.Count > 0)
        {
            RefreshCompleted(_logger, stale.Count);
        }
    }

    [LoggerMessage(EventId = 1795, Level = LogLevel.Information,
        Message = "Serviceability refresh worker started, running every {IntervalHours}h.")]
    private static partial void WorkerStarted(ILogger logger, int intervalHours);

    [LoggerMessage(EventId = 1796, Level = LogLevel.Error,
        Message = "A serviceability refresh failed. The loop continues.")]
    private static partial void RefreshFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1797, Level = LogLevel.Information,
        Message = "Serviceability refreshed for {Count} PIN code(s).")]
    private static partial void RefreshCompleted(ILogger logger, int count);
}
