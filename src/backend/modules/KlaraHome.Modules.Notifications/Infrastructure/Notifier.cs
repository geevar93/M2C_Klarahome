using System.Text.Json;
using KlaraHome.Contracts.Notifications;
using KlaraHome.Infrastructure.Correlation;
using KlaraHome.Modules.Notifications.Domain;
using KlaraHome.Modules.Notifications.Infrastructure.Channels;
using KlaraHome.Modules.Notifications.Infrastructure.Persistence;
using KlaraHome.Modules.Notifications.Infrastructure.Templating;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Notifications.Infrastructure;

/// <summary>
/// The published entry point: turns "tell this person about this event" into rendered, recorded
/// messages.
/// </summary>
/// <remarks>
/// <para>
/// The write happens in its own scope with its own <c>DbContext</c>, the same arrangement
/// <c>IAuditLogger</c> uses: the caller's context may be carrying unsaved work that a notification
/// must not commit on its way past.
/// </para>
/// <para>
/// <b>A sensitive message is sent inline rather than queued</b>, and that is the decision that
/// retires the Step 7 OTP-in-the-log debt. A one-time code cannot be stored, so there is nothing
/// for a dispatcher to pick up later; it also must not wait for the next poll, because somebody is
/// staring at a sign-in screen. So it is rendered, sent, and recorded with its body redacted, all
/// within the request. Everything else is queued and dispatched by the worker with retry and
/// backoff (ADR-017).
/// </para>
/// </remarks>
/// <param name="scopeFactory">Creates the isolated scope the messages are written in.</param>
/// <param name="router">Decides channel availability and carries the message.</param>
/// <param name="correlationContext">Stamps the causing request's id on every message.</param>
/// <param name="options">The default locale.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what could not be sent, without ever reporting what it said.</param>
internal sealed partial class Notifier(
    IServiceScopeFactory scopeFactory,
    ChannelRouter router,
    ICorrelationContext correlationContext,
    IOptions<NotificationOptions> options,
    IClock clock,
    ILogger<Notifier> logger) : INotifier
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<QueuedNotification>> EnqueueAsync(
        NotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        var locale = string.IsNullOrWhiteSpace(request.Locale) ? options.Value.DefaultLocale : request.Locale;
        var templates = await ResolveTemplatesAsync(context, request, locale, cancellationToken).ConfigureAwait(false);

        var results = new List<QueuedNotification>();
        var pendingSends = new List<(NotificationMessage Message, NotificationTemplate Template, OutboundMessage Outbound)>();

        foreach (var channel in ChannelsFor(request, templates))
        {
            var template = templates.FirstOrDefault(candidate => candidate.Channel == channel);
            var message = NotificationMessage.Queue(
                clock.UtcNow,
                request.EventKey,
                channel,
                Address(request.Recipient, channel) ?? string.Empty);

            message.Attribute(request.Recipient.UserId, correlationContext.CorrelationId);
            context.Messages.Add(message);

            if (template is null)
            {
                message.Suppress(NotificationSuppression.NoTemplate);
                results.Add(Describe(message));
                continue;
            }

            message.RenderedFrom(template.Id, template.Version);

            var suppression = await SuppressionForAsync(context, request, template, channel, cancellationToken)
                .ConfigureAwait(false);

            if (suppression != NotificationSuppression.None)
            {
                message.Suppress(suppression);
                results.Add(Describe(message));
                continue;
            }

            var rendered = Render(template, request, message);

            if (rendered is null)
            {
                results.Add(Describe(message));
                continue;
            }

            if (template.IsSensitive)
            {
                pendingSends.Add((message, template, rendered));
            }

            results.Add(Describe(message));
        }

        // Sent before the rows are saved, so a sensitive message's outcome is written once rather
        // than as a queued row that is then updated - which would leave a window in which the
        // queue contains a message the dispatcher must never touch.
        foreach (var (message, template, outbound) in pendingSends)
        {
            await SendInlineAsync(message, template, outbound, request.Variables, cancellationToken)
                .ConfigureAwait(false);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Re-read after saving so the caller sees the state the row actually holds, including the
        // outcome of an inline send.
        return [.. results.Select(result => ResultFor(result, pendingSends))];
    }

    /// <summary>
    /// The templates that exist for this event, preferring the requested language and falling back
    /// to the store default.
    /// </summary>
    /// <remarks>
    /// A fallback rather than a failure: a deployment that has translated half its messages should
    /// send the other half in English, not send nothing.
    /// </remarks>
    private async Task<List<NotificationTemplate>> ResolveTemplatesAsync(
        NotificationsDbContext context,
        NotificationRequest request,
        string locale,
        CancellationToken cancellationToken)
    {
        var candidates = await context.Templates
            .AsNoTracking()
            .Where(template => template.EventKey == request.EventKey
                               && template.IsActive
                               && (template.Locale == locale || template.Locale == options.Value.DefaultLocale))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. candidates
            .GroupBy(template => template.Channel)
            .Select(group => group.OrderBy(template =>
                string.Equals(template.Locale, locale, StringComparison.OrdinalIgnoreCase) ? 0 : 1).First())];
    }

    /// <summary>
    /// Which channels this request produces a message on.
    /// </summary>
    /// <remarks>
    /// When the caller names no channels, the templates decide — which is what makes "also send
    /// this by SMS" a matter of adding a template rather than of changing the code that raises the
    /// event. When there are no templates at all, one email row is recorded so the delivery log can
    /// still answer why nobody was told.
    /// </remarks>
    private static IReadOnlyList<NotificationChannel> ChannelsFor(
        NotificationRequest request,
        List<NotificationTemplate> templates)
    {
        if (request.Channels is { Count: > 0 })
        {
            return [.. request.Channels.Distinct()];
        }

        return templates.Count > 0
            ? [.. templates.Select(template => template.Channel)]
            : [NotificationChannel.Email];
    }

    /// <summary>Whether anything stops this message, and what.</summary>
    private async Task<NotificationSuppression> SuppressionForAsync(
        NotificationsDbContext context,
        NotificationRequest request,
        NotificationTemplate template,
        NotificationChannel channel,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Address(request.Recipient, channel)))
        {
            return NotificationSuppression.NoRecipient;
        }

        var availability = await router.AvailabilityAsync(channel, cancellationToken).ConfigureAwait(false);

        if (availability != NotificationSuppression.None)
        {
            return availability;
        }

        // Security messages and transactional messages ignore preferences. Somebody who has turned
        // off notice of a password change has turned off the only warning they would get that
        // someone else changed it.
        if (template.Category == NotificationCategory.Security || template.IsTransactional)
        {
            return NotificationSuppression.None;
        }

        if (request.Recipient.UserId is not { } userId)
        {
            return NotificationSuppression.None;
        }

        var preference = await context.Preferences
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.UserId == userId && candidate.Category == template.Category,
                cancellationToken)
            .ConfigureAwait(false);

        var allowed = preference?.Allows(channel) ?? NotificationPreference.DefaultFor(template.Category);

        return allowed ? NotificationSuppression.None : NotificationSuppression.OptedOut;
    }

    /// <summary>
    /// Renders the template, attaches the content to the message, and returns what to send.
    /// </summary>
    /// <remarks>
    /// A missing variable fails the message rather than rendering a gap. It is a caller's bug —
    /// "Your order  has shipped" — and a failed row with the variable named in the error is what
    /// makes it findable.
    /// </remarks>
    private OutboundMessage? Render(
        NotificationTemplate template,
        NotificationRequest request,
        NotificationMessage message)
    {
        var escape = template.Channel == NotificationChannel.Email;
        var body = TemplateRenderer.Render(template.Body, request.Variables, escape);
        var subject = TemplateRenderer.Render(template.Subject, request.Variables, escapeHtml: false);

        if (!body.IsSuccess || !subject.IsSuccess)
        {
            var missing = string.Join(", ", body.MissingVariables.Concat(subject.MissingVariables).Distinct());
            message.Failed($"The template is missing values for: {missing}.");
            TemplateRenderFailed(logger, template.EventKey, template.Channel.ToString(), missing);
            return null;
        }

        message.Describe(subject.Text, body.Text!, Payload(request.Variables, template.IsSensitive), template.IsSensitive);

        return new OutboundMessage(
            message.Recipient,
            request.Recipient.Name,
            subject.Text,
            body.Text!,
            template.ProviderTemplateId,
            template.EventKey);
    }

    /// <summary>Sends a sensitive message now, because its content cannot be kept for later.</summary>
    private async Task SendInlineAsync(
        NotificationMessage message,
        NotificationTemplate template,
        OutboundMessage outbound,
        IReadOnlyDictionary<string, string> variables,
        CancellationToken cancellationToken)
    {
        message.BeginAttempt();

        try
        {
            var outcome = await router
                .SendAsync(template.Channel, outbound, variables, cancellationToken)
                .ConfigureAwait(false);

            if (outcome.IsSuccess)
            {
                message.Sent(clock.UtcNow, outcome.ProviderMessageId);
                return;
            }

            // No retry, deliberately. By the time a retry ran the person would have pressed
            // "resend" and been issued a different code, and holding the plaintext to retry with is
            // exactly what this design refuses to do.
            message.Failed(outcome.Error ?? "The provider refused the message.");
            InlineSendFailed(logger, template.EventKey, template.Channel.ToString(), outcome.Error ?? "unknown");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            message.Failed(exception.Message);
            InlineSendFailed(logger, template.EventKey, template.Channel.ToString(), exception.GetType().Name);
        }
    }

    /// <summary>
    /// The variables, as stored.
    /// </summary>
    /// <remarks>
    /// For a sensitive template the names are kept and every value is replaced. Knowing which
    /// variables a message carried is diagnostic; knowing that the code was 493028 is a breach.
    /// </remarks>
    private static string Payload(IReadOnlyDictionary<string, string> variables, bool sensitive)
    {
        var stored = sensitive
            ? variables.ToDictionary(entry => entry.Key, _ => "[redacted]", StringComparer.Ordinal)
            : variables.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

        return JsonSerializer.Serialize(stored, NotificationJson.Options);
    }

    private static string? Address(NotificationRecipient recipient, NotificationChannel channel) => channel switch
    {
        NotificationChannel.Email => recipient.Email,
        NotificationChannel.Sms or NotificationChannel.WhatsApp => recipient.Mobile,
        NotificationChannel.InApp => recipient.UserId?.ToString(),
        _ => null,
    };

    private static QueuedNotification Describe(NotificationMessage message)
        => new(message.Id, message.Channel, message.Status, message.Suppression);

    /// <summary>Re-reads the final state of a message that was sent inline after it was described.</summary>
    private static QueuedNotification ResultFor(
        QueuedNotification result,
        List<(NotificationMessage Message, NotificationTemplate Template, OutboundMessage Outbound)> sent)
    {
        var match = sent.FirstOrDefault(entry => entry.Message.Id == result.MessageId).Message;

        return match is null
            ? result
            : new QueuedNotification(match.Id, match.Channel, match.Status, match.Suppression);
    }

    [LoggerMessage(EventId = 1530, Level = LogLevel.Error,
        Message = "The {NotificationEventKey} template for {NotificationChannel} was not given values for: "
                  + "{NotificationMissingVariables}. The message was not sent.")]
    private static partial void TemplateRenderFailed(
        ILogger logger,
        string notificationEventKey,
        string notificationChannel,
        string notificationMissingVariables);

    [LoggerMessage(EventId = 1531, Level = LogLevel.Warning,
        Message = "A {NotificationEventKey} message could not be delivered on {NotificationChannel}: "
                  + "{NotificationError}")]
    private static partial void InlineSendFailed(
        ILogger logger,
        string notificationEventKey,
        string notificationChannel,
        string notificationError);
}

/// <summary>Serialisation for the <c>jsonb</c> columns in this schema.</summary>
internal static class NotificationJson
{
    /// <summary>camelCase, matching the API payloads these documents are shown in.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };
}
