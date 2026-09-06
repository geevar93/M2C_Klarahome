using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Settlements.Infrastructure.Accounting;
using KlaraHome.Modules.Settlements.Infrastructure.Payouts;
using KlaraHome.Modules.Settlements.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Settlements.Infrastructure.Jobs;

/// <summary>
/// Draws the line under every seller's period, on time and without anybody asking.
/// </summary>
/// <remarks>
/// <para>
/// The scheduler docs/03-database-design.md §4.12 requires, and the reason settlement is a background
/// job rather than a screen: a marketplace with four thousand sellers cannot depend on somebody
/// remembering to press a button every Monday, and a seller whose statement arrives when finance gets
/// round to it has no cash-flow planning at all.
/// </para>
/// <para>
/// It closes the <em>previous</em> period and only once its hold has expired. The current period is
/// still accruing by definition; the one before it is finished, and the hold is what gives a shopper
/// time to send something back before the seller is paid for it.
/// </para>
/// <para>
/// It is bounded, resumable and safe to run twice. Each pass settles a limited number of sellers and
/// the next pass takes the rest; a cycle that already exists and is already closed is left alone,
/// which is enforced by a unique index rather than by the job being careful.
/// </para>
/// <para>
/// Off in the API and on in the worker, exactly as every scheduler before it. A job that ran in both
/// would race itself for the same rows.
/// </para>
/// </remarks>
internal sealed partial class SettlementCycleWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<SettlementsOptions> _options;
    private readonly ILogger<SettlementCycleWorker> _logger;

    public SettlementCycleWorker(
        IServiceProvider services,
        IOptionsMonitor<SettlementsOptions> options,
        ILogger<SettlementCycleWorker> logger)
    {
        _services = services;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CurrentValue.SchedulerEnabled)
        {
            return;
        }

        WorkerStarted(_logger, _options.CurrentValue.SchedulerIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;

            try
            {
                await RunAsync(options, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                RunFailed(_logger, exception);
            }

            await Task.Delay(TimeSpan.FromMinutes(options.SchedulerIntervalMinutes), stoppingToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Closes what is due, and builds the batch if the store has asked for one.</summary>
    private async Task RunAsync(SettlementsOptions options, CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();

        var settings = scope.ServiceProvider.GetRequiredService<IStoreSettings>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var policy = await settings.GetAsync<SettlementSettings>(cancellationToken).ConfigureAwait(false);

        var now = clock.UtcNow;
        var period = CyclePlanner.PreviousPeriod(now, policy);
        var closableFrom = CyclePlanner.ClosableFrom(period, policy);

        if (now < closableFrom)
        {
            NothingDue(_logger, closableFrom);
            return;
        }

        var cycles = scope.ServiceProvider.GetRequiredService<SettlementCycleService>();
        var context = scope.ServiceProvider.GetRequiredService<SettlementsDbContext>();

        var vendors = await cycles
            .SettleableVendorsAsync(period.End, options.SchedulerBatchSize, cancellationToken)
            .ConfigureAwait(false);

        if (vendors.Count == 0)
        {
            return;
        }

        // One transaction per seller rather than one for the run. A seller whose closing fails — a
        // missing profile, a constraint nobody expected — must not stop the other three hundred, and
        // the next pass will try them again.
        foreach (var vendorId in vendors)
        {
            try
            {
                await cycles.CloseAsync(vendorId, period, closedBy: null, cancellationToken).ConfigureAwait(false);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                VendorFailed(_logger, vendorId, exception);
            }
        }

        SweepFinished(_logger, vendors.Count, period.Start, period.End);

        if (policy.AutoBatchOnClose)
        {
            await AutoBatchAsync(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Builds a draft batch from everything that has just closed, where the store has asked for it.
    /// </summary>
    /// <remarks>
    /// It drafts and it never sends. Building a batch is cheap and reversible; approving one is a
    /// second signature and sending one is money, and neither of those is a thing a background job
    /// does unattended.
    /// </remarks>
    private async Task AutoBatchAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var payouts = services.GetRequiredService<PayoutWorkflow>();
        var context = services.GetRequiredService<SettlementsDbContext>();

        var batch = await payouts.BuildAsync([], requestedBy: null, cancellationToken).ConfigureAwait(false);

        if (batch.IsFailure)
        {
            return;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        BatchDrafted(_logger, batch.Value.Reference, batch.Value.VendorCount, batch.Value.TotalAmount);
    }

    [LoggerMessage(EventId = 1850, Level = LogLevel.Information,
        Message = "Settlement scheduler started; closing due periods every {IntervalMinutes} minute(s).")]
    private static partial void WorkerStarted(ILogger logger, int intervalMinutes);

    [LoggerMessage(EventId = 1851, Level = LogLevel.Debug,
        Message = "No settlement period is due yet; the current one can be closed from {ClosableFrom:u}.")]
    private static partial void NothingDue(ILogger logger, DateTimeOffset closableFrom);

    [LoggerMessage(EventId = 1852, Level = LogLevel.Information,
        Message = "Closed {VendorCount} seller period(s) for {PeriodStart:d MMM yyyy} to {PeriodEnd:d MMM yyyy}.")]
    private static partial void SweepFinished(
        ILogger logger,
        int vendorCount,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd);

    [LoggerMessage(EventId = 1853, Level = LogLevel.Error,
        Message = "The settlement period for vendor {VendorId} could not be closed; the next pass will try again.")]
    private static partial void VendorFailed(ILogger logger, Guid vendorId, Exception exception);

    [LoggerMessage(EventId = 1854, Level = LogLevel.Information,
        Message = "Drafted payout batch {BatchReference} for {VendorCount} seller(s), {TotalAmount}. "
                  + "It still needs approving before anything is sent.")]
    private static partial void BatchDrafted(
        ILogger logger,
        string batchReference,
        int vendorCount,
        decimal totalAmount);

    [LoggerMessage(EventId = 1855, Level = LogLevel.Error,
        Message = "A settlement scheduler pass failed.")]
    private static partial void RunFailed(ILogger logger, Exception exception);
}
