using System.Globalization;
using FluentValidation;
using KlaraHome.Contracts.Notifications;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Notifications.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Notifications.Application;

/// <summary>One entry in the delivery log.</summary>
/// <param name="Id">The message id.</param>
/// <param name="EventKey">The event that produced it.</param>
/// <param name="Channel">The channel it was queued on.</param>
/// <param name="Recipient">The address, masked.</param>
/// <param name="Subject">The rendered subject; null for a sensitive message.</param>
/// <param name="Status">Where it got to.</param>
/// <param name="Suppression">Why nobody was asked, when nobody was.</param>
/// <param name="Attempts">How many times delivery was attempted.</param>
/// <param name="Error">The last failure.</param>
/// <param name="CreatedAt">When it was queued.</param>
/// <param name="SentAt">When a provider accepted it.</param>
/// <param name="NextAttemptAt">When the dispatcher will try again.</param>
internal sealed record NotificationLogResponse(
    Guid Id,
    string EventKey,
    string Channel,
    string Recipient,
    string? Subject,
    string Status,
    string? Suppression,
    int Attempts,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? NextAttemptAt);

/// <summary>Searches the delivery log, newest first.</summary>
/// <param name="Status">Restrict to one status, or null.</param>
/// <param name="Channel">Restrict to one channel, or null.</param>
/// <param name="EventKey">Restrict to one event, or null.</param>
/// <param name="From">Earliest queued time.</param>
/// <param name="To">Latest queued time.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record SearchNotificationsQuery(
    string? Status,
    string? Channel,
    string? EventKey,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Cursor,
    int? Size) : IQuery<PagedResult<NotificationLogResponse>>;

/// <summary>Reads one entry.</summary>
/// <param name="MessageId">The message id.</param>
internal sealed record GetNotificationQuery(Guid MessageId) : IQuery<NotificationLogResponse>;

/// <summary>Puts a failed or suppressed message back in the queue.</summary>
/// <param name="MessageId">The message id.</param>
internal sealed record RetryNotificationCommand(Guid MessageId) : ICommand<NotificationLogResponse>;

/// <summary>Sends a test message, to prove a channel works end to end.</summary>
/// <param name="Channel">Which channel to test.</param>
/// <param name="To">Where to send it.</param>
internal sealed record SendTestNotificationCommand(string Channel, string To) : ICommand<IReadOnlyList<QueuedNotification>>;

/// <summary>Rules for a test send.</summary>
internal sealed class SendTestNotificationCommandValidator : AbstractValidator<SendTestNotificationCommand>
{
    public SendTestNotificationCommandValidator()
    {
        RuleFor(command => command.Channel)
            .Must(channel => Enum.TryParse<NotificationChannel>(channel, ignoreCase: true, out _))
            .WithMessage("Unknown channel.");

        RuleFor(command => command.To)
            .NotEmpty()
            .MaximumLength(320);
    }
}

/// <param name="context">The Notifications data context.</param>
internal sealed class SearchNotificationsQueryHandler(NotificationsDbContext context)
    : IQueryHandler<SearchNotificationsQuery, PagedResult<NotificationLogResponse>>
{
    public async Task<Result<PagedResult<NotificationLogResponse>>> HandleAsync(
        SearchNotificationsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);
        var messages = context.Messages.AsNoTracking();

        if (Enum.TryParse<NotificationStatus>(query.Status, ignoreCase: true, out var status))
        {
            messages = messages.Where(message => message.Status == status);
        }

        if (Enum.TryParse<NotificationChannel>(query.Channel, ignoreCase: true, out var channel))
        {
            messages = messages.Where(message => message.Channel == channel);
        }

        if (!string.IsNullOrWhiteSpace(query.EventKey))
        {
            messages = messages.Where(message => message.EventKey == query.EventKey);
        }

        if (query.From is { } from)
        {
            messages = messages.Where(message => message.CreatedAt >= from);
        }

        if (query.To is { } to)
        {
            messages = messages.Where(message => message.CreatedAt <= to);
        }

        // Keyset on (created_at, id). The timestamp alone is not unique and is the partition key,
        // so paging on it also keeps the planner pruning partitions.
        if (Cursor.TryDecode(query.Cursor, out var key)
            && key.Split('|') is [var timestamp, var id]
            && DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, out var after)
            && Guid.TryParse(id, out var afterId))
        {
            messages = messages.Where(message =>
                message.CreatedAt < after || (message.CreatedAt == after && message.Id.CompareTo(afterId) < 0));
        }

        var page = await messages
            .OrderByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var items = hasMore ? page[..size] : page;

        var next = hasMore
            ? Cursor.Encode(string.Create(
                CultureInfo.InvariantCulture,
                $"{items[^1].CreatedAt:O}|{items[^1].Id}"))
            : null;

        IReadOnlyList<NotificationLogResponse> responses = [.. items.Select(NotificationLogProjection.ToResponse)];

        return Result.Success(new PagedResult<NotificationLogResponse>(responses, new PageInfo(size, next)));
    }
}

