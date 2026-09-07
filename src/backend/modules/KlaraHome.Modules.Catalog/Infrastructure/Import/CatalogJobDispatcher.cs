using System.Text;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Storage;
using KlaraHome.Modules.Catalog.Domain;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Catalog.Infrastructure.Import;

/// <summary>
/// Drains the catalogue job queue: claims a queued import or export, runs it, and records what
/// happened.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as the outbox and notification dispatchers, and for the same reasons. Rows are
/// claimed with <c>FOR UPDATE SKIP LOCKED</c>, so running more than one worker is safe by
/// construction rather than by convention, and the loop is disabled by configuration in the API —
/// every replica polling would multiply the contention for no gain.
/// </para>
/// <para>
/// One job at a time rather than a batch. An import is minutes of work and a batch would hold a
/// claim on jobs a second worker could be running; the throughput that matters here is jobs in
/// parallel across workers, not rows per poll.
/// </para>
/// </remarks>
internal sealed partial class CatalogJobDispatcher : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<CatalogOptions> _options;
    private readonly IClock _clock;
    private readonly ILogger<CatalogJobDispatcher> _logger;

    /// <param name="services">Resolves a scoped context and runner per job.</param>
    /// <param name="options">Poll interval and attempt budget, re-read each cycle.</param>
    /// <param name="clock">The sanctioned clock.</param>
    /// <param name="logger">Reports job outcomes.</param>
    public CatalogJobDispatcher(
        IServiceProvider services,
        IOptionsMonitor<CatalogOptions> options,
        IClock clock,
        ILogger<CatalogJobDispatcher> logger)
    {
        _services = services;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CurrentValue.JobRunnerEnabled)
        {
            return;
        }

        RunnerStarted(_logger, _options.CurrentValue.JobPollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;

            try
            {
                // A job that ran means there may be another waiting; go round again rather than
                // idling while a backlog grows.
                if (await DrainOneAsync(options, stoppingToken).ConfigureAwait(false))
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
                // A poll failure is infrastructure-level. Log it and keep the loop alive; killing
                // this service would stop every later import too.
                PollFailed(_logger, exception);
            }

            await Task.Delay(TimeSpan.FromSeconds(options.JobPollIntervalSeconds), stoppingToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Claims and runs one job. False when the queue was empty.</summary>
    private async Task<bool> DrainOneAsync(CatalogOptions options, CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

        var strategy = context.Database.CreateExecutionStrategy();
        var ran = false;

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            // FOR UPDATE SKIP LOCKED: a second worker polling at the same instant takes the next
            // job rather than blocking on this one, which is what makes horizontal scaling safe
            // without a distributed lock.
            var claimed = await context
                .Claim<CatalogJob>($"{CatalogModule.SchemaName}.catalog_jobs", $"status = 'Queued'", "created_at", 1)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (claimed is null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            claimed.Start(_clock.UtcNow);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            ran = true;

            await RunAsync(scope.ServiceProvider, context, storage, claimed, options, cancellationToken)
                .ConfigureAwait(false);
        }).ConfigureAwait(false);

        return ran;
    }

    private async Task RunAsync(
        IServiceProvider provider,
        CatalogDbContext context,
        IFileStorage storage,
        CatalogJob job,
        CatalogOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            if (job.Kind == CatalogJobKind.ProductImport)
            {
                var content = await ReadAsync(storage, job.SourceKey, cancellationToken).ConfigureAwait(false);

                if (content is null)
                {
                    job.Fail("The uploaded file is no longer in storage.", _clock.UtcNow);
                }
                else
                {
                    await provider.GetRequiredService<ProductImportRunner>()
                        .RunAsync(job, content, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            else
            {
                var key = await provider.GetRequiredService<ProductExportRunner>()
                    .RunAsync(job, cancellationToken)
                    .ConfigureAwait(false);

                job.Complete(_clock.UtcNow, key);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The change tracker may be holding entities from the row that threw, so it is cleared
            // before the failure itself is written — otherwise saving the job would try to save
            // them too, and fail again.
            context.ChangeTracker.Clear();
            context.Attach(job);

            JobFailed(_logger, exception, job.Id, job.Attempts);

            if (job.Attempts >= options.MaxJobAttempts)
            {
                job.Fail($"Abandoned after {job.Attempts} attempts: {exception.Message}", _clock.UtcNow);
            }
            else
            {
                // Back to Queued so the next poll retries it. A transient storage outage should not
                // cost a merchandiser their upload.
                job.Requeue();
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        JobFinished(_logger, job.Id, job.Status, job.SucceededRows, job.FailedRows);
    }

    private static async Task<string?> ReadAsync(
        IFileStorage storage,
        string? key,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        await using var stream = await storage
            .GetAsync(key, StorageVisibility.Private, cancellationToken)
            .ConfigureAwait(false);

        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);

        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(
        EventId = 6200,
        Level = LogLevel.Information,
        Message = "Catalogue job runner started; polling every {IntervalSeconds}s")]
    private static partial void RunnerStarted(ILogger logger, int intervalSeconds);

    [LoggerMessage(EventId = 6201, Level = LogLevel.Error, Message = "Catalogue job poll failed")]
    private static partial void PollFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6202, Level = LogLevel.Error, Message = "Catalogue job {JobId} failed on attempt {Attempts}")]
    private static partial void JobFailed(ILogger logger, Exception exception, Guid jobId, int attempts);

    [LoggerMessage(
        EventId = 6203,
        Level = LogLevel.Information,
        Message = "Catalogue job {JobId} finished as {Status}: {Succeeded} applied, {Failed} rejected")]
    private static partial void JobFinished(
        ILogger logger,
        Guid jobId,
        Domain.CatalogJobStatus status,
        int succeeded,
        int failed);
}
