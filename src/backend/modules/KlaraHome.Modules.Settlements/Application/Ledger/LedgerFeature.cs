using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Settlements.Domain;
using KlaraHome.Modules.Settlements.Infrastructure;
using KlaraHome.Modules.Settlements.Infrastructure.Accounting;
using KlaraHome.Modules.Settlements.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Primitives;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Settlements.Application.Ledger;

/// <summary>The movements on one seller's account, newest first.</summary>
/// <param name="VendorId">Whose. Ignored for a seller caller, who has only their own.</param>
/// <param name="EntryType">Filter to one kind of movement.</param>
/// <param name="CycleId">Filter to one settlement period.</param>
/// <param name="From">Only movements on or after this instant.</param>
/// <param name="To">Only movements strictly before this instant.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListLedgerEntriesQuery(
    Guid? VendorId,
    string? EntryType,
    Guid? CycleId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Cursor,
    int? Size) : IQuery<PagedResult<LedgerEntryResponse>>;

/// <summary>One seller's statement over a window: opening balance, movements, closing balance.</summary>
/// <param name="VendorId">Whose. Ignored for a seller caller.</param>
/// <param name="From">The first instant covered.</param>
/// <param name="To">The first instant not covered.</param>
internal sealed record GetVendorStatementQuery(
    Guid VendorId,
    DateTimeOffset? From,
    DateTimeOffset? To) : IQuery<LedgerStatementResponse>;

/// <summary>What a seller is owed right now.</summary>
/// <param name="VendorId">Whose. Ignored for a seller caller.</param>
internal sealed record GetVendorBalanceQuery(Guid VendorId) : IQuery<VendorBalanceResponse>;

/// <summary>Writes a correction into a seller's account.</summary>
/// <param name="VendorId">Whose.</param>
/// <param name="Direction">Which way it moves the balance: <c>credit</c> or <c>debit</c>.</param>
/// <param name="Amount">How much, positive.</param>
/// <param name="Reason">Why. It appears on the seller's statement.</param>
internal sealed record PostAdjustmentCommand(
    Guid VendorId,
    string? Direction,
    decimal Amount,
    string? Reason) : ICommand<LedgerEntryResponse>;

