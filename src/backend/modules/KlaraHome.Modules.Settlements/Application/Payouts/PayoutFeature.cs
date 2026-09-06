using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Settlements.Domain;
using KlaraHome.Modules.Settlements.Infrastructure;
using KlaraHome.Modules.Settlements.Infrastructure.Payouts;
using KlaraHome.Modules.Settlements.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Settlements.Application.Payouts;

/// <summary>The payout runs, newest first.</summary>
/// <param name="Status">Filter to one state.</param>
/// <param name="From">Only runs raised on or after this instant.</param>
/// <param name="To">Only runs raised strictly before this instant.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListPayoutBatchesQuery(
    string? Status,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Cursor,
    int? Size) : IQuery<PagedResult<PayoutBatchResponse>>;

/// <summary>One payout run in full, with its transfers.</summary>
/// <param name="BatchId">The batch.</param>
internal sealed record GetPayoutBatchQuery(Guid BatchId) : IQuery<PayoutBatchResponse>;

/// <summary>Builds a draft payout run from closed settlement periods.</summary>
/// <param name="CycleIds">Which periods. Empty means every closed, unbatched, payable one.</param>
internal sealed record CreatePayoutBatchCommand(IReadOnlyList<Guid>? CycleIds) : ICommand<PayoutBatchResponse>;

/// <summary>Signs a payout run off.</summary>
/// <param name="BatchId">The batch.</param>
internal sealed record ApprovePayoutBatchCommand(Guid BatchId) : ICommand<PayoutBatchResponse>;

/// <summary>Hands an approved run to the gateway.</summary>
/// <param name="BatchId">The batch.</param>
internal sealed record ProcessPayoutBatchCommand(Guid BatchId) : ICommand<PayoutBatchResponse>;

/// <summary>Abandons a run nothing has left.</summary>
/// <param name="BatchId">The batch.</param>
/// <param name="Reason">Why.</param>
internal sealed record CancelPayoutBatchCommand(Guid BatchId, string? Reason) : ICommand<PayoutBatchResponse>;

