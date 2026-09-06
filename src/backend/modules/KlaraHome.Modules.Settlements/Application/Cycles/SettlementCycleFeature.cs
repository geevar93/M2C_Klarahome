using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Settlements.Domain;
using KlaraHome.Modules.Settlements.Infrastructure;
using KlaraHome.Modules.Settlements.Infrastructure.Accounting;
using KlaraHome.Modules.Settlements.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Settlements.Application.Cycles;

/// <summary>The settlement periods, newest first.</summary>
/// <param name="VendorId">Filter to one seller. Ignored for a seller caller, who has only their own.</param>
/// <param name="Status">Filter to <c>Open</c>, <c>Closed</c> or <c>Paid</c>.</param>
/// <param name="From">Only periods that end on or after this instant.</param>
/// <param name="To">Only periods that start strictly before this instant.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListSettlementCyclesQuery(
    Guid? VendorId,
    string? Status,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Cursor,
    int? Size) : IQuery<PagedResult<SettlementCycleResponse>>;

/// <summary>One settlement period in full.</summary>
/// <param name="CycleId">The cycle.</param>
internal sealed record GetSettlementCycleQuery(Guid CycleId) : IQuery<SettlementCycleResponse>;

/// <summary>
/// Closes a settlement period by hand.
/// </summary>
/// <remarks>
/// The scheduler does this unattended and on time; the endpoint exists for the periods it could not —
/// a seller onboarded mid-week, a store that has just turned settlement on, a period whose closing
/// failed and was logged. It applies exactly the same rules, including the hold.
/// </remarks>
/// <param name="CycleId">The cycle to close, when it already exists.</param>
/// <param name="VendorId">The seller, when closing a period no cycle has been opened for.</param>
/// <param name="Force">
/// Whether to close before the hold expires. Refused unless it is set, because the hold is the thing
/// that stops a seller being paid for goods a shopper is still entitled to send back.
/// </param>
internal sealed record CloseSettlementCycleCommand(
    Guid? CycleId,
    Guid? VendorId,
    bool Force) : ICommand<SettlementCycleResponse>;