/// <summary>
/// Lists the movements on a seller's account.
/// </summary>
/// <remarks>
/// A seller sees only their own, and the vendor query filter in the data layer is what confines them —
/// not the filter this handler applies, which is a convenience for platform staff. That is the whole
/// point of the arrangement: a developer who forgot the filter here would get platform staff a
/// slightly wrong list, not get one seller another's ledger.
/// </remarks>
/// <param name="context">The Settlements data context.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="options">Supplies the page ceiling.</param>
internal sealed class ListLedgerEntriesQueryHandler(
    SettlementsDbContext context,
    SettlementsScope scope,
    IOptions<SettlementsOptions> options)
    : IQueryHandler<ListLedgerEntriesQuery, PagedResult<LedgerEntryResponse>>
{
    public async Task<Result<PagedResult<LedgerEntryResponse>>> HandleAsync(
        ListLedgerEntriesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Math.Min(Cursor.NormalizeSize(query.Size), options.Value.MaxPageSize);
        var rows = context.LedgerEntries.AsNoTracking().AsQueryable();

        if (scope.VendorFilter(query.VendorId) is { } vendorId)
        {
            rows = rows.Where(entry => entry.VendorId == vendorId);
        }

        if (query.EntryType is { Length: > 0 } entryType)
        {
            rows = rows.Where(entry => entry.EntryType == entryType);
        }

        if (query.CycleId is { } cycleId)
        {
            rows = rows.Where(entry => entry.SettlementCycleId == cycleId);
        }

        if (query.From is { } from)
        {
            rows = rows.Where(entry => entry.OccurredAt >= from);
        }

        if (query.To is { } to)
        {
            rows = rows.Where(entry => entry.OccurredAt < to);
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
        var items = page.Take(size).Select(SettlementProjection.ToEntry).ToArray();
        var next = hasMore && items.Length > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<LedgerEntryResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>
/// Builds one seller's statement.
/// </summary>
/// <remarks>
/// <para>
/// Opening balance, movements, closing balance — a bank statement, because that is the document a
/// seller already knows how to read. The opening balance is the sum of everything before the window
/// rather than a stored figure, so a statement asked for twice is the same statement whatever has
/// been posted since.
/// </para>
/// <para>
/// The window is bounded by the export ceiling rather than paged. A statement is a document: half of
/// one is not a statement, and a caller who wants to walk the ledger a page at a time has the list
/// endpoint for that.
/// </para>
/// </remarks>
/// <param name="context">The Settlements data context.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="cycles">Supplies the live balance.</param>
/// <param name="options">Supplies the export ceiling.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class GetVendorStatementQueryHandler(
    SettlementsDbContext context,
    SettlementsScope scope,
    SettlementCycleService cycles,
    IOptions<SettlementsOptions> options,
    IClock clock)
    : IQueryHandler<GetVendorStatementQuery, LedgerStatementResponse>
{
    public async Task<Result<LedgerStatementResponse>> HandleAsync(
        GetVendorStatementQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var vendorId = scope.VendorFilter(query.VendorId) ?? query.VendorId;

        if (vendorId == Guid.Empty)
        {
            return Result.Failure<LedgerStatementResponse>(SettlementsErrors.NotFound("seller"));
        }

        // Ninety days back by default. Long enough to cover a quarter of weekly cycles, short enough
        // that a seller pressing the button gets a document rather than a timeout.
        var to = query.To ?? clock.UtcNow;
        var from = query.From ?? to.AddDays(-90);

        var opening = await BalanceBeforeAsync(vendorId, from, cancellationToken).ConfigureAwait(false);

        var ceiling = options.Value.MaxExportRows;

        var entries = await context.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.VendorId == vendorId
                            && entry.OccurredAt >= from
                            && entry.OccurredAt < to)
            .OrderBy(entry => entry.OccurredAt)
            .ThenBy(entry => entry.Id)
            .Take(ceiling + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (entries.Count > ceiling)
        {
            return Result.Failure<LedgerStatementResponse>(SettlementsErrors.ExportTooLarge(ceiling));
        }

        var totals = entries
            .GroupBy(entry => entry.EntryType, StringComparer.Ordinal)
            .Select(group => new LedgerTotalResponse(
                group.Key,
                group.Count(),
                group.Sum(entry => entry.Amount),
                group.Sum(entry => entry.SignedAmount)))
            .OrderBy(total => LedgerEntryTypes.Rank(total.EntryType))
            .ToArray();

        var movement = entries.Sum(entry => entry.SignedAmount);
        var current = await cycles.BalanceAsync(vendorId, cancellationToken).ConfigureAwait(false);

        return Result.Success(new LedgerStatementResponse(
            vendorId,
            from,
            to,
            opening,
            opening + movement,
            current,
            totals,
            entries.Count > 0 ? entries[0].CurrencyCode : Money.Inr,
            [.. entries.Select(SettlementProjection.ToEntry)]));
    }

    /// <summary>The balance at the moment the window opens.</summary>
    private async Task<decimal> BalanceBeforeAsync(
        Guid vendorId,
        DateTimeOffset before,
        CancellationToken cancellationToken)
    {
        var sums = await context.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.VendorId == vendorId && entry.OccurredAt < before)
            .GroupBy(entry => entry.Direction)
            .Select(group => new { Direction = group.Key, Amount = group.Sum(entry => entry.Amount) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return sums.Sum(sum => sum.Direction == LedgerDirection.Credit ? sum.Amount : -sum.Amount);
    }
}

/// <summary>
/// Answers what a seller is owed, in three numbers.
/// </summary>
/// <remarks>
/// Three sums rather than a statement, because this is a dashboard tile and a support answer: the
/// whole balance, the part no period has drawn in yet, and the part that is closed and waiting for a
/// transfer. The three are different questions — "what have I earned", "what is still accruing" and
/// "when does it arrive" — and a seller asks all of them at once.
/// </remarks>
/// <param name="context">The Settlements data context.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="cycles">Supplies the live balance.</param>
internal sealed class GetVendorBalanceQueryHandler(
    SettlementsDbContext context,
    SettlementsScope scope,
    SettlementCycleService cycles)
    : IQueryHandler<GetVendorBalanceQuery, VendorBalanceResponse>
{
    public async Task<Result<VendorBalanceResponse>> HandleAsync(
        GetVendorBalanceQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var vendorId = scope.VendorFilter(query.VendorId) ?? query.VendorId;

        if (vendorId == Guid.Empty)
        {
            return Result.Failure<VendorBalanceResponse>(SettlementsErrors.NotFound("seller"));
        }

        var current = await cycles.BalanceAsync(vendorId, cancellationToken).ConfigureAwait(false);

        var unsettled = await context.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.VendorId == vendorId && entry.SettlementCycleId == null)
            .GroupBy(entry => entry.Direction)
            .Select(group => new { Direction = group.Key, Amount = group.Sum(entry => entry.Amount) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var awaiting = await context.Cycles
            .AsNoTracking()
            .Where(cycle => cycle.VendorId == vendorId && cycle.Status == SettlementCycleStatus.Closed)
            .SumAsync(cycle => cycle.NetPayable, cancellationToken)
            .ConfigureAwait(false);

        var lastClosed = await context.Cycles
            .AsNoTracking()
            .Where(cycle => cycle.VendorId == vendorId && cycle.ClosedAt != null)
            .MaxAsync(cycle => (DateTimeOffset?)cycle.ClosedAt, cancellationToken)
            .ConfigureAwait(false);

        var lastPaid = await context.Cycles
            .AsNoTracking()
            .Where(cycle => cycle.VendorId == vendorId && cycle.PaidAt != null)
            .MaxAsync(cycle => (DateTimeOffset?)cycle.PaidAt, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new VendorBalanceResponse(
            vendorId,
            current,
            unsettled.Sum(sum => sum.Direction == LedgerDirection.Credit ? sum.Amount : -sum.Amount),
            awaiting,
            Money.Inr,
            lastClosed,
            lastPaid));
    }
}

/// <summary>
/// Writes a correction into a seller's account.
/// </summary>
/// <remarks>
/// <para>
/// The only entry a human writes, and the reason it is refused without a reason: an unexplained
/// adjustment is the entry that becomes an argument six months later, and the seller reads the words
/// on their own statement.
/// </para>
/// <para>
/// It is an append like everything else on this ledger. Correcting a wrong adjustment means writing
/// its opposite, and nothing anywhere edits or deletes one.
/// </para>
/// <para>
/// Deliberately unavailable to a seller. A vendor caller who reached this handler would be adjusting
/// their own balance, and the check is here as well as on the endpoint's permission because the two
/// answer different questions — the permission says who may act, and this says on whose account.
/// </para>
/// </remarks>
/// <param name="context">The Settlements data context.</param>
/// <param name="poster">The one place the ledger is written to.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class PostAdjustmentCommandHandler(
    SettlementsDbContext context,
    SettlementPoster poster,
    SettlementsScope scope,
    IClock clock)
    : ICommandHandler<PostAdjustmentCommand, LedgerEntryResponse>
{
    public async Task<Result<LedgerEntryResponse>> HandleAsync(
        PostAdjustmentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (scope.IsVendor)
        {
            return Result.Failure<LedgerEntryResponse>(SettlementsErrors.VendorForbidden);
        }

        if (command.VendorId == Guid.Empty)
        {
            return Result.Failure<LedgerEntryResponse>(SettlementsErrors.NotFound("seller"));
        }

        if (command.Amount <= 0m)
        {
            return Result.Failure<LedgerEntryResponse>(SettlementsErrors.AdjustmentAmount);
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result.Failure<LedgerEntryResponse>(SettlementsErrors.AdjustmentReason);
        }

        if (!TryDirection(command.Direction, out var direction))
        {
            return Result.Failure<LedgerEntryResponse>(SettlementsErrors.AdjustmentDirection);
        }

        var entry = poster.PostAdjustment(
            command.VendorId,
            direction,
            command.Amount,
            Money.Inr,
            command.Reason.Trim(),
            clock.UtcNow);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(SettlementProjection.ToEntry(entry));
    }

    /// <summary>Reads the direction a caller asked for, refusing anything it does not recognise.</summary>
    /// <remarks>
    /// Refused rather than defaulted. Guessing that an unrecognised word meant "credit" would credit
    /// a seller because somebody made a typing mistake.
    /// </remarks>
    private static bool TryDirection(string? value, out LedgerDirection direction)
    {
        direction = LedgerDirection.Credit;

        return !string.IsNullOrWhiteSpace(value)
               && Enum.TryParse(value, ignoreCase: true, out direction)
               && Enum.IsDefined(direction);
    }
}
