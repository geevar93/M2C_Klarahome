using KlaraHome.Modules.Search.Infrastructure.Projection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Search.Infrastructure.Jobs;

/// <summary>
/// Rebuilds the index rows that have fallen behind.
/// </summary>
/// <remarks>
/// <para>
/// The safety net under the event pipeline, not the way the index is kept current. Events do that,
/// within seconds; this catches the row whose event was dropped, whose handler threw, or that was
/// written while a module was being deployed. On a healthy deployment it finds nothing every fifteen
/// minutes for ever, and that is exactly what it is for.
/// </para>
/// <para>
/// Bounded and resumable: each pass rebuilds a limited number of rows, oldest first, and the next
/// pass takes the rest. A sweep that tried to catch up a whole catalogue in one go would hold a read
/// on it while the storefront was trying to search it.
/// </para>
/// <para>
/// Off in the API and on in the worker, exactly as every sweeper before it. Two processes rebuilding
/// the same rows would corrupt nothing — every write is an upsert keyed on the variant — but it
/// would double the load on the catalogue for no benefit at all.
/// </para>
/// </remarks>
internal sealed partial class SearchReindexWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<SearchOptions> _options;
    private readonly ILogger<SearchReindexWorker> _logger;

    public SearchReindexWorker(
        IServiceProvider services,
        IOptionsMonitor<SearchOptions> options,
        ILogger<SearchReindexWorker> logger)
    {
        _services = services;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CurrentValue.ReindexEnabled)
        {
            return;
        }

        WorkerStarted(_logger, _options.CurrentValue.ReindexIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;

            try
            {
                using var scope = _services.CreateScope();

                var index = scope.ServiceProvider.GetRequiredService<SearchIndexService>();

                await index.RefreshStaleAsync(options.ReindexBatchSize, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Swallowed and logged rather than allowed to stop the host. A sweep that fails
                // because the catalogue is briefly unreachable must try again in fifteen minutes,
                // not take the worker process down with it.
                SweepFailed(_logger, exception);
            }

            await Task.Delay(TimeSpan.FromMinutes(options.ReindexIntervalMinutes), stoppingToken)
                .ConfigureAwait(false);
        }
    }

    [LoggerMessage(
        EventId = 7950,
        Level = LogLevel.Information,
        Message = "Search reindex sweep started; running every {IntervalMinutes} minute(s)")]
    private static partial void WorkerStarted(ILogger logger, int intervalMinutes);

    [LoggerMessage(
        EventId = 7951,
        Level = LogLevel.Error,
        Message = "A search reindex sweep failed; it will run again on the next interval")]
    private static partial void SweepFailed(ILogger logger, Exception exception);
}
