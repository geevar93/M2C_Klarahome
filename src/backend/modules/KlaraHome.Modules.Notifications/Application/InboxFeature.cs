using System.Globalization;
using KlaraHome.Contracts.Notifications;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Notifications.Domain;
using KlaraHome.Modules.Notifications.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Notifications.Application;

/// <summary>One message in a shopper's in-app inbox.</summary>
/// <param name="Id">The message id.</param>
/// <param name="EventKey">The event that produced it, which the storefront turns into a link.</param>
/// <param name="Category">The preference bucket it belongs to, for grouping and for an icon.</param>
/// <param name="Subject">The rendered subject.</param>
/// <param name="Body">The rendered body.</param>
/// <param name="CreatedAt">When it was raised.</param>
/// <param name="ReadAt">When it was read, or null.</param>
internal sealed record InboxMessageResponse(
    Guid Id,
    string EventKey,
    string? Category,
    string? Subject,
    string? Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

/// <summary>A page of the inbox, and what the badge should say.</summary>
/// <param name="Items">The messages, newest first.</param>
/// <param name="Page">The paging token.</param>
/// <param name="UnreadCount">How many are unread in total, not on this page.</param>
internal sealed record InboxResponse(
    IReadOnlyList<InboxMessageResponse> Items,
    PageInfo Page,
    int UnreadCount);

/// <summary>How many unread messages the caller has.</summary>
/// <param name="UnreadCount">The count.</param>
internal sealed record UnreadCountResponse(int UnreadCount);

/// <summary>How many messages a bulk mark-read actually changed.</summary>
/// <param name="MarkedCount">The number of messages that moved from unread to read.</param>
internal sealed record MarkAllReadResponse(int MarkedCount);

/// <summary>Reads the caller's in-app messages, newest first.</summary>
/// <param name="UnreadOnly">Whether to return only what has not been read.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record GetInboxQuery(bool UnreadOnly, string? Cursor, int? Size) : IQuery<InboxResponse>;

/// <summary>Counts the caller's unread messages.</summary>
internal sealed record GetUnreadCountQuery : IQuery<UnreadCountResponse>;

/// <summary>Marks one of the caller's messages read.</summary>
/// <param name="MessageId">The message.</param>
internal sealed record MarkNotificationReadCommand(Guid MessageId) : ICommand<InboxMessageResponse>;

/// <summary>Marks everything the caller has not read as read.</summary>
internal sealed record MarkAllNotificationsReadCommand : ICommand<MarkAllReadResponse>;

/// <summary>
/// The one definition of "a message this person can see in the application".
/// </summary>
/// <remarks>
/// <para>
/// Four things at once, and every one of them matters. The channel, because an inbox that returned
/// email rows would hand a shopper a second copy of everything already in their mail. The account,
/// because the delivery log is one table for every customer on the store. The status, because a
/// message the dispatcher has not carried yet is not something anybody has been told — and a
/// suppressed one is a record of something we decided <em>not</em> to say. And a stored body,
/// because a sensitive message keeps none and there would be nothing to show.
/// </para>
/// <para>
/// Written once, as an expression, so the list, the count and the mark-read cannot disagree about
/// it. Three places each carrying their own version of this predicate is how a badge ends up
/// counting messages the list does not show.
/// </para>
/// </remarks>
internal static class InboxScope
{
    /// <summary>The caller's visible in-app messages.</summary>
    /// <param name="context">The Notifications data context.</param>
    /// <param name="userId">Whose inbox.</param>
    public static IQueryable<NotificationMessage> For(NotificationsDbContext context, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Messages.Where(message =>
            message.Channel == NotificationChannel.InApp
            && message.UserId == userId
            && (message.Status == NotificationStatus.Sent || message.Status == NotificationStatus.Delivered)
            && message.Body != null);
    }
}

/// <param name="context">The Notifications data context.</param>
/// <param name="userContext">Whose inbox to read.</param>
internal sealed class GetInboxQueryHandler(NotificationsDbContext context, IUserContext userContext)
    : IQueryHandler<GetInboxQuery, InboxResponse>
{
    public async Task<Result<InboxResponse>> HandleAsync(GetInboxQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (userContext.UserId is not { } userId)
        {
            return Result.Failure<InboxResponse>(Error.Unauthorized());
        }

        var size = Cursor.NormalizeSize(query.Size);
        var scope = InboxScope.For(context, userId).AsNoTracking();

        // Counted over the whole scope rather than the filtered page: the badge is "how many have
        // you not read", and on the unread-only tab that has to keep agreeing with the list the
        // shopper is looking at.
        var unreadCount = await scope
            .CountAsync(message => message.ReadAt == null, cancellationToken)
            .ConfigureAwait(false);

        var messages = query.UnreadOnly ? scope.Where(message => message.ReadAt == null) : scope;

        // Keyset on (created_at, id), the same shape the admin log uses and for the same reason:
        // the timestamp is the partition key, so paging on it keeps the planner pruning partitions
        // instead of opening two years of them to count an offset.
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

        IReadOnlyList<InboxMessageResponse> responses = [.. items.Select(InboxProjection.ToResponse)];

        return Result.Success(new InboxResponse(responses, new PageInfo(size, next), unreadCount));
    }
}

/// <param name="context">The Notifications data context.</param>
/// <param name="userContext">Whose inbox to count.</param>
internal sealed class GetUnreadCountQueryHandler(NotificationsDbContext context, IUserContext userContext)
    : IQueryHandler<GetUnreadCountQuery, UnreadCountResponse>
{
    public async Task<Result<UnreadCountResponse>> HandleAsync(
        GetUnreadCountQuery query,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Result.Failure<UnreadCountResponse>(Error.Unauthorized());
        }

        var count = await InboxScope.For(context, userId)
            .AsNoTracking()
            .CountAsync(message => message.ReadAt == null, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new UnreadCountResponse(count));
    }
}

