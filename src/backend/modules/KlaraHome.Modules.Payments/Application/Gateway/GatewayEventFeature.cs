using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Payments.Application.Payments;
using KlaraHome.Modules.Payments.Domain;
using KlaraHome.Modules.Payments.Infrastructure;
using KlaraHome.Modules.Payments.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Payments.Application.Gateway;

/// <summary>Lists stored webhooks — the log and the dead-letter queue, one list.</summary>
/// <param name="Status">Filter by where processing stands. <c>DeadLettered</c> is the queue.</param>
/// <param name="Type">Filter by event type.</param>
/// <param name="PaymentId">Filter to one collection.</param>
/// <param name="From">Only events received on or after this instant.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListGatewayEventsQuery(
    string? Status,
    string? Type,
    Guid? PaymentId,
    DateTimeOffset? From,
    string? Cursor,
    int? Size) : IQuery<PagedResult<GatewayEventSummaryResponse>>;

/// <summary>Reads one stored webhook, with the body exactly as it arrived.</summary>
/// <param name="EventId">The stored event.</param>
internal sealed record GetGatewayEventQuery(Guid EventId) : IQuery<GatewayEventResponse>;

/// <summary>
/// Puts a failed or dead-lettered event back in the queue.
/// </summary>
/// <remarks>
/// The action an operator takes after reading a payload and fixing whatever made it unprocessable.
/// It resets the attempt budget, because a replay is a deliberate decision rather than another
/// automatic try — but it does not re-verify the signature, and an event whose signature never
/// verified stays unprocessable however many times it is replayed.
/// </remarks>
/// <param name="EventId">The stored event.</param>
internal sealed record ReplayGatewayEventCommand(Guid EventId) : ICommand<GatewayEventSummaryResponse>;

/// <summary>Lists stored webhooks, newest first.</summary>
/// <param name="context">The Payments data context.</param>
/// <param name="options">Supplies the page ceiling.</param>
internal sealed class ListGatewayEventsQueryHandler(
    PaymentsDbContext context,
    IOptions<PaymentsOptions> options)
    : IQueryHandler<ListGatewayEventsQuery, PagedResult<GatewayEventSummaryResponse>>
{
    public async Task<Result<PagedResult<GatewayEventSummaryResponse>>> HandleAsync(
        ListGatewayEventsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Math.Min(Cursor.NormalizeSize(query.Size), options.Value.MaxPageSize);
        var rows = context.GatewayEvents.AsNoTracking().AsQueryable();

        if (Enum.TryParse<GatewayEventStatus>(query.Status, ignoreCase: true, out var status))
        {
            rows = rows.Where(entry => entry.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            var type = query.Type.Trim();
            rows = rows.Where(entry => entry.EventType == type);
        }

        if (query.PaymentId is { } paymentId)
        {
            rows = rows.Where(entry => entry.PaymentId == paymentId);
        }

        if (query.From is { } from)
        {
            rows = rows.Where(entry => entry.ReceivedAt >= from);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(entry => entry.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(entry => entry.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var items = page.Take(size).Select(PaymentProjection.ToEvent).ToArray();
        var next = hasMore && items.Length > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<GatewayEventSummaryResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>
/// Reads one stored webhook with its payload.
/// </summary>
/// <remarks>
/// The payload is returned verbatim because it is the evidence, and it is behind a permission for the
/// same reason: it carries whatever the gateway chose to put in it about a customer's payment, and it
/// is not something every operator needs to read.
/// </remarks>
/// <param name="context">The Payments data context.</param>
internal sealed class GetGatewayEventQueryHandler(PaymentsDbContext context)
    : IQueryHandler<GetGatewayEventQuery, GatewayEventResponse>
{
    public async Task<Result<GatewayEventResponse>> HandleAsync(
        GetGatewayEventQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var stored = await context.GatewayEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Id == query.EventId, cancellationToken)
            .ConfigureAwait(false);

        return stored is null
            ? Result.Failure<GatewayEventResponse>(PaymentsErrors.NotFound("gateway event"))
            : Result.Success(new GatewayEventResponse(PaymentProjection.ToEvent(stored), stored.Payload));
    }
}

/// <summary>Re-queues a failed or dead-lettered event.</summary>
/// <param name="context">The Payments data context.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class ReplayGatewayEventCommandHandler(PaymentsDbContext context, IClock clock)
    : ICommandHandler<ReplayGatewayEventCommand, GatewayEventSummaryResponse>
{
    public async Task<Result<GatewayEventSummaryResponse>> HandleAsync(
        ReplayGatewayEventCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var stored = await context.GatewayEvents
            .FirstOrDefaultAsync(entry => entry.Id == command.EventId, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            return Result.Failure<GatewayEventSummaryResponse>(PaymentsErrors.NotFound("gateway event"));
        }

        if (!stored.Replay(clock.UtcNow))
        {
            return Result.Failure<GatewayEventSummaryResponse>(PaymentsErrors.EventNotReplayable);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(PaymentProjection.ToEvent(stored));
    }
}
