using KlaraHome.Modules.Settlements.Domain;
using KlaraHome.Modules.Settlements.Infrastructure.Payouts;
using KlaraHome.Modules.Settlements.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Settlements.Infrastructure.Jobs;

/// <summary>
/// Asks the gateway what became of the transfers it was given.
/// </summary>
/// <remarks>
/// <para>
/// A bank transfer is not instant. A payout handed to the gateway is queued, then processing, then
/// processed — or reversed a day later because the beneficiary's account was closed — and this sweep
/// is what turns those into facts on the ledger. It is the payout half of the payment
/// reconciliation docs/08-integrations.md §1 already requires, and it exists for the same reason: a
/// platform that learns about money only from webhooks learns nothing on the day a webhook is lost.
/// </para>
/// <para>
/// It repairs what it is entitled to and reports the rest. A transfer the gateway calls processed is
/// completed here, a transfer it calls failed is failed here — those are its facts to give. A transfer
/// that has been in flight for longer than the deployment's patience is <em>reported</em> and not
/// touched: declaring it failed because it is slow would free the cycle to be paid a second time, and
/// paying twice is worse than paying late.
/// </para>
/// <para>
/// Off in the API and on in the worker, exactly as every sweeper before it.
/// </para>
/// </remarks>
internal sealed partial class PayoutReconciliationWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<SettlementsOptions> _options;
    private readonly ILogger<PayoutReconciliationWorker> _logger;

    public PayoutReconciliationWorker(
        IServiceProvider services,
        IOptionsMonitor<SettlementsOptions> options,
        ILogger<PayoutReconciliationWorker> logger)
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

        WorkerStarted(_logger, _options.CurrentValue.ReconciliationIntervalMinutes);

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

            await Task.Delay(TimeSpan.FromMinutes(options.ReconciliationIntervalMinutes), stoppingToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Re-reads every transfer that is still in flight, and finishes the batches that can finish.</summary>
    private async Task SweepAsync(SettlementsOptions options, CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<SettlementsDbContext>();
        var registry = scope.ServiceProvider.GetRequiredService<PayoutProviderRegistry>();
        var workflow = scope.ServiceProvider.GetRequiredService<PayoutWorkflow>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var inFlight = await context.PayoutItems
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == context.TenantId
                           && item.Status == PayoutItemStatus.Processing
                           && item.ProviderPayoutId != null)
            .OrderBy(item => item.SentAt)
            .Take(options.ReconciliationBatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (inFlight.Count == 0)
        {
            return;
        }

        var batchIds = inFlight.Select(item => item.PayoutBatchId).Distinct().ToArray();

        var batches = await context.PayoutBatches
            .IgnoreQueryFilters()
            .Where(batch => batch.TenantId == context.TenantId && batchIds.Contains(batch.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var stale = clock.UtcNow.AddHours(-options.StaleTransferHours);
        var repaired = 0;

        foreach (var item in inFlight)
        {
            var batch = batches.Find(candidate => candidate.Id == item.PayoutBatchId);

            if (batch is null)
            {
                continue;
            }

            var rail = registry.For(batch.Provider);
            var answer = await rail
                .FetchAsync(item.ProviderPayoutId!, cancellationToken)
                .ConfigureAwait(false);

            if (answer.IsFailure)
            {
                // The gateway could not be asked. Nothing is concluded from that — the transfer is
                // whatever it already was, and the next sweep asks again.
                continue;
            }

            if (!answer.Value.IsProcessed && !answer.Value.IsFailed && item.SentAt < stale)
            {
                TransferStuck(
                    _logger,
                    batch.Reference,
                    item.VendorId ?? Guid.Empty,
                    item.ProviderPayoutId,
                    answer.Value.Status);

                continue;
            }

            await workflow.ApplyAsync(batch, item, answer.Value, cancellationToken).ConfigureAwait(false);
            repaired++;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var batch in batches)
        {
            await workflow.SettleAsync(batch, cancellationToken).ConfigureAwait(false);
        }

        if (repaired > 0)
        {
            SweepFinished(_logger, repaired, inFlight.Count);
        }
    }

    [LoggerMessage(EventId = 1860, Level = LogLevel.Information,
        Message = "Payout reconciliation started; re-reading in-flight transfers every {IntervalMinutes} minute(s).")]
    private static partial void WorkerStarted(ILogger logger, int intervalMinutes);

    [LoggerMessage(EventId = 1861, Level = LogLevel.Information,
        Message = "Payout reconciliation settled {SettledCount} of {ExaminedCount} in-flight transfer(s).")]
    private static partial void SweepFinished(ILogger logger, int settledCount, int examinedCount);

    [LoggerMessage(EventId = 1862, Level = LogLevel.Warning,
        Message = "Payout {ProviderPayoutId} to vendor {VendorId} in batch {BatchReference} has been "
                  + "'{ProviderStatus}' for longer than expected. It has not been touched; somebody has "
                  + "to ask the gateway.")]
    private static partial void TransferStuck(
        ILogger logger,
        string batchReference,
        Guid vendorId,
        string? providerPayoutId,
        string providerStatus);

    [LoggerMessage(EventId = 1863, Level = LogLevel.Error,
        Message = "A payout reconciliation sweep failed.")]
    private static partial void SweepFailed(ILogger logger, Exception exception);
}
