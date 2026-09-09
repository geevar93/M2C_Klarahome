using KlaraHome.Modules.Payments.Domain;
using KlaraHome.Modules.Payments.Infrastructure;
using KlaraHome.Modules.Payments.Infrastructure.Persistence;
using KlaraHome.Modules.Payments.Infrastructure.Processing;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Applies every stored webhook that is due, exactly the way <c>GatewayEventWorker</c> does — one
/// scope per event, the same processor, the same failure and dead-letter bookkeeping — without the
/// polling loop.
/// </summary>
/// <remarks>
/// The worker itself is a <c>BackgroundService</c> whose drain loop is private and runs on a timer;
/// a test that needs the same behaviour deterministically asks for one pass, the same way
/// <see cref="CommerceTestBase.RunOnceAsync{TService}"/> does for every other sweeper. This is that
/// one pass, reimplemented against the real <see cref="GatewayEventProcessor"/> and the real domain
/// methods rather than the private loop, because there is nothing else in the worker worth
/// duplicating: claiming, applying, and marking failed or processed.
/// </remarks>
internal static class GatewayEventDrain
{
    /// <summary>Processes every pending or due-for-retry event once, and commits what happened.</summary>
    /// <param name="factory">The host whose events should be drained.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many events were touched.</returns>
    public static async Task<int> RunOnceAsync(CommerceApiFactory factory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);

        using var scope = factory.Services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<GatewayEventProcessor>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var now = clock.UtcNow;

        var due = await context.GatewayEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(entry => entry.SignatureValid
                            && (entry.Status == GatewayEventStatus.Pending
                                || (entry.Status == GatewayEventStatus.Failed
                                    && entry.NextAttemptAt != null
                                    && entry.NextAttemptAt <= now)))
            .Select(entry => entry.Id)
            .ToListAsync(cancellationToken);

        var touched = 0;

        foreach (var id in due)
        {
            await ApplyOneAsync(factory, id, cancellationToken);
            touched++;
        }

        return touched;
    }

    /// <summary>Runs one pass repeatedly until nothing more is due, or a cap is reached.</summary>
    /// <param name="factory">The host whose events should be drained.</param>
    /// <param name="maxPasses">The most passes to run — an event that keeps failing is due again
    /// only after its backoff, so this does not spin.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<int> DrainAllAsync(
        CommerceApiFactory factory,
        int maxPasses,
        CancellationToken cancellationToken)
    {
        var total = 0;

        for (var pass = 0; pass < maxPasses; pass++)
        {
            var touched = await RunOnceAsync(factory, cancellationToken);
            total += touched;

            if (touched == 0)
            {
                return total;
            }
        }

        return total;
    }

    private static async Task ApplyOneAsync(CommerceApiFactory factory, Guid eventId, CancellationToken cancellationToken)
    {
        using var scope = factory.Services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<GatewayEventProcessor>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<PaymentsOptions>>().Value;

        var stored = await context.GatewayEvents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(entry => entry.Id == eventId, cancellationToken);

        if (stored is null || stored.Status is GatewayEventStatus.Processed or GatewayEventStatus.Ignored)
        {
            return;
        }

        var now = clock.UtcNow;

        try
        {
            var applied = await processor.ProcessAsync(stored, cancellationToken);

            if (applied.IsSuccess)
            {
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
                var retryAt = now.AddSeconds(options.EventRetryBackoffSeconds * (stored.Attempts + 1));
                stored.MarkFailed(applied.Error.Message, options.MaxEventAttempts, retryAt);
            }
        }
        catch (Exception exception)
        {
            var retryAt = now.AddSeconds(options.EventRetryBackoffSeconds * (stored.Attempts + 1));
            stored.MarkFailed(exception.Message, options.MaxEventAttempts, retryAt);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
