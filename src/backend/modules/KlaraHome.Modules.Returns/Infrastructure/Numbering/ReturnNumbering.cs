using System.Globalization;
using KlaraHome.Modules.Returns.Domain;
using KlaraHome.Modules.Returns.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Returns.Infrastructure.Numbering;

/// <summary>
/// Allocates RMA and credit-note numbers, gaplessly.
/// </summary>
/// <remarks>
/// <para>
/// Every number this module hands out comes from here, and none of them comes from a PostgreSQL
/// sequence. <c>nextval</c> is deliberately non-transactional — a rolled-back transaction keeps the
/// number it consumed — and a hole in a credit-note series is a finding at an audit rather than a
/// cosmetic problem (docs/03-database-design.md §4.8).
/// </para>
/// <para>
/// The allocation is therefore meant to be: find or create the counter row, take it with
/// <c>SELECT ... FOR UPDATE</c>, read it, increment it, with the lock held until the caller's
/// transaction ends — serialising allocation within one series and giving the number back if the
/// caller rolls back.
/// </para>
/// <para>
/// <b>That is not what happens today, and it is a known, parked defect (PARKING_LOT.md; found while
/// closing Step 17's test debt).</b> No caller of <see cref="NextCreditNoteNumberAsync"/> opens an
/// explicit transaction before calling it, so the <c>FOR UPDATE</c> row lock — taken by a bare
/// command with no ambient transaction — is released by Npgsql's autocommit the instant that one
/// statement completes, long before the caller's own <c>SaveChangesAsync</c> writes the increment.
/// Two concurrent callers can both read the same counter value and both attempt to commit a credit
/// note carrying it, which the unique index on <c>(tenant, vendor, financial year, number)</c> then
/// refuses as a 500 rather than the graceful retry this class's shape suggests. Reproduced directly:
/// two return inspections issuing credit notes for the same seller at the same time. Fixing it
/// properly means every caller opening its own transaction before asking for a number, which is
/// wider than this module can decide alone — parked for the User rather than patched here in a way
/// that could silently drop other pending writes on the same context (see the parking-lot entry for
/// the trade-off considered and rejected).
/// </para>
/// <para>
/// The series is narrow so the lock is narrow: RMA numbers are scoped to a calendar month and
/// credit-note numbers to one seller's financial year, so two sellers crediting at once never wait
/// for each other.
/// </para>
/// </remarks>
/// <param name="context">The Returns data context; the lock lives in its transaction.</param>
/// <param name="options">Supplies the prefix and the padding.</param>
internal sealed class ReturnNumbering(ReturnsDbContext context, IOptions<ReturnsOptions> options)
{
    /// <summary>
    /// Takes the next RMA number, as <c>RMA-2609-000184</c>.
    /// </summary>
    /// <remarks>
    /// The period is in the number on purpose: it makes an RMA number self-dating for a support
    /// conversation, and it keeps each month's sequence short enough to read aloud.
    /// </remarks>
    /// <param name="raisedAt">When the return is being raised.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<string> NextReturnNumberAsync(
        DateTimeOffset raisedAt,
        CancellationToken cancellationToken)
    {
        var period = FinancialYear.PeriodOf(raisedAt);
        var year = FinancialYear.Of(raisedAt);
        var next = await TakeAsync(NumberSequenceKinds.Return, period, year, cancellationToken)
            .ConfigureAwait(false);

        var digits = options.Value.ReturnNumberDigits;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{options.Value.ReturnNumberPrefix}-{period}-{next.ToString($"D{digits}", CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// Takes the next credit-note number for one seller in one financial year.
    /// </summary>
    /// <remarks>
    /// Scoped to the seller because the series belongs to them: the note reduces their output tax
    /// under their GSTIN, and rule 53 of the CGST Rules asks for a consecutive series per supplier
    /// per financial year, not per marketplace. The scope key is the seller's id rather than their
    /// code, so a seller given a code halfway through a year cannot silently start a second series.
    /// </remarks>
    /// <param name="vendorCode">The seller's short code, which becomes the readable series.</param>
    /// <param name="vendorId">The seller, which is the stable scope.</param>
    /// <param name="issuedAt">When the note is being raised.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The series, the number drawn from it, and the financial year.</returns>
    public async Task<(string Series, string Number, string FinancialYear)> NextCreditNoteNumberAsync(
        string? vendorCode,
        Guid vendorId,
        DateTimeOffset issuedAt,
        CancellationToken cancellationToken)
    {
        var year = FinancialYear.Of(issuedAt);
        var scope = vendorId.ToString("N", CultureInfo.InvariantCulture);

        // "CN" in the series so a credit note is never mistaken for an invoice on a statement. The
        // two series are independent counters and would otherwise both read as `ACME/2026-27/00042`.
        var series = string.IsNullOrWhiteSpace(vendorCode)
            ? $"CN/{year}"
            : $"{vendorCode.Trim().ToUpperInvariant()}/CN/{year}";

        var next = await TakeAsync(NumberSequenceKinds.CreditNote, scope, year, cancellationToken)
            .ConfigureAwait(false);

        var digits = options.Value.CreditNoteDigits;
        var number = string.Create(
            CultureInfo.InvariantCulture,
            $"{series}/{next.ToString($"D{digits}", CultureInfo.InvariantCulture)}");

        return (series, number, year);
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
    private async Task<long> TakeAsync(
        string kind,
        string scopeKey,
        string financialYear,
        CancellationToken cancellationToken)
    {
        var sequence = await LockAsync(kind, scopeKey, financialYear, cancellationToken).ConfigureAwait(false);

        if (sequence is not null)
        {
            return sequence.Take();
        }

        // Nobody has used this series yet. Two requests can reach here at once; the unique index on
        // (tenant, kind, scope, year) decides which one creates it and the loser re-reads.
        var opened = NumberSequence.Start(kind, scopeKey, financialYear);
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
        // something to paper over with an invented number.
        var winner = await LockAsync(kind, scopeKey, financialYear, cancellationToken).ConfigureAwait(false)
                     ?? throw new InvalidOperationException(
                         $"The {kind} number series '{scopeKey}' for {financialYear} could not be opened.");

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
        string financialYear,
        CancellationToken cancellationToken)
    {
        var tenantId = context.TenantId;

        return await context.NumberSequences
            // xmin is named explicitly because it is a system column: SELECT * omits it, and the model maps
            // it as this entity's concurrency token.
            .FromSql(
                $"""
                 SELECT *, xmin FROM returns.number_sequences
                 WHERE tenant_id = {tenantId}
                   AND kind = {kind}
                   AND scope_key = {scopeKey}
                   AND financial_year = {financialYear}
                 FOR UPDATE
                 """)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
