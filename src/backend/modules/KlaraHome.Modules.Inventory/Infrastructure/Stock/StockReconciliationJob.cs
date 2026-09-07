using KlaraHome.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Inventory.Infrastructure.Stock;

/// <summary>One stock row whose cached quantities disagree with its ledger.</summary>
/// <param name="StockItemId">The row.</param>
/// <param name="Sku">Which goods, so the report reads like something.</param>
/// <param name="CachedOnHand">What the row says.</param>
/// <param name="LedgerOnHand">What the ledger sums to.</param>
/// <param name="CachedReserved">What the row says is held.</param>
/// <param name="LedgerReserved">What the ledger says is held.</param>
internal sealed record StockDrift(
    Guid StockItemId,
    string Sku,
    int CachedOnHand,
    int LedgerOnHand,
    int CachedReserved,
    int LedgerReserved);

/// <summary>
/// Asserts nightly that both quantity caches still equal the ledger sums
/// (docs/03-database-design.md §4.5).
/// </summary>
/// <remarks>
/// <para>
/// It <b>reports</b>; it does not repair. A cache that has drifted from the ledger is a defect in
/// the code that moved the stock, and silently correcting it would destroy the only evidence of how
/// it happened — the next occurrence would then be just as invisible. The alert is the deliverable.
/// </para>
/// <para>
/// The comparison runs entirely in SQL, because the alternative is streaming every ledger entry
/// ever written into memory once a night. It reads across tenants for the same reason the sweeper
/// does: this is the platform checking itself, and a filtered query would check only whichever
/// tenant the ambient context happened to name.
/// </para>
/// </remarks>
internal sealed partial class StockReconciliationJob : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<InventoryOptions> _options;
    private readonly ILogger<StockReconciliationJob> _logger;

    /// <param name="services">Resolves a scoped context per pass.</param>
    /// <param name="options">Whether to run, and how often.</param>
    /// <param name="logger">Where the drift report goes.</param>
    public StockReconciliationJob(
        IServiceProvider services,
        IOptionsMonitor<InventoryOptions> options,
        ILogger<StockReconciliationJob> logger)
    {
        _services = services;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// The reconciliation itself. Left-joined so a stock row with no ledger entries at all is
    /// compared against zero rather than dropped — a cached quantity with no movement behind it is
    /// precisely the drift worth catching.
    /// </summary>
    /// <remarks>
    /// The aliases are snake_case, not the property names. <c>SqlQuery</c> maps a result to the
    /// model's <em>column</em> names, and this model applies the snake-case naming convention to
    /// every entity type it builds — a query type included. Aliasing to <c>"CachedOnHand"</c>
    /// produces a column EF then reports as missing under the name it was actually looking for.
    /// </remarks>
    internal const string DriftQuery =
        """
        SELECT  i.id             AS "stock_item_id",
                i.sku            AS "sku",
                i.quantity_on_hand   AS "cached_on_hand",
                COALESCE(l.on_hand, 0)  AS "ledger_on_hand",
                i.quantity_reserved  AS "cached_reserved",
                COALESCE(l.reserved, 0) AS "ledger_reserved"
        FROM inventory.stock_items i
        LEFT JOIN (
            SELECT stock_item_id,
                   SUM(change)::int          AS on_hand,
                   SUM(reserved_change)::int AS reserved
            FROM inventory.stock_ledger_entries
            GROUP BY stock_item_id
        ) l ON l.stock_item_id = i.id
        WHERE i.quantity_on_hand  <> COALESCE(l.on_hand, 0)
           OR i.quantity_reserved <> COALESCE(l.reserved, 0)
        ORDER BY i.sku
        """;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CurrentValue.ReconciliationEnabled)
        {
            return;
        }

        ReconciliationStarted(_logger, _options.CurrentValue.ReconciliationIntervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                ReconciliationFailed(_logger, exception);
            }

            await Task
                .Delay(
                    TimeSpan.FromHours(_options.CurrentValue.ReconciliationIntervalHours),
                    stoppingToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Runs one pass and reports what it found.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The drifting rows, so a caller can act on them; empty when everything agrees.</returns>
    internal async Task<IReadOnlyList<StockDrift>> ReconcileAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        // EF1002 does not apply: the query is a compile-time constant in this assembly with nothing
        // caller-supplied anywhere in it.
#pragma warning disable EF1002
        var drifted = await context.Database
            .SqlQueryRaw<StockDrift>(DriftQuery)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
#pragma warning restore EF1002

        if (drifted.Count == 0)
        {
            ReconciliationClean(_logger);
            return drifted;
        }

        DriftDetected(_logger, drifted.Count);

        foreach (var drift in drifted)
        {
            DriftRow(
                _logger,
                drift.Sku,
                drift.StockItemId,
                drift.CachedOnHand,
                drift.LedgerOnHand,
                drift.CachedReserved,
                drift.LedgerReserved);
        }

        return drifted;
    }

    [LoggerMessage(
        EventId = 7120,
        Level = LogLevel.Information,
        Message = "Stock reconciliation started; running every {IntervalHours}h")]
    private static partial void ReconciliationStarted(ILogger logger, int intervalHours);

    [LoggerMessage(EventId = 7121, Level = LogLevel.Error, Message = "Stock reconciliation failed")]
    private static partial void ReconciliationFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 7122,
        Level = LogLevel.Information,
        Message = "Stock reconciliation clean: every cached quantity matches its ledger")]
    private static partial void ReconciliationClean(ILogger logger);

    [LoggerMessage(
        EventId = 7123,
        Level = LogLevel.Error,
        Message = "Stock reconciliation found {Count} rows whose cached quantities do not match the ledger")]
    private static partial void DriftDetected(ILogger logger, int count);

    [LoggerMessage(
        EventId = 7124,
        Level = LogLevel.Error,
        Message = "Drift on {Sku} ({StockItemId}): on hand cached {CachedOnHand} vs ledger {LedgerOnHand}; "
                  + "reserved cached {CachedReserved} vs ledger {LedgerReserved}")]
    private static partial void DriftRow(
        ILogger logger,
        string sku,
        Guid stockItemId,
        int cachedOnHand,
        int ledgerOnHand,
        int cachedReserved,
        int ledgerReserved);
}
