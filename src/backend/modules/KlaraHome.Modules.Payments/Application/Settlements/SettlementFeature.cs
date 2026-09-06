using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Payments.Application.Payments;
using KlaraHome.Modules.Payments.Domain;
using KlaraHome.Modules.Payments.Infrastructure;
using KlaraHome.Modules.Payments.Infrastructure.Persistence;
using KlaraHome.Modules.Payments.Infrastructure.Processing;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Payments.Application.Settlements;

/// <summary>Lists imported settlement reports, newest first.</summary>
/// <param name="From">Only reports settled on or after this instant.</param>
/// <param name="To">Only reports settled strictly before this instant.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListSettlementsQuery(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Cursor,
    int? Size) : IQuery<PagedResult<SettlementResponse>>;

/// <summary>Reads one report and its reconciliation counts.</summary>
/// <param name="SettlementId">The report.</param>
internal sealed record GetSettlementQuery(Guid SettlementId) : IQuery<SettlementResponse>;

/// <summary>Lists one report's lines — the screen a mismatch is addressed on.</summary>
/// <param name="SettlementId">The report.</param>
/// <param name="MatchStatus">Filter to <c>Mismatched</c>, which is what somebody has to act on.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListSettlementEntriesQuery(
    Guid SettlementId,
    string? MatchStatus,
    string? Cursor,
    int? Size) : IQuery<PagedResult<SettlementEntryResponse>>;

/// <summary>Pulls settlement reports for a window and matches their lines.</summary>
/// <param name="From">Start of the window, or null for the configured lookback.</param>
/// <param name="To">End of the window, or null for now.</param>
internal sealed record ImportSettlementsCommand(DateTimeOffset? From, DateTimeOffset? To)
    : ICommand<SettlementIngestionSummary>;

/// <summary>Runs the reconciliation sweep now, rather than waiting for the timer.</summary>
internal sealed record RunReconciliationCommand : ICommand<ReconciliationSummary>;

/// <summary>Lists imported reports.</summary>
/// <param name="context">The Payments data context.</param>
/// <param name="options">Supplies the page ceiling.</param>
internal sealed class ListSettlementsQueryHandler(PaymentsDbContext context, IOptions<PaymentsOptions> options)
    : IQueryHandler<ListSettlementsQuery, PagedResult<SettlementResponse>>
{
    public async Task<Result<PagedResult<SettlementResponse>>> HandleAsync(
        ListSettlementsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Math.Min(Cursor.NormalizeSize(query.Size), options.Value.MaxPageSize);
        var rows = context.GatewaySettlements.AsNoTracking().AsQueryable();

        if (query.From is { } from)
        {
            rows = rows.Where(settlement => settlement.SettledAt >= from);
        }

        if (query.To is { } to)
        {
            rows = rows.Where(settlement => settlement.SettledAt < to);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(settlement => settlement.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(settlement => settlement.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var items = page.Take(size).Select(PaymentProjection.ToSettlement).ToArray();
        var next = hasMore && items.Length > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<SettlementResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>Reads one report.</summary>
/// <param name="context">The Payments data context.</param>
internal sealed class GetSettlementQueryHandler(PaymentsDbContext context)
    : IQueryHandler<GetSettlementQuery, SettlementResponse>
{
    public async Task<Result<SettlementResponse>> HandleAsync(
        GetSettlementQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var settlement = await context.GatewaySettlements
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == query.SettlementId, cancellationToken)
            .ConfigureAwait(false);

        return settlement is null
            ? Result.Failure<SettlementResponse>(PaymentsErrors.NotFound("settlement"))
            : Result.Success(PaymentProjection.ToSettlement(settlement));
    }
}

/// <summary>Lists one report's lines.</summary>
/// <param name="context">The Payments data context.</param>
/// <param name="options">Supplies the page ceiling.</param>
internal sealed class ListSettlementEntriesQueryHandler(
    PaymentsDbContext context,
    IOptions<PaymentsOptions> options)
    : IQueryHandler<ListSettlementEntriesQuery, PagedResult<SettlementEntryResponse>>
{
    public async Task<Result<PagedResult<SettlementEntryResponse>>> HandleAsync(
        ListSettlementEntriesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Math.Min(Cursor.NormalizeSize(query.Size), options.Value.MaxPageSize);

        var rows = context.GatewaySettlementEntries
            .AsNoTracking()
            .Where(entry => entry.SettlementId == query.SettlementId);

        if (Enum.TryParse<SettlementMatchStatus>(query.MatchStatus, ignoreCase: true, out var match))
        {
            rows = rows.Where(entry => entry.MatchStatus == match);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(entry => entry.Id.CompareTo(after) > 0);
        }

        var page = await rows
            .OrderBy(entry => entry.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var items = page.Take(size).Select(PaymentProjection.ToSettlementEntry).ToArray();
        var next = hasMore && items.Length > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<SettlementEntryResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>
/// Pulls settlement reports on demand.
/// </summary>
/// <remarks>
/// The same <see cref="SettlementIngestionService"/> the scheduled job runs, so the button and the
/// timer cannot reach different answers. Ingestion is idempotent on the gateway's settlement id, so
/// an operator pressing it twice imports nothing twice.
/// </remarks>
/// <param name="ingestion">Does the work.</param>
/// <param name="options">Supplies the default lookback.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class ImportSettlementsCommandHandler(
    SettlementIngestionService ingestion,
    IOptions<PaymentsOptions> options,
    IClock clock) : ICommandHandler<ImportSettlementsCommand, SettlementIngestionSummary>
{
    public async Task<Result<SettlementIngestionSummary>> HandleAsync(
        ImportSettlementsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.UtcNow;
        var to = command.To ?? now;
        var from = command.From ?? to.AddDays(-options.Value.SettlementLookbackDays);

        return await ingestion.IngestAsync(from, to, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Runs the reconciliation sweep on demand.
/// </summary>
/// <remarks>
/// What an operator presses when a customer says they paid and the order says otherwise. It is the
/// same service the scheduled sweep runs, so an investigation and the timer cannot disagree about
/// what the gateway says.
/// </remarks>
/// <param name="reconciliation">Does the work.</param>
internal sealed class RunReconciliationCommandHandler(PaymentReconciliationService reconciliation)
    : ICommandHandler<RunReconciliationCommand, ReconciliationSummary>
{
    public async Task<Result<ReconciliationSummary>> HandleAsync(
        RunReconciliationCommand command,
        CancellationToken cancellationToken)
        => await reconciliation.SweepAsync(cancellationToken).ConfigureAwait(false);
}