/// <param name="context">The Notifications data context.</param>
internal sealed class GetNotificationQueryHandler(NotificationsDbContext context)
    : IQueryHandler<GetNotificationQuery, NotificationLogResponse>
{
    public async Task<Result<NotificationLogResponse>> HandleAsync(
        GetNotificationQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var message = await context.Messages
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == query.MessageId, cancellationToken)
            .ConfigureAwait(false);

        return message is null
            ? Result.Failure<NotificationLogResponse>(NotificationErrors.MessageNotFound)
            : Result.Success(NotificationLogProjection.ToResponse(message));
    }
}

/// <param name="context">The Notifications data context.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class RetryNotificationCommandHandler(NotificationsDbContext context, IClock clock)
    : ICommandHandler<RetryNotificationCommand, NotificationLogResponse>
{
    public async Task<Result<NotificationLogResponse>> HandleAsync(
        RetryNotificationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var message = await context.Messages
            .FirstOrDefaultAsync(candidate => candidate.Id == command.MessageId, cancellationToken)
            .ConfigureAwait(false);

        if (message is null)
        {
            return Result.Failure<NotificationLogResponse>(NotificationErrors.MessageNotFound);
        }

        // Only a terminal, non-delivered message. Re-queueing one that is already queued would give
        // the dispatcher two claims on the same row, and re-queueing a sent one would send it twice.
        if (message.Status is not (NotificationStatus.Failed or NotificationStatus.Suppressed
            or NotificationStatus.Bounced))
        {
            return Result.Failure<NotificationLogResponse>(NotificationErrors.NotRetryable);
        }

        // A sensitive message has no stored body, so there is nothing to re-send: the person asks
        // for a new code instead.
        if (string.IsNullOrEmpty(message.Body))
        {
            return Result.Failure<NotificationLogResponse>(NotificationErrors.NotRetryable);
        }

        message.Requeue(clock.UtcNow);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(NotificationLogProjection.ToResponse(message));
    }
}

/// <summary>Maps a message onto its log response.</summary>
internal static class NotificationLogProjection
{
    /// <summary>Builds the admin-facing response, with the recipient masked.</summary>
    /// <param name="message">The message.</param>
    public static NotificationLogResponse ToResponse(Domain.NotificationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new NotificationLogResponse(
            message.Id,
            message.EventKey,
            message.Channel.ToString(),
            Mask(message.Recipient),
            message.Subject,
            message.Status.ToString(),
            message.Suppression == NotificationSuppression.None ? null : message.Suppression.ToString(),
            message.Attempts,
            message.Error,
            message.CreatedAt,
            message.SentAt,
            message.NextAttemptAt);
    }

    /// <summary>
    /// Masks the recipient for the admin list.
    /// </summary>
    /// <remarks>
    /// <c>07-security-compliance.md</c> §5 requires personal data to be masked in admin lists. Enough
    /// is left for an operator to match an entry against a support conversation — the domain of an
    /// address, the last four digits of a number — without the delivery log becoming an exportable
    /// contact database.
    /// </remarks>
    internal static string Mask(string recipient)
    {
        if (string.IsNullOrEmpty(recipient))
        {
            return string.Empty;
        }

        var at = recipient.IndexOf('@', StringComparison.Ordinal);

        if (at > 0)
        {
            var local = recipient[..at];
            var shown = local.Length <= 2 ? local[..1] : local[..2];
            return $"{shown}{new string('*', Math.Max(1, local.Length - shown.Length))}{recipient[at..]}";
        }

        return recipient.Length <= 4
            ? new string('*', recipient.Length)
            : new string('*', recipient.Length - 4) + recipient[^4..];
    }
}

/// <summary>
/// Sends the built-in test message, which is how an operator finds out whether a channel works
/// without waiting for a real order.
/// </summary>
/// <remarks>
/// It goes through the ordinary path — template, preferences, router, delivery log — rather than
/// calling a sender directly. A test that bypassed the pipeline would prove the SMTP credentials
/// and nothing else, and the failures worth catching are further up: a missing template, a flag
/// somebody left off, a DLT id nobody registered.
/// </remarks>
/// <param name="notifier">The published entry point.</param>
/// <param name="settings">Supplies the store name the template asks for.</param>
/// <param name="clock">Stamps the message so two tests are distinguishable.</param>
internal sealed class SendTestNotificationCommandHandler(
    INotifier notifier,
    KlaraHome.Contracts.Platform.IStoreSettings settings,
    IClock clock) : ICommandHandler<SendTestNotificationCommand, IReadOnlyList<QueuedNotification>>
{
    public async Task<Result<IReadOnlyList<QueuedNotification>>> HandleAsync(
        SendTestNotificationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var channel = Enum.Parse<NotificationChannel>(command.Channel, ignoreCase: true);
        var branding = await settings
            .GetAsync<KlaraHome.Contracts.Platform.BrandingSettings>(cancellationToken)
            .ConfigureAwait(false);

        var recipient = channel == NotificationChannel.Email
            ? new NotificationRecipient(Email: command.To)
            : new NotificationRecipient(Mobile: command.To);

        var result = await notifier
            .EnqueueAsync(
                new NotificationRequest(
                    NotificationEvents.ChannelTest,
                    recipient,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["storeName"] = branding.StoreName,
                        ["sentAt"] = clock.UtcNow.ToString("u", CultureInfo.InvariantCulture),
                    },
                    [channel]),
                cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(result);
    }
}
