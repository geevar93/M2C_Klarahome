using KlaraHome.Modules.Payments.Infrastructure.Processing;
using KlaraHome.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Payments.Infrastructure.Jobs;

/// <summary>
/// Runs the reconciliation sweep on a timer (docs/08-integrations.md §1).
/// </summary>
/// <remarks>
/// <para>
/// Every fifteen minutes by default, which is the figure the integration design names. The work
/// itself is <see cref="PaymentReconciliationService"/>, so the admin's <em>reconcile now</em> button
/// and this loop do exactly the same thing — an operator investigating a stuck order and the
/// scheduled sweep must not be able to reach different answers.
/// </para>
/// <para>
/// Off in the API and on in the worker, exactly as the outbox dispatcher, the notification
/// dispatcher, the catalogue job runner, the reservation sweeper, the abandoned-cart sweeper and the
/// order-lifecycle sweeper are configured.
/// </para>
/// </remarks>
internal sealed partial class PaymentReconciliationWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<PaymentsOptions> _options;
    private readonly ILogger<PaymentReconciliationWorker> _logger;

    /// <param name="services">Resolves a scoped sweep.</param>
    /// <param name="options">The interval, re-read each cycle.</param>
    /// <param name="logger">Reports a sweep that failed outright.</param>
    public PaymentReconciliationWorker(
        IServiceProvider services,
        IOptionsMonitor<PaymentsOptions> options,
        ILogger<PaymentReconciliationWorker> logger)
    {
        _services = services;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CurrentValue.ReconciliationEnabled)
        {
            return;
        }

        ReconciliationStarted(_logger, _options.CurrentValue.ReconciliationIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.FromMinutes(_options.CurrentValue.ReconciliationIntervalMinutes);

            try
            {
                using var scope = _services.CreateScope();

                await scope.ServiceProvider
                    .GetRequiredService<PaymentReconciliationService>()
                    .SweepAsync(stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // The one loop in this module whose job is to notice that something else broke.
                // Killing it because a single sweep threw would remove the safety net entirely.
                SweepFailed(_logger, exception);
            }

            await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(EventId = 1580, Level = LogLevel.Information,
        Message = "Payment reconciliation started, sweeping every {IntervalMinutes} minute(s).")]
    private static partial void ReconciliationStarted(ILogger logger, int intervalMinutes);

    [LoggerMessage(EventId = 1581, Level = LogLevel.Error,
        Message = "A reconciliation sweep failed. The loop continues.")]
    private static partial void SweepFailed(ILogger logger, Exception exception);
}

/// <summary>
/// Pulls the gateway's settlement reports on a timer (docs/08-integrations.md §1).
/// </summary>
/// <remarks>
/// Daily by default, with a lookback window rather than a single day, because a report for Friday
/// can appear on Monday. Ingestion is idempotent on the gateway's settlement id, so re-reading the
/// same three days costs an indexed lookup per report and imports nothing twice.
/// </remarks>
internal sealed partial class SettlementIngestionWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<PaymentsOptions> _options;
    private readonly IClock _clock;
    private readonly ILogger<SettlementIngestionWorker> _logger;

    /// <param name="services">Resolves a scoped ingestion run.</param>
    /// <param name="options">The interval and lookback, re-read each cycle.</param>
    /// <param name="clock">The sanctioned clock.</param>
    /// <param name="logger">Reports a run that failed outright.</param>
    public SettlementIngestionWorker(
        IServiceProvider services,
        IOptionsMonitor<PaymentsOptions> options,
        IClock clock,
        ILogger<SettlementIngestionWorker> logger)
    {
        _services = services;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CurrentValue.SettlementIngestionEnabled)
        {
            return;
        }

        IngestionStarted(_logger, _options.CurrentValue.SettlementIntervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;

            try
            {
                using var scope = _services.CreateScope();

                var now = _clock.UtcNow;

                await scope.ServiceProvider
                    .GetRequiredService<SettlementIngestionService>()
                    .IngestAsync(now.AddDays(-options.SettlementLookbackDays), now, stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                IngestionFailed(_logger, exception);
            }

            await Task.Delay(TimeSpan.FromHours(options.SettlementIntervalHours), stoppingToken)
                .ConfigureAwait(false);
        }
    }

    [LoggerMessage(EventId = 1590, Level = LogLevel.Information,
        Message = "Settlement ingestion started, pulling every {IntervalHours} hour(s).")]
    private static partial void IngestionStarted(ILogger logger, int intervalHours);

    [LoggerMessage(EventId = 1591, Level = LogLevel.Error,
        Message = "A settlement ingestion run failed. The loop continues.")]
    private static partial void IngestionFailed(ILogger logger, Exception exception);
}