/// <summary>
/// Lists settlement periods.
/// </summary>
/// <remarks>
/// The seller's name is resolved in one batched call after the page is read, never per row. A list of
/// fifty cycles is one query and one directory lookup; resolving inside the projection would be
/// fifty-one.
/// </remarks>
/// <param name="context">The Settlements data context.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="vendors">Resolves seller names for the page.</param>
/// <param name="options">Supplies the page ceiling.</param>
internal sealed class ListSettlementCyclesQueryHandler(
    SettlementsDbContext context,
    SettlementsScope scope,
    IVendorPayouts vendors,
    IOptions<SettlementsOptions> options)
    : IQueryHandler<ListSettlementCyclesQuery, PagedResult<SettlementCycleResponse>>
{
    public async Task<Result<PagedResult<SettlementCycleResponse>>> HandleAsync(
        ListSettlementCyclesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Math.Min(Cursor.NormalizeSize(query.Size), options.Value.MaxPageSize);
        var rows = context.Cycles.AsNoTracking().AsQueryable();

        if (scope.VendorFilter(query.VendorId) is { } vendorId)
        {
            rows = rows.Where(cycle => cycle.VendorId == vendorId);
        }

        if (Enum.TryParse<SettlementCycleStatus>(query.Status, ignoreCase: true, out var status))
        {
            rows = rows.Where(cycle => cycle.Status == status);
        }

        if (query.From is { } from)
        {
            rows = rows.Where(cycle => cycle.PeriodEnd >= from);
        }

        if (query.To is { } to)
        {
            rows = rows.Where(cycle => cycle.PeriodStart < to);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(cycle => cycle.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(cycle => cycle.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var window = page.Take(size).ToArray();

        var profiles = await vendors
            .FindManyAsync([.. window.Select(cycle => cycle.VendorId ?? Guid.Empty).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        var items = window
            .Select(cycle =>
            {
                var profile = profiles.GetValueOrDefault(cycle.VendorId ?? Guid.Empty);
                return SettlementProjection.ToCycle(cycle, profile?.Code, profile?.LegalName);
            })
            .ToArray();

        var next = hasMore && items.Length > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<SettlementCycleResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>Reads one settlement period.</summary>
/// <param name="context">The Settlements data context.</param>
/// <param name="vendors">Resolves the seller's name.</param>
internal sealed class GetSettlementCycleQueryHandler(SettlementsDbContext context, IVendorPayouts vendors)
    : IQueryHandler<GetSettlementCycleQuery, SettlementCycleResponse>
{
    public async Task<Result<SettlementCycleResponse>> HandleAsync(
        GetSettlementCycleQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var cycle = await context.Cycles
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == query.CycleId, cancellationToken)
            .ConfigureAwait(false);

        if (cycle is null)
        {
            return Result.Failure<SettlementCycleResponse>(SettlementsErrors.NotFound("settlement period"));
        }

        var profile = await vendors
            .FindAsync(cycle.VendorId ?? Guid.Empty, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(SettlementProjection.ToCycle(cycle, profile?.Code, profile?.LegalName));
    }
}

/// <summary>
/// Closes a settlement period on somebody's instruction.
/// </summary>
/// <remarks>
/// <para>
/// Two ways in, and the difference is whether the cycle exists yet. Naming a cycle closes that one;
/// naming a seller closes the period that is currently due for them, opening the cycle if the
/// scheduler has not. The second is what an operator actually wants when a seller asks to be settled
/// early.
/// </para>
/// <para>
/// The hold is enforced unless the caller explicitly overrides it. It is what stops a seller being
/// paid for goods a shopper is still entitled to send back, and defaulting the override on would make
/// the protection a thing people forget rather than a thing they decide.
/// </para>
/// </remarks>
/// <param name="context">The Settlements data context.</param>
/// <param name="cycles">The one place a period is closed.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="settings">Supplies the frequency and the hold.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class CloseSettlementCycleCommandHandler(
    SettlementsDbContext context,
    SettlementCycleService cycles,
    SettlementsScope scope,
    IStoreSettings settings,
    IClock clock)
    : ICommandHandler<CloseSettlementCycleCommand, SettlementCycleResponse>
{
    public async Task<Result<SettlementCycleResponse>> HandleAsync(
        CloseSettlementCycleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (scope.IsVendor)
        {
            return Result.Failure<SettlementCycleResponse>(SettlementsErrors.VendorForbidden);
        }

        var policy = await settings.GetAsync<SettlementSettings>(cancellationToken).ConfigureAwait(false);

        var (vendorId, period, alreadyClosed) = command.CycleId is { } cycleId
            ? await ResolveAsync(cycleId, cancellationToken).ConfigureAwait(false)
            : (command.VendorId, CyclePlanner.PreviousPeriod(clock.UtcNow, policy), false);

        if (vendorId is not { } vendor || vendor == Guid.Empty)
        {
            return Result.Failure<SettlementCycleResponse>(SettlementsErrors.NotFound("settlement period"));
        }

        // Refused rather than answered with the closed cycle. An operator pressing the button twice
        // is asking a question, and "it was already closed on the 14th" is the answer they need —
        // silently handing back the existing figures would look like a second close that changed
        // nothing.
        if (alreadyClosed)
        {
            return Result.Failure<SettlementCycleResponse>(SettlementsErrors.CycleClosed);
        }

        var closableFrom = CyclePlanner.ClosableFrom(period, policy);

        if (!command.Force && clock.UtcNow < closableFrom)
        {
            return Result.Failure<SettlementCycleResponse>(SettlementsErrors.CycleNotDue(closableFrom));
        }

        var closed = await cycles
            .CloseAsync(vendor, period, scope.ActorId, cancellationToken)
            .ConfigureAwait(false);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(SettlementProjection.ToCycle(closed));
    }

    /// <summary>
    /// The seller and period a named cycle covers, and whether it has already been closed.
    /// </summary>
    /// <remarks>
    /// The period is read off the cycle rather than recomputed from the store's frequency, so a cycle
    /// drawn before somebody changed weekly to monthly still closes over the days it actually covers.
    /// </remarks>
    private async Task<(Guid? VendorId, SettlementPeriod Period, bool AlreadyClosed)> ResolveAsync(
        Guid cycleId,
        CancellationToken cancellationToken)
    {
        var cycle = await context.Cycles
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                candidate => candidate.TenantId == context.TenantId && candidate.Id == cycleId,
                cancellationToken)
            .ConfigureAwait(false);

        return cycle is null
            ? (null, default, false)
            : (cycle.VendorId,
               new SettlementPeriod(cycle.PeriodStart, cycle.PeriodEnd),
               cycle.Status != SettlementCycleStatus.Open);
    }
}
