using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Returns.Domain;
using KlaraHome.Modules.Returns.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Returns.Infrastructure.Jobs;

/// <summary>
/// Finds returns that have gone quiet.
/// </summary>
/// <remarks>
/// <para>
/// A returns operation fails silently rather than loudly. An approved return whose courier never
/// came, a parcel that arrived and sat in the receiving bay, a QC pass whose refund was never
/// raised — none of them throws an error and all of them are a shopper who has stopped being told
/// anything. This sweep is what turns them into something on somebody's screen.
/// </para>
/// <para>
/// It reports rather than repairs, and that is deliberate. Re-booking a courier automatically would
/// send a second van to a door where the first one may already have called; raising a refund
/// automatically would move money on a return nobody has looked at. What an operator needs is to be
/// told, and that is what a warning in the log with the RMA number is for.
/// </para>
/// <para>
/// Off in the API and on in the worker, exactly as every sweeper before it. A job that ran in both
/// would do its work twice.
/// </para>
/// </remarks>
internal sealed partial class StaleReturnWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<ReturnsOptions> _options;
    private readonly ILogger<StaleReturnWorker> _logger;

    public StaleReturnWorker(
        IServiceProvider services,
        IOptionsMonitor<ReturnsOptions> options,
        ILogger<StaleReturnWorker> logger)
    {
        _services = services;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CurrentValue.SweeperEnabled)
        {
            return;
        }

        WorkerStarted(_logger, _options.CurrentValue.SweepIntervalMinutes);

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

            await Task.Delay(TimeSpan.FromMinutes(options.SweepIntervalMinutes), stoppingToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Looks for the three ways a return goes quiet, and names each one it finds.</summary>
    private async Task SweepAsync(ReturnsOptions options, CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ReturnsDbContext>();
        var settings = scope.ServiceProvider.GetRequiredService<IStoreSettings>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var policy = await settings.GetAsync<ReturnsSettings>(cancellationToken).ConfigureAwait(false);
        var cutoff = clock.UtcNow.AddDays(-Math.Max(1, policy.PickupSlaDays));

        // Approved or booked and still not collected, past the store's own patience.
        var uncollected = await context.Returns
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(request =>
                (request.Status == ReturnStatus.Approved || request.Status == ReturnStatus.PickupScheduled)
                && request.ApprovedAt != null
                && request.ApprovedAt < cutoff)
            .OrderBy(request => request.ApprovedAt)
            .Take(options.SweepBatchSize)
            .Select(request => request.ReturnNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var number in uncollected)
        {
            PickupOverdue(_logger, number, policy.PickupSlaDays);
        }

        // Arrived and never opened. The number a returns operation is actually run on.
        var uninspected = await context.Returns
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(request => request.Status == ReturnStatus.Received && request.ReceivedAt < cutoff)
            .OrderBy(request => request.ReceivedAt)
            .Take(options.SweepBatchSize)
            .Select(request => request.ReturnNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var number in uninspected)
        {
            InspectionOverdue(_logger, number);
        }

        // Passed inspection and the money never left. The one that costs the platform a complaint.
        var unpaid = await context.Returns
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(request => request.Status == ReturnStatus.QcPassed && request.InspectedAt < cutoff)
            .OrderBy(request => request.InspectedAt)
            .Take(options.SweepBatchSize)
            .Select(request => request.ReturnNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var number in unpaid)
        {
            RefundOverdue(_logger, number);
        }
    }

    [LoggerMessage(EventId = 1770, Level = LogLevel.Information,
        Message = "The stale-return sweep is running every {Minutes} minute(s).")]
    private static partial void WorkerStarted(ILogger logger, int minutes);

    [LoggerMessage(EventId = 1771, Level = LogLevel.Error, Message = "The stale-return sweep failed.")]
    private static partial void SweepFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1772, Level = LogLevel.Warning,
        Message = "Return {ReturnNumber} was approved more than {Days} day(s) ago and has still not been "
                  + "collected.")]
    private static partial void PickupOverdue(ILogger logger, string returnNumber, int days);

    [LoggerMessage(EventId = 1773, Level = LogLevel.Warning,
        Message = "Return {ReturnNumber} arrived and has still not been inspected.")]
    private static partial void InspectionOverdue(ILogger logger, string returnNumber);

    [LoggerMessage(EventId = 1774, Level = LogLevel.Warning,
        Message = "Return {ReturnNumber} passed inspection and has still not been refunded.")]
    private static partial void RefundOverdue(ILogger logger, string returnNumber);
}
