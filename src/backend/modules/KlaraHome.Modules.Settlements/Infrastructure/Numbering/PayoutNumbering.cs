using System.Globalization;
using KlaraHome.Modules.Settlements.Domain;
using KlaraHome.Modules.Settlements.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Settlements.Infrastructure.Numbering;

/// <summary>
/// Allocates payout batch references, gaplessly.
/// </summary>
/// <remarks>
/// <para>
/// The same mechanism the Ordering module uses for invoice numbers and Returns for credit notes, and
/// it is here rather than shared for the reason every duplicated primitive in this platform is: a
/// module may not reference another (docs/03-database-design.md §4.8).
/// </para>
/// <para>
/// The allocation is: find or create the counter row, take it with <c>SELECT ... FOR UPDATE</c>,
/// read it, increment it. The lock is held until the caller's transaction ends, which serialises
/// allocation and gives the number back if the caller rolls back. Both are the point rather than the
/// cost — a payout reference that was consumed by a batch nobody kept is a gap somebody will look
/// for on a bank statement.
/// </para>
/// <para>
/// The series is scoped to a calendar month, so the reference is self-dating for a support
/// conversation and each month's counter stays short enough to read aloud.
/// </para>
/// </remarks>
/// <param name="context">The Settlements data context; the lock lives in its transaction.</param>
/// <param name="options">Supplies the prefix and the padding.</param>
internal sealed class PayoutNumbering(SettlementsDbContext context, IOptions<SettlementsOptions> options)
{
    /// <summary>Takes the next batch reference, as <c>PAY-2609-000042</c>.</summary>
    /// <param name="raisedAt">When the batch is being built.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<string> NextReferenceAsync(DateTimeOffset raisedAt, CancellationToken cancellationToken)
    {
        var period = FinancialYear.PeriodOf(raisedAt);
        var next = await TakeAsync(NumberSequenceKinds.Payout, period, cancellationToken).ConfigureAwait(false);

        var digits = options.Value.PayoutReferenceDigits;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{options.Value.PayoutReferencePrefix}-{period}-{next.ToString($"D{digits}", CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// Finds or opens the counter, locks it, and takes the next value.
    /// </summary>
    /// <remarks>
    /// The <c>FOR UPDATE</c> is what makes this correct, and it is issued through <c>FromSql</c>
    /// rather than through LINQ because EF has no way to express a row lock. The entity comes back
    /// tracked, so the increment is written by the caller's own <c>SaveChangesAsync</c> — inside the
    /// caller's transaction, which is where the number has to be given back from if they roll back.
    /// </remarks>
    private async Task<long> TakeAsync(string kind, string scopeKey, CancellationToken cancellationToken)
    {
        var sequence = await LockAsync(kind, scopeKey, cancellationToken).ConfigureAwait(false);

        if (sequence is not null)
        {
            return sequence.Take();
        }

        // Nobody has used this series yet. Two requests can reach here at once; the unique index on
        // (tenant, kind, scope) decides which one creates it and the loser re-reads.
        var opened = NumberSequence.Start(kind, scopeKey);
        context.NumberSequences.Add(opened);

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return opened.Take();
        }
        catch (DbUpdateException)
        {
            context.Entry(opened).State = EntityState.Detached;
        }

        // Lost the race. The winner's row is the authority, and it certainly exists now — a null
        // here would mean the insert failed for a reason other than the unique index, which is not
        // something to paper over with an invented reference.
        var winner = await LockAsync(kind, scopeKey, cancellationToken).ConfigureAwait(false)
                     ?? throw new InvalidOperationException(
                         $"The {kind} number series '{scopeKey}' could not be opened.");

        return winner.Take();
    }

    /// <summary>
    /// Takes the counter row for one series, or null when it has never been opened.
    /// </summary>
    /// <remarks>
    /// The tenant is written into the predicate rather than left to the global query filter. EF
    /// composes a filter by wrapping raw SQL in a subquery, and a lock taken inside a subquery is a
    /// lock this code would be relying on the provider to preserve — a thing worth being explicit
    /// about on the one query where the lock is the entire point.
    /// </remarks>
    private async Task<NumberSequence?> LockAsync(
        string kind,
        string scopeKey,
        CancellationToken cancellationToken)
    {
        var tenantId = context.TenantId;

        return await context.NumberSequences
            .FromSql(
                $"""
                 SELECT * FROM settlements.number_sequences
                 WHERE tenant_id = {tenantId}
                   AND kind = {kind}
                   AND scope_key = {scopeKey}
                 FOR UPDATE
                 """)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
