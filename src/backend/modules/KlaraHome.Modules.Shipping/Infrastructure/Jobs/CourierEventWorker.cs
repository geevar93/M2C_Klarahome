using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure.Persistence;
using KlaraHome.Modules.Shipping.Infrastructure.Processing;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Shipping.Infrastructure.Jobs;

/// <summary>
/// Drains the stored courier webhooks, and dead-letters the ones that will never work.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as the payments gateway-event worker, the outbox dispatcher, the notification
/// dispatcher and every sweeper before them, and for the same reasons. Rows are claimed with
/// <c>FOR UPDATE SKIP LOCKED</c>, so running more than one worker is safe by construction rather
/// than by convention, and the loop is disabled by configuration in the API, where every replica
/// polling would multiply contention for no gain.
/// </para>
/// <para>
/// Each event is committed in its own transaction. One poisonous webhook must not roll back the
/// nine good ones claimed alongside it — that would turn a single unprocessable payload into a queue
/// that never drains, and a queue that never drains is a shop that stops telling people where their
/// parcels are.
/// </para>
/// </remarks>
internal sealed partial class CourierEventWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<ShippingOptions> _options;
    private readonly IClock _clock;
    private readonly ILogger<CourierEventWorker> _logger;

    /// <param name="services">Resolves a scoped context per event.</param>
    /// <param name="options">Poll interval, batch size and retry budget, re-read each cycle.</param>
    /// <param name="clock">The sanctioned clock.</param>
    /// <param name="logger">Reports what drained and what was dead-lettered.</param>
    public CourierEventWorker(
        IServiceProvider services,
        IOptionsMonitor<ShippingOptions> options,
        IClock clock,
        ILogger<CourierEventWorker> logger)
    {
        _services = services;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CurrentValue.EventProcessorEnabled)
        {
            return;
        }

        WorkerStarted(_logger, _options.CurrentValue.EventPollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;

            try
            {
                // A full batch means there may be more waiting; go round again rather than idling
                // while a backlog of scans leaves shoppers looking at stale tracking.
                if (await DrainAsync(options, stoppingToken).ConfigureAwait(false) >= options.EventBatchSize)
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
                // A drain failure is infrastructure-level. Log it and keep the loop alive; killing
                // this service would strand every later webhook too.
                DrainFailed(_logger, exception);
            }

            await Task.Delay(TimeSpan.FromSeconds(options.EventPollIntervalSeconds), stoppingToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Claims a batch and applies it. Returns how many events were touched.</summary>
    private async Task<int> DrainAsync(ShippingOptions options, CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        var now = _clock.UtcNow;

        // The query filters are bypassed on purpose. This loop has no tenant and no caller — it is
        // the platform draining its own inbox — and a filtered query would drain only whichever
        // tenant the ambient context happened to name.
        var due = await context.CourierEvents
            .FromSql(
                $"""
                 SELECT * FROM shipping.courier_events
                 WHERE signature_valid
                   AND (status = 'Pending'
                        OR (status = 'Failed' AND next_attempt_at IS NOT NULL AND next_attempt_at <= {now}))
                 ORDER BY received_at
                 LIMIT {options.EventBatchSize}
                 FOR UPDATE SKIP LOCKED
                 """)
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(entry => entry.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var touched = 0;

        foreach (var id in due)
        {
            // A scope, and therefore a transaction, per event.
            await ApplyOneAsync(id, options, cancellationToken).ConfigureAwait(false);
            touched++;
        }

        return touched;
    }

    private async Task ApplyOneAsync(Guid eventId, ShippingOptions options, CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<CourierEventProcessor>();

        var stored = await context.CourierEvents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(entry => entry.Id == eventId, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null || stored.Status is CourierEventStatus.Processed or CourierEventStatus.Ignored)
        {
            return;
        }

        var now = _clock.UtcNow;

        try
        {
            var applied = await processor.ProcessAsync(stored, cancellationToken).ConfigureAwait(false);

            if (applied.IsSuccess)
            {
                // The processor marks and saves when it matched a parcel. Anything still pending
                // here resolved to nothing, and retrying will not make an unknown waybill exist.
                if (stored.Status is CourierEventStatus.Pending or CourierEventStatus.Failed)
                {
                    stored.MarkIgnored(now);
                }
            }
            else
            {
                Fail(stored, applied.Error.Message, options, now);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Fail(stored, exception.Message, options, now);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Fail(CourierEvent stored, string error, ShippingOptions options, DateTimeOffset now)
    {
        // Linear backoff on the attempt count: an aggregator that is down stays down for minutes,
        // not hours, and an exponential curve here would leave the last retries days apart.
        var retryAt = now.AddSeconds(options.EventRetryBackoffSeconds * (stored.Attempts + 1));

        if (stored.MarkFailed(error, options.MaxEventAttempts, retryAt))
        {
            DeadLettered(_logger, stored.ProviderEventId, stored.Awb ?? "<none>", error);
        }
    }

    [LoggerMessage(EventId = 1780, Level = LogLevel.Information,
        Message = "Courier event worker started, polling every {IntervalSeconds}s.")]
    private static partial void WorkerStarted(ILogger logger, int intervalSeconds);

    [LoggerMessage(EventId = 1781, Level = LogLevel.Error,
        Message = "A courier event drain failed. The loop continues.")]
    private static partial void DrainFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1782, Level = LogLevel.Error,
        Message = "Courier event {ProviderEventId} for parcel {Awb} was dead-lettered after exhausting its "
                  + "attempts: {Detail}")]
    private static partial void DeadLettered(
        ILogger logger,
        string providerEventId,
        string awb,
        string detail);
}
