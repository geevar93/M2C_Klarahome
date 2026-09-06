using KlaraHome.Contracts.Inventory;
using KlaraHome.Modules.Reporting.Domain;
using KlaraHome.Modules.Reporting.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Reporting.Infrastructure.Jobs;

/// <summary>
/// Records how much stock was sitting where, and how old it was, once a day.
/// </summary>
/// <remarks>
/// <para>
/// The only scheduled write of a fact in this module, and it exists because ageing is the one
/// inventory number no integration event carries. <c>StockLevelChanged</c> says what the balance is;
/// nothing in the message stream says when the units currently on the shelf arrived, and that answer
/// lives in Inventory's own ledger — reached here through <c>IInventoryAgeing</c>, a read-only seam
/// Inventory implements.
/// </para>
/// <para>
/// A series rather than a state, and that is the point of taking it at all. "How much stock is over
/// ninety days old" is a number a query could compute at any moment; "is that getting better or
/// worse" is the question a buying team actually asks, and only a row per day can answer it.
/// </para>
/// <para>
/// It runs at most once a day and is idempotent within it: the unique index is on the day and the
/// stock line, so a pass re-run after a failure replaces rather than doubles. For a series that
/// matters more than it would for a total — a doubled day makes the trend line wrong on both sides
/// of it.
/// </para>
/// </remarks>
/// <param name="services">The root provider; a scope is taken per pass.</param>
/// <param name="options">The hour, the page size and the ceiling.</param>
/// <param name="logger">Reports what was recorded.</param>
internal sealed partial class InventorySnapshotWorker(
    IServiceProvider services,
    IOptionsMonitor<ReportingOptions> options,
    ILogger<InventorySnapshotWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.SnapshotEnabled)
        {
            return;
        }

        WorkerStarted(logger, options.CurrentValue.SnapshotHourUtc);

        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = options.CurrentValue;

            try
            {
                await SnapshotIfDueAsync(settings, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                SnapshotFailed(logger, exception);
            }

            await Task.Delay(TimeSpan.FromMinutes(settings.SnapshotCheckIntervalMinutes), stoppingToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Takes today's snapshot if the hour has come and it has not been taken.</summary>
    /// <remarks>
    /// The "has it been taken" check is against the rows rather than against a stored marker, so a
    /// worker that was restarted or a deployment that briefly ran two of them still produces one
    /// snapshot. It is the cheapest possible query — an existence check on an indexed date — and it
    /// runs at most twice an hour.
    /// </remarks>
    private async Task SnapshotIfDueAsync(ReportingOptions settings, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var ageing = scope.ServiceProvider.GetRequiredService<IInventoryAgeing>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var now = clock.UtcNow;

        if (now.Hour < settings.SnapshotHourUtc)
        {
            return;
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var taken = await context.InventoryAgeing
            .AnyAsync(fact => fact.SnapshotOn == today, cancellationToken)
            .ConfigureAwait(false);

        if (taken)
        {
            return;
        }

        var recorded = 0;
        Guid? cursor = null;

        while (recorded < settings.MaxSnapshotLines && !cancellationToken.IsCancellationRequested)
        {
            var page = await ageing
                .EnumerateAsync(cursor, settings.SnapshotPageSize, cancellationToken)
                .ConfigureAwait(false);

            if (page.Items.Count == 0)
            {
                break;
            }

            foreach (var line in page.Items)
            {
                context.InventoryAgeing.Add(InventoryAgeFact.Record(
                    today,
                    line.ListingId,
                    line.WarehouseId,
                    line.WarehouseName,
                    line.VendorId,
                    line.Sku,
                    line.QuantityOnHand,
                    line.QuantityReserved,
                    line.LastInboundAt,
                    line.LastOutboundAt,
                    line.AgeDays));

                recorded++;
            }

            // Saved per page rather than at the end, so a walk that fails two thirds of the way
            // through leaves two thirds of a snapshot rather than nothing — and the existence check
            // above then skips the day, which is the honest outcome: a partial snapshot is visible in
            // the report as a smaller number, and re-running it would be the doubling this design
            // avoids.
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            cursor = page.NextCursor;

            if (cursor is null)
            {
                break;
            }
        }

        if (recorded > 0)
        {
            SnapshotTaken(logger, recorded, today);
        }
    }

    [LoggerMessage(
        EventId = 8230,
        Level = LogLevel.Information,
        Message = "Inventory ageing snapshot started; due at {SnapshotHourUtc}:00 UTC")]
    private static partial void WorkerStarted(ILogger logger, int snapshotHourUtc);

    [LoggerMessage(
        EventId = 8231,
        Level = LogLevel.Information,
        Message = "Recorded {LineCount} stock line(s) in the ageing snapshot for {SnapshotOn}")]
    private static partial void SnapshotTaken(ILogger logger, int lineCount, DateOnly snapshotOn);

    [LoggerMessage(
        EventId = 8232,
        Level = LogLevel.Error,
        Message = "Inventory ageing snapshot failed")]
    private static partial void SnapshotFailed(ILogger logger, Exception exception);
}
