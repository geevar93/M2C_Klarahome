using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Modules.Payments.Domain;
using KlaraHome.Modules.Payments.Infrastructure.Persistence;
using KlaraHome.Modules.Payments.Infrastructure.Processing;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Payments.Infrastructure.Jobs;

/// <summary>
/// Drains the stored webhooks, and dead-letters the ones that will never work.
/// </summary>
/// <remarks>
/// <para>
/// The other half of the webhook contract in docs/04-api-specification.md §5. The endpoint verifies,
/// stores and answers <c>200</c> in milliseconds; this applies what was stored, with a retry budget
/// and a dead-letter queue behind it, so a gateway is never kept waiting while an order is confirmed,
/// stock committed and an invoice raised.
/// </para>
/// <para>
/// The same shape as the outbox, notification, reservation, abandoned-cart and order-lifecycle
/// sweepers, and for the same reasons. Rows are claimed with <c>FOR UPDATE SKIP LOCKED</c>, so
/// running more than one worker is safe by construction rather than by convention, and the loop is
/// disabled by configuration in the API, where every replica polling would multiply the contention
/// for no gain.
/// </para>
/// <para>
/// Each event is committed in its own transaction. One poisonous event must not roll back the nine
/// good ones claimed alongside it — that would turn a single unprocessable webhook into a queue that
/// never drains.
/// </para>
/// </remarks>
internal sealed partial class GatewayEventWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<PaymentsOptions> _options;
    private readonly IClock _clock;
    private readonly ILogger<GatewayEventWorker> _logger;

    /// <param name="services">Resolves a scoped context per event.</param>
    /// <param name="options">Poll interval, batch size and retry budget, re-read each cycle.</param>
    /// <param name="clock">The sanctioned clock.</param>
    /// <param name="logger">Reports what drained and what was dead-lettered.</param>
    public GatewayEventWorker(
        IServiceProvider services,
        IOptionsMonitor<PaymentsOptions> options,
        IClock clock,
        ILogger<GatewayEventWorker> logger)
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

        ProcessorStarted(_logger, _options.CurrentValue.EventPollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;

            try
            {
                // A full batch means there may be more waiting; go round again rather than idling
                // for five seconds while a backlog of captures leaves orders unconfirmed.
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
    private async Task<int> DrainAsync(PaymentsOptions options, CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var now = _clock.UtcNow;

        // The query filters are bypassed on purpose. This loop has no tenant and no caller — it is
        // the platform draining its own inbox — and a filtered query would drain only whichever
        // tenant the ambient context happened to name.
        var due = await context
            .Claim<GatewayEvent>(
                "payments.gateway_events",
                $"""
                 signature_valid
                   AND (status = 'Pending'
                        OR (status = 'Failed' AND next_attempt_at IS NOT NULL AND next_attempt_at <= {now}))
                 """,
                "received_at",
                options.EventBatchSize)
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(entry => entry.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var touched = 0;

        foreach (var id in due)
        {
            // A scope, and therefore a transaction, per event. One poisonous webhook must not roll
            // back the good ones claimed alongside it.
            await ApplyOneAsync(id, options, cancellationToken).ConfigureAwait(false);
            touched++;
        }

        return touched;
    }

    private async Task ApplyOneAsync(Guid eventId, PaymentsOptions options, CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<GatewayEventProcessor>();

        var stored = await context.GatewayEvents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(entry => entry.Id == eventId, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null || stored.Status is GatewayEventStatus.Processed or GatewayEventStatus.Ignored)
        {
            return;
        }

        var now = _clock.UtcNow;

        try
        {
            var applied = await processor.ProcessAsync(stored, cancellationToken).ConfigureAwait(false);

            if (applied.IsSuccess)
            {
                // A subscribed type that resolved to nothing is still processed: retrying will not
                // make a payment we never opened start existing.
                if (stored.Status is GatewayEventStatus.Pending or GatewayEventStatus.Failed)
                {
                    if (GatewayEventTypes.Subscribed.Contains(stored.EventType))
                    {
                        stored.MarkProcessed(stored.PaymentId, now);
                    }
                    else
                    {
                        stored.MarkIgnored(now);
                    }
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

    private void Fail(GatewayEvent stored, string error, PaymentsOptions options, DateTimeOffset now)
    {
        // Linear backoff on the attempt count: a gateway that is down stays down for minutes, not
        // hours, and an exponential curve here would leave the last retries days apart.
        var retryAt = now.AddSeconds(options.EventRetryBackoffSeconds * (stored.Attempts + 1));

        if (stored.MarkFailed(error, options.MaxEventAttempts, retryAt))
        {
            DeadLettered(_logger, stored.ProviderEventId, stored.EventType, error);
        }
    }

    [LoggerMessage(EventId = 1570, Level = LogLevel.Information,
        Message = "Gateway event processor started, polling every {IntervalSeconds}s.")]
    private static partial void ProcessorStarted(ILogger logger, int intervalSeconds);

    [LoggerMessage(EventId = 1571, Level = LogLevel.Error,
        Message = "A gateway event drain failed. The loop continues.")]
    private static partial void DrainFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1572, Level = LogLevel.Error,
        Message = "Gateway event {ProviderEventId} ({EventType}) was dead-lettered after exhausting its "
                  + "attempts: {Detail}")]
    private static partial void DeadLettered(
        ILogger logger,
        string providerEventId,
        string eventType,
        string detail);
}
