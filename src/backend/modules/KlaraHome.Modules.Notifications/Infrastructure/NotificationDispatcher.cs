using KlaraHome.Contracts.Notifications;
using KlaraHome.Modules.Notifications.Domain;
using KlaraHome.Modules.Notifications.Infrastructure.Channels;
using KlaraHome.Modules.Notifications.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Notifications.Infrastructure;

/// <summary>
/// Drains the notification queue: claims due messages, sends them, and records what happened.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as the outbox dispatcher, and for the same reasons. Rows are claimed with
/// <c>FOR UPDATE SKIP LOCKED</c>, so running more than one worker is safe by construction rather
/// than by convention.
/// </para>
/// <para>
/// It never sees a sensitive message. Those are sent inline by the notifier and their bodies are
/// never stored, so there is nothing here to re-render — which is what makes it safe for this loop
/// to run on rows that persist for ninety days.
/// </para>
/// </remarks>
internal sealed partial class NotificationDispatcher : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<NotificationOptions> _options;
    private readonly IClock _clock;
    private readonly ILogger<NotificationDispatcher> _logger;

    /// <param name="services">Resolves a scoped context and router per batch.</param>
    /// <param name="options">Poll, batch and retry settings, re-read each cycle.</param>
    /// <param name="clock">The sanctioned clock.</param>
    /// <param name="logger">Reports dispatch outcomes.</param>
    public NotificationDispatcher(
        IServiceProvider services,
        IOptionsMonitor<NotificationOptions> options,
        IClock clock,
        ILogger<NotificationDispatcher> logger)
    {
        _services = services;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CurrentValue.DispatcherEnabled)
        {
            return;
        }

        DispatcherStarted(_logger, _options.CurrentValue.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;

            try
            {
                var sent = await DrainAsync(options, stoppingToken).ConfigureAwait(false);

                // A full batch means there is probably more waiting; go round again rather than
                // idling while a backlog grows.
                if (sent >= options.BatchSize)
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
                // the worker would stop the outbox too.
                PollFailed(_logger, exception);
            }

            await Task.Delay(TimeSpan.FromSeconds(options.PollIntervalSeconds), stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The delay before attempt <paramref name="attempts"/> + 1, doubling and capped, with jitter.
    /// </summary>
    /// <remarks>
    /// The jitter matters more than the doubling. Without it, a hundred messages queued during an
    /// SMTP outage all retry in the same second when it ends, and the recovering host is hit by the
    /// entire backlog at once.
    /// </remarks>
    /// <param name="attempts">How many attempts have already been made.</param>
    /// <param name="options">Base and ceiling.</param>
    /// <param name="random">Source of jitter; injected so a test can be deterministic.</param>
    internal static TimeSpan BackoffFor(int attempts, NotificationOptions options, Random random)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(random);

        var exponent = Math.Min(attempts - 1, 16);
        var seconds = Math.Min(options.RetryBaseSeconds * Math.Pow(2, exponent), options.RetryMaxSeconds);

        // Full jitter over the top 25% of the window: never shorter than three quarters of the
        // computed delay, so the backoff still grows, and never two messages on the same tick.
        var jitter = random.NextDouble() * seconds * 0.25;

        return TimeSpan.FromSeconds((seconds * 0.75) + jitter);
    }

    private async Task<int> DrainAsync(NotificationOptions options, CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var router = scope.ServiceProvider.GetRequiredService<ChannelRouter>();

        var handled = 0;

        // The transaction spans the claim and the outcome: the rows stay locked from
        // FOR UPDATE SKIP LOCKED until their new status is committed, so no other worker can pick
        // up a message this one is midway through sending.
        await context.ExecuteInTransactionAsync(
            async (transactional, token) =>
                handled = await ClaimAndSendAsync(
                        (NotificationsDbContext)transactional,
                        router,
                        options,
                        token)
                    .ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        return handled;
    }

    private async Task<int> ClaimAndSendAsync(
        NotificationsDbContext context,
        ChannelRouter router,
        NotificationOptions options,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var due = await context.Messages
            .FromSql($"""
                SELECT * FROM notifications.notification_messages
                WHERE status = 'Queued'
                  AND next_attempt_at IS NOT NULL
                  AND next_attempt_at <= {now}
                ORDER BY next_attempt_at
                LIMIT {options.BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var random = Random.Shared;

        foreach (var message in due)
        {
            await SendAsync(context, router, message, options, random, cancellationToken).ConfigureAwait(false);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return due.Count;
    }

    private async Task SendAsync(
        NotificationsDbContext context,
        ChannelRouter router,
        NotificationMessage message,
        NotificationOptions options,
        Random random,
        CancellationToken cancellationToken)
    {
        message.BeginAttempt();

        // The body is what the notifier rendered and stored. A message with none is either
        // sensitive — which the notifier never queues — or corrupt; either way there is nothing to
        // send and nothing a retry would fix.
        if (string.IsNullOrEmpty(message.Body))
        {
            message.Failed("The message has no stored body and cannot be re-rendered.");
            return;
        }

        var providerTemplateId = await context.Templates
            .AsNoTracking()
            .Where(template => template.Id == message.TemplateId)
            .Select(template => template.ProviderTemplateId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var outbound = new OutboundMessage(
            message.Recipient,
            null,
            message.Subject,
            message.Body,
            providerTemplateId,
            message.EventKey);

        try
        {
            var outcome = await router
                .SendAsync(message.Channel, outbound, EmptyVariables, cancellationToken)
                .ConfigureAwait(false);

            if (outcome.IsSuccess)
            {
                message.Sent(_clock.UtcNow, outcome.ProviderMessageId);
                Dispatched(_logger, message.Id, message.EventKey, message.Channel);
                return;
            }

            Give(message, options, random, outcome.Error ?? "The provider refused the message.", outcome.IsPermanent);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Give(message, options, random, exception.Message, permanent: false);
        }
    }

    /// <summary>Schedules a retry, or gives up when the budget is spent or the failure is permanent.</summary>
    private void Give(
        NotificationMessage message,
        NotificationOptions options,
        Random random,
        string error,
        bool permanent)
    {
        if (permanent || message.Attempts >= options.MaxAttempts)
        {
            message.Failed(error);
            GaveUp(_logger, message.Id, message.EventKey, message.Attempts, permanent);
            return;
        }

        message.Retry(_clock.UtcNow.Add(BackoffFor(message.Attempts, options, random)), error);
    }

    /// <summary>
    /// The DLT value rules were already applied when the message was rendered, and the values are
    /// not kept, so the dispatcher has nothing further to check.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> EmptyVariables =
        new Dictionary<string, string>(StringComparer.Ordinal);

    [LoggerMessage(EventId = 1540, Level = LogLevel.Information,
        Message = "The notification dispatcher is running, polling every {NotificationPollSeconds}s")]
    private static partial void DispatcherStarted(ILogger logger, int notificationPollSeconds);

    [LoggerMessage(EventId = 1541, Level = LogLevel.Debug,
        Message = "Sent notification {NotificationMessageId} ({NotificationEventKey} on {NotificationChannel})")]
    private static partial void Dispatched(
        ILogger logger,
        Guid notificationMessageId,
        string notificationEventKey,
        NotificationChannel notificationChannel);

    [LoggerMessage(EventId = 1542, Level = LogLevel.Warning,
        Message = "Gave up on notification {NotificationMessageId} ({NotificationEventKey}) after "
                  + "{NotificationAttempts} attempt(s); permanent: {NotificationPermanent}")]
    private static partial void GaveUp(
        ILogger logger,
        Guid notificationMessageId,
        string notificationEventKey,
        int notificationAttempts,
        bool notificationPermanent);

    [LoggerMessage(EventId = 1543, Level = LogLevel.Error,
        Message = "The notification dispatcher could not poll the queue")]
    private static partial void PollFailed(ILogger logger, Exception exception);
}