/// <param name="context">The Notifications data context.</param>
/// <param name="userContext">Whose message it must be.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class MarkNotificationReadCommandHandler(
    NotificationsDbContext context,
    IUserContext userContext,
    IClock clock) : ICommandHandler<MarkNotificationReadCommand, InboxMessageResponse>
{
    public async Task<Result<InboxMessageResponse>> HandleAsync(
        MarkNotificationReadCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (userContext.UserId is not { } userId)
        {
            return Result.Failure<InboxMessageResponse>(Error.Unauthorized());
        }

        // Scoped to the caller before the id is applied, so somebody else's message id is a 404
        // rather than a 403. A "that is not yours" tells the asker the message exists.
        var message = await InboxScope.For(context, userId)
            .FirstOrDefaultAsync(candidate => candidate.Id == command.MessageId, cancellationToken)
            .ConfigureAwait(false);

        if (message is null)
        {
            return Result.Failure<InboxMessageResponse>(NotificationErrors.MessageNotFound);
        }

        message.MarkRead(clock.UtcNow);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(InboxProjection.ToResponse(message));
    }
}

/// <param name="context">The Notifications data context.</param>
/// <param name="userContext">Whose inbox to clear.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class MarkAllNotificationsReadCommandHandler(
    NotificationsDbContext context,
    IUserContext userContext,
    IClock clock) : ICommandHandler<MarkAllNotificationsReadCommand, MarkAllReadResponse>
{
    /// <summary>
    /// Marks the caller's unread messages read.
    /// </summary>
    /// <remarks>
    /// A set-based update rather than loading the rows: a shopper who has ignored their inbox for a
    /// year is one statement either way, and materialising an unbounded number of aggregates to set
    /// one timestamp on each would make the cost of this button depend on how long they waited to
    /// press it. The unread predicate is part of the statement, so the already-read keep the
    /// timestamp they had.
    /// </remarks>
    public async Task<Result<MarkAllReadResponse>> HandleAsync(
        MarkAllNotificationsReadCommand command,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Result.Failure<MarkAllReadResponse>(Error.Unauthorized());
        }

        var now = clock.UtcNow;

        var marked = await InboxScope.For(context, userId)
            .Where(message => message.ReadAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(message => message.ReadAt, now),
                cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new MarkAllReadResponse(marked));
    }
}

/// <summary>Maps a message onto its inbox response.</summary>
internal static class InboxProjection
{
    /// <summary>Builds the customer-facing response.</summary>
    /// <param name="message">The message.</param>
    public static InboxMessageResponse ToResponse(NotificationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new InboxMessageResponse(
            message.Id,
            message.EventKey,
            CategoryFor(message.EventKey),
            message.Subject,
            message.Body,
            message.CreatedAt,
            message.ReadAt);
    }

    /// <summary>
    /// The category a message belongs to, derived from its event key.
    /// </summary>
    /// <remarks>
    /// Read from the shipped template catalogue rather than joined from the template row, which
    /// would be a second query per message for a value that does not vary. A key an operator has
    /// added a template for and this catalogue does not know returns null, and the storefront falls
    /// back to a neutral icon rather than showing nothing.
    /// </remarks>
    /// <param name="eventKey">The event key.</param>
    internal static string? CategoryFor(string eventKey)
        => Infrastructure.Seeding.DefaultTemplates.All
            .FirstOrDefault(template => string.Equals(template.EventKey, eventKey, StringComparison.Ordinal))?
            .Category
            .ToString();
}