/// <summary>
/// Lists payout runs.
/// </summary>
/// <remarks>
/// A batch spans sellers, so it carries no vendor scope of its own and the query filter cannot
/// confine a seller to theirs. The confinement is explicit instead: a vendor caller sees only the
/// runs that contain a transfer to them, and the transfers inside those runs are filtered by the data
/// layer as usual — so a seller can see the line that paid them without seeing what anybody else was
/// paid.
/// </remarks>
/// <param name="context">The Settlements data context.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="options">Supplies the page ceiling.</param>
internal sealed class ListPayoutBatchesQueryHandler(
    SettlementsDbContext context,
    SettlementsScope scope,
    IOptions<SettlementsOptions> options)
    : IQueryHandler<ListPayoutBatchesQuery, PagedResult<PayoutBatchResponse>>
{
    public async Task<Result<PagedResult<PayoutBatchResponse>>> HandleAsync(
        ListPayoutBatchesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Math.Min(Cursor.NormalizeSize(query.Size), options.Value.MaxPageSize);
        var rows = context.PayoutBatches.AsNoTracking().AsQueryable();

        if (scope.VendorId is { } vendorId)
        {
            rows = rows.Where(batch => context.PayoutItems
                .Any(item => item.PayoutBatchId == batch.Id && item.VendorId == vendorId));
        }

        if (Enum.TryParse<PayoutBatchStatus>(query.Status, ignoreCase: true, out var status))
        {
            rows = rows.Where(batch => batch.Status == status);
        }

        if (query.From is { } from)
        {
            rows = rows.Where(batch => batch.RequestedAt >= from);
        }

        if (query.To is { } to)
        {
            rows = rows.Where(batch => batch.RequestedAt < to);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(batch => batch.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(batch => batch.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var actor = scope.Actor;
        var items = page.Take(size).Select(batch => SettlementProjection.ToBatch(batch, actor)).ToArray();
        var next = hasMore && items.Length > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<PayoutBatchResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>Reads one payout run.</summary>
/// <param name="context">The Settlements data context.</param>
/// <param name="scope">Who is asking; decides which transitions the response offers.</param>
internal sealed class GetPayoutBatchQueryHandler(SettlementsDbContext context, SettlementsScope scope)
    : IQueryHandler<GetPayoutBatchQuery, PayoutBatchResponse>
{
    public async Task<Result<PayoutBatchResponse>> HandleAsync(
        GetPayoutBatchQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var batch = await context.PayoutBatches
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == query.BatchId, cancellationToken)
            .ConfigureAwait(false);

        if (batch is null)
        {
            return Result.Failure<PayoutBatchResponse>(SettlementsErrors.NotFound("payout batch"));
        }

        // A seller may read a batch only if it paid them. The items inside it are already confined by
        // the vendor query filter, so an empty item list here means the batch is not theirs.
        if (scope.IsVendor && batch.Items.Count == 0)
        {
            return Result.Failure<PayoutBatchResponse>(SettlementsErrors.NotFound("payout batch"));
        }

        return Result.Success(SettlementProjection.ToBatch(batch, scope.Actor));
    }
}

/// <summary>Builds a draft run.</summary>
/// <param name="context">The Settlements data context.</param>
/// <param name="workflow">The one place a batch is built.</param>
/// <param name="scope">Who is asking.</param>
internal sealed class CreatePayoutBatchCommandHandler(
    SettlementsDbContext context,
    PayoutWorkflow workflow,
    SettlementsScope scope)
    : ICommandHandler<CreatePayoutBatchCommand, PayoutBatchResponse>
{
    public async Task<Result<PayoutBatchResponse>> HandleAsync(
        CreatePayoutBatchCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (scope.IsVendor)
        {
            return Result.Failure<PayoutBatchResponse>(SettlementsErrors.VendorForbidden);
        }

        var built = await workflow
            .BuildAsync(command.CycleIds ?? [], scope.ActorId, cancellationToken)
            .ConfigureAwait(false);

        if (built.IsFailure)
        {
            return Result.Failure<PayoutBatchResponse>(built.Error);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(SettlementProjection.ToBatch(built.Value, scope.Actor));
    }
}

/// <summary>
/// Signs a run off.
/// </summary>
/// <remarks>
/// The checker's half of the control. The workflow refuses a self-approval, the aggregate refuses it
/// again and a database constraint refuses it a third time; this handler's job is to make sure the
/// caller reaches those checks with the right identity, and to turn their refusal into a sentence
/// somebody can act on.
/// </remarks>
/// <param name="context">The Settlements data context.</param>
/// <param name="workflow">The one place a batch is signed off.</param>
/// <param name="scope">Who is asking.</param>
internal sealed class ApprovePayoutBatchCommandHandler(
    SettlementsDbContext context,
    PayoutWorkflow workflow,
    SettlementsScope scope)
    : ICommandHandler<ApprovePayoutBatchCommand, PayoutBatchResponse>
{
    public async Task<Result<PayoutBatchResponse>> HandleAsync(
        ApprovePayoutBatchCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (scope.IsVendor)
        {
            return Result.Failure<PayoutBatchResponse>(SettlementsErrors.VendorForbidden);
        }

        var batch = await LoadAsync(context, command.BatchId, cancellationToken).ConfigureAwait(false);

        if (batch is null)
        {
            return Result.Failure<PayoutBatchResponse>(SettlementsErrors.NotFound("payout batch"));
        }

        var approved = workflow.Approve(batch, scope.Actor, scope.ActorId);

        if (approved.IsFailure)
        {
            return Result.Failure<PayoutBatchResponse>(approved.Error);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(SettlementProjection.ToBatch(batch, scope.Actor));
    }

    /// <summary>Loads a batch tracked and ready to be moved.</summary>
    internal static Task<PayoutBatch?> LoadAsync(
        SettlementsDbContext context,
        Guid batchId,
        CancellationToken cancellationToken)
        => context.PayoutBatches
            .IgnoreQueryFilters()
            .Include(batch => batch.Items)
            .FirstOrDefaultAsync(
                batch => batch.TenantId == context.TenantId && batch.Id == batchId,
                cancellationToken);
}

/// <summary>
/// Hands an approved run to the gateway.
/// </summary>
/// <remarks>
/// The pass is bounded and resumable, so a large batch is sent over several calls and this endpoint
/// answers with the batch as it stands. That is deliberate: a request that waited for four hundred
/// bank transfers would time out, and a timeout on the request that sends money is the worst place in
/// the system to have one.
/// </remarks>
/// <param name="context">The Settlements data context.</param>
/// <param name="workflow">The one place a batch is sent.</param>
/// <param name="scope">Who is asking.</param>
internal sealed class ProcessPayoutBatchCommandHandler(
    SettlementsDbContext context,
    PayoutWorkflow workflow,
    SettlementsScope scope)
    : ICommandHandler<ProcessPayoutBatchCommand, PayoutBatchResponse>
{
    public async Task<Result<PayoutBatchResponse>> HandleAsync(
        ProcessPayoutBatchCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (scope.IsVendor)
        {
            return Result.Failure<PayoutBatchResponse>(SettlementsErrors.VendorForbidden);
        }

        var batch = await ApprovePayoutBatchCommandHandler
            .LoadAsync(context, command.BatchId, cancellationToken)
            .ConfigureAwait(false);

        if (batch is null)
        {
            return Result.Failure<PayoutBatchResponse>(SettlementsErrors.NotFound("payout batch"));
        }

        var processed = await workflow.ProcessAsync(batch, scope.Actor, cancellationToken).ConfigureAwait(false);

        return processed.IsFailure
            ? Result.Failure<PayoutBatchResponse>(processed.Error)
            : Result.Success(SettlementProjection.ToBatch(batch, scope.Actor));
    }
}

/// <summary>
/// Abandons a run nothing has left.
/// </summary>
/// <remarks>
/// The cycles in it become payable again, which is the whole reason cancelling is safe: nothing is
/// lost and a new batch can be built. A batch that has been sent cannot be cancelled at all — the
/// transition table has no such edge, because money that has left is not withdrawn by a button.
/// </remarks>
/// <param name="context">The Settlements data context.</param>
/// <param name="scope">Who is asking.</param>
internal sealed class CancelPayoutBatchCommandHandler(SettlementsDbContext context, SettlementsScope scope)
    : ICommandHandler<CancelPayoutBatchCommand, PayoutBatchResponse>
{
    public async Task<Result<PayoutBatchResponse>> HandleAsync(
        CancelPayoutBatchCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (scope.IsVendor)
        {
            return Result.Failure<PayoutBatchResponse>(SettlementsErrors.VendorForbidden);
        }

        var batch = await ApprovePayoutBatchCommandHandler
            .LoadAsync(context, command.BatchId, cancellationToken)
            .ConfigureAwait(false);

        if (batch is null)
        {
            return Result.Failure<PayoutBatchResponse>(SettlementsErrors.NotFound("payout batch"));
        }

        if (!PayoutLifecycle.IsAllowed(batch.Status, PayoutBatchStatus.Cancelled, scope.Actor))
        {
            return Result.Failure<PayoutBatchResponse>(
                PayoutLifecycle.Exists(batch.Status, PayoutBatchStatus.Cancelled)
                    ? SettlementsErrors.NotPermitted("cancel")
                    : SettlementsErrors.BatchNotIn(batch.Status.ToString(), "cancelled"));
        }

        var cycleIds = batch.Items
            .Select(item => item.SettlementCycleId)
            .Where(cycleId => cycleId is not null)
            .Select(cycleId => cycleId!.Value)
            .ToArray();

        var cycles = await context.Cycles
            .IgnoreQueryFilters()
            .Where(cycle => cycle.TenantId == context.TenantId && cycleIds.Contains(cycle.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var cycle in cycles)
        {
            cycle.Batch(null);
        }

        batch.Cancel(command.Reason);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(SettlementProjection.ToBatch(batch, scope.Actor));
    }
}
