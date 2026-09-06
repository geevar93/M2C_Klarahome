using System.Globalization;
using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Modules.Settlements.Domain;
using KlaraHome.Modules.Settlements.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Settlements.Infrastructure.Accounting;

/// <summary>
/// The one place a seller's account is written to.
/// </summary>
/// <remarks>
/// <para>
/// Every route into the ledger comes through here — a delivered parcel, a courier's cash remittance,
/// a cancellation, a credit note, a payout, an operator's correction — which is what stops six
/// callers drifting about what an earning is worth or what a reversal gives back.
/// </para>
/// <para>
/// <b>Every posting is idempotent, and the guarantee is a unique index.</b> Integration events are
/// delivered at least once, so a redelivered "this parcel arrived" would otherwise credit a seller
/// twice. Each entry carries a source key derived from the fact rather than generated; the second
/// attempt collides on <c>(tenant_id, source_key)</c> and posts nothing. The pre-check that skips the
/// work is an optimisation — the index is the correctness.
/// </para>
/// <para>
/// It writes and it does not commit. The caller's <c>SaveChangesAsync</c> is what makes a posting
/// real, so an event handler that posts and then fails posts nothing — the same guarantee the outbox
/// gives in the other direction.
/// </para>
/// </remarks>
/// <param name="context">The Settlements data context.</param>
/// <param name="orders">Reads what was sold, over the contract.</param>
/// <param name="commissions">Resolves a rate for a line frozen without one.</param>
/// <param name="settings">Supplies the store's settlement policy.</param>
/// <param name="logger">Reports what was posted and what was skipped.</param>
internal sealed partial class SettlementPoster(
    SettlementsDbContext context,
    IOrderSettlement orders,
    ICommissionResolver commissions,
    IStoreSettings settings,
    ILogger<SettlementPoster> logger)
{
    /// <summary>
    /// Posts what a delivered sub-order earns its seller, and what comes off it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called from two places and posting the same thing from both: a prepaid parcel earns on
    /// delivery, and a cash-on-delivery parcel earns when the courier remits, which can be a week
    /// later. Both go through one method with one source key, so a parcel that somehow triggered both
    /// is credited once.
    /// </para>
    /// <para>
    /// It returns quietly when there is nothing to post. A sub-order that has been entirely cancelled,
    /// or one the ordering module no longer knows about, is not an error worth failing an event
    /// handler over — the outbox would retry it eight times and dead-letter a fact that is simply not
    /// interesting.
    /// </para>
    /// </remarks>
    /// <param name="subOrderId">The seller's part of the order.</param>
    /// <param name="occurredAt">When the earning arose.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What was posted, or an empty list when nothing was.</returns>
    public async Task<IReadOnlyList<LedgerEntry>> PostEarningAsync(
        Guid subOrderId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var key = SourceKey(LedgerEntryTypes.Sale, LedgerReferenceTypes.SubOrder, subOrderId);

        if (await ExistsAsync(key, cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        var sale = await orders.GetAsync(subOrderId, cancellationToken).ConfigureAwait(false);

        if (sale.IsFailure)
        {
            SaleNotFound(logger, subOrderId);
            return [];
        }

        var view = sale.Value;
        var policy = await settings.GetAsync<SettlementSettings>(cancellationToken).ConfigureAwait(false);
        var fallback = await FallbackCommissionAsync(view, cancellationToken).ConfigureAwait(false);

        var postings = SettlementCalculator.Earning(view, policy, fallback);

        if (postings.Count == 0)
        {
            return [];
        }

        var entries = postings
            .Select(posting => Build(
                posting,
                view.VendorId,
                view.CurrencyCode,
                LedgerReferenceTypes.SubOrder,
                subOrderId,
                occurredAt).Against(subOrderId))
            .ToArray();

        context.LedgerEntries.AddRange(entries);

        Earned(logger, view.SubOrderNumber, view.VendorId, entries[0].Amount);

        return entries;
    }

    /// <summary>
    /// Posts a supply coming back off a seller's account, and the charges that come back with it.
    /// </summary>
    /// <remarks>
    /// Reads what was originally posted rather than recomputing it. The proportion given back is
    /// taken against the sale entry that is actually on the ledger, so a reversal against a sale that
    /// was never posted — a cancellation before delivery, a return on an order that predates this
    /// module — posts nothing rather than inventing a credit.
    /// </remarks>
    /// <param name="vendorId">The seller.</param>
    /// <param name="subOrderId">The seller's part of the order the goods came off.</param>
    /// <param name="amount">What is coming off, inclusive of tax.</param>
    /// <param name="taxableValue">What that was worth before tax.</param>
    /// <param name="currencyCode">The currency.</param>
    /// <param name="referenceType">What caused the reversal.</param>
    /// <param name="referenceId">The id of that document.</param>
    /// <param name="keySuffix">
    /// What makes this reversal distinct from another against the same sub-order — a credit-note id,
    /// or the id of the cancellation event.
    /// </param>
    /// <param name="note">What the reversal is, in the words a statement shows.</param>
    /// <param name="occurredAt">When it happened.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<LedgerEntry>> PostReversalAsync(
        Guid vendorId,
        Guid subOrderId,
        decimal amount,
        decimal taxableValue,
        string currencyCode,
        string referenceType,
        Guid? referenceId,
        string keySuffix,
        string note,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var key = SourceKey(LedgerEntryTypes.Refund, referenceType, keySuffix);

        if (await ExistsAsync(key, cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        var posted = await context.LedgerEntries
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(entry => entry.TenantId == context.TenantId && entry.SubOrderId == subOrderId)
            .Select(entry => new { entry.EntryType, entry.Amount })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var originalSale = posted
            .Where(entry => entry.EntryType == LedgerEntryTypes.Sale)
            .Sum(entry => entry.Amount);

        if (originalSale <= 0m)
        {
            NothingToReverse(logger, subOrderId);
            return [];
        }

        // The freight is deliberately not in this sum. A parcel that was delivered and then returned
        // was still delivered, and the courier was still paid.
        var reversible = posted
            .Where(entry => entry.EntryType is LedgerEntryTypes.Commission
                or LedgerEntryTypes.PlatformFee
                or LedgerEntryTypes.PaymentFee
                or LedgerEntryTypes.PlatformTax)
            .Sum(entry => entry.Amount);

        var alreadyReversed = posted
            .Where(entry => entry.EntryType == LedgerEntryTypes.Refund)
            .Sum(entry => entry.Amount);

        // Clamped to what is left of the sale. Two returns and a cancellation against one parcel can
        // legitimately arrive, and between them they must never take back more than was credited.
        var remaining = Math.Max(0m, originalSale - alreadyReversed);
        var reversing = Math.Min(amount, remaining);

        if (reversing <= 0m)
        {
            NothingToReverse(logger, subOrderId);
            return [];
        }

        var scaledTaxable = amount > 0m ? taxableValue * (reversing / amount) : 0m;

        var postings = SettlementCalculator.Reversal(
            reversing,
            scaledTaxable,
            originalSale,
            reversible,
            note);

        if (postings.Count == 0)
        {
            return [];
        }

        var entries = postings
            .Select(posting => Build(
                posting,
                vendorId,
                currencyCode,
                referenceType,
                referenceId,
                occurredAt,
                keySuffix).Against(subOrderId))
            .ToArray();

        context.LedgerEntries.AddRange(entries);

        Reversed(logger, subOrderId, vendorId, reversing);

        return entries;
    }

    /// <summary>Posts a correction somebody made deliberately.</summary>
    /// <remarks>
    /// The only entry a human writes, and the only one whose direction is a choice. It is an append
    /// like every other: correcting a wrong adjustment means writing its opposite, never editing it.
    /// </remarks>
    /// <param name="vendorId">The seller.</param>
    /// <param name="direction">Which way it moves the balance.</param>
    /// <param name="amount">How much, positive.</param>
    /// <param name="currencyCode">The currency.</param>
    /// <param name="note">Why, in the words a statement shows.</param>
    /// <param name="occurredAt">When.</param>
    public LedgerEntry PostAdjustment(
        Guid vendorId,
        LedgerDirection direction,
        decimal amount,
        string currencyCode,
        string note,
        DateTimeOffset occurredAt)
    {
        var entry = LedgerEntry
            .Post(
                vendorId,
                LedgerEntryTypes.Adjustment,
                direction,
                SettlementCalculator.Round(amount),
                currencyCode,
                LedgerReferenceTypes.Manual,
                referenceId: null,
                SourceKey(LedgerEntryTypes.Adjustment, LedgerReferenceTypes.Manual, Guid.CreateVersion7()),
                occurredAt)
            .Describe(note);

        context.LedgerEntries.Add(entry);

        return entry;
    }

    /// <summary>Posts money that actually reached a seller.</summary>
    /// <remarks>
    /// Written when the gateway confirms the transfer and not when it is requested, so the ledger
    /// balance is what is still owed rather than what somebody intends to send. It is keyed on the
    /// payout item, so a reconciliation sweep that learns the same completion twice debits once.
    /// </remarks>
    /// <param name="item">The transfer.</param>
    /// <param name="occurredAt">When the gateway settled it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<LedgerEntry?> PostPayoutAsync(
        PayoutItem item,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        var key = SourceKey(LedgerEntryTypes.Payout, LedgerReferenceTypes.Payout, item.Id);

        if (await ExistsAsync(key, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var entry = LedgerEntry
            .Post(
                item.VendorId ?? Guid.Empty,
                LedgerEntryTypes.Payout,
                LedgerDirection.Debit,
                item.Amount,
                item.CurrencyCode,
                LedgerReferenceTypes.Payout,
                item.Id,
                key,
                occurredAt)
            .Describe($"Payout {item.Utr ?? item.ProviderPayoutId ?? item.Id.ToString()}");

        entry.PaidIn(item.PayoutBatchId);
        context.LedgerEntries.Add(entry);

        return entry;
    }

    /// <summary>Posts the two statutory deductions computed on a closing period.</summary>
    /// <remarks>
    /// They are ledger entries like everything else, and that is deliberate: the seller's balance is
    /// the sum of the rows, so a deduction that lived only as a column on the cycle would leave the
    /// balance and the statement disagreeing by exactly the tax.
    /// </remarks>
    /// <param name="vendorId">The seller.</param>
    /// <param name="cycleId">The cycle being closed.</param>
    /// <param name="deduction">What the state takes, and on what bases.</param>
    /// <param name="currencyCode">The currency.</param>
    /// <param name="occurredAt">When the period was closed.</param>
    public IReadOnlyList<LedgerEntry> PostDeductions(
        Guid vendorId,
        Guid cycleId,
        StatutoryDeduction deduction,
        string currencyCode,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(deduction);

        var entries = new List<LedgerEntry>(2);

        Add(LedgerEntryTypes.Tcs, deduction.Tcs, deduction.TcsBase, "Tax collected at source (GST s.52)");
        Add(LedgerEntryTypes.Tds, deduction.Tds, deduction.TdsBase, "Tax deducted at source (s.194-O)");

        context.LedgerEntries.AddRange(entries);

        return entries;

        void Add(string entryType, decimal amount, decimal basis, string note)
        {
            if (amount <= 0m)
            {
                return;
            }

            var entry = LedgerEntry
                .Post(
                    vendorId,
                    entryType,
                    LedgerDirection.Debit,
                    amount,
                    currencyCode,
                    LedgerReferenceTypes.Cycle,
                    cycleId,
                    SourceKey(entryType, LedgerReferenceTypes.Cycle, cycleId),
                    occurredAt)
                .Taxed(basis)
                .Describe(note);

            entry.AssignTo(cycleId);
            entries.Add(entry);
        }
    }

    /// <summary>
    /// The key a redelivery of the same fact collides on.
    /// </summary>
    /// <remarks>
    /// Derived from what happened rather than generated, which is the whole mechanism. The entry type
    /// is part of it because one fact posts several entries, and each of them needs a key of its own.
    /// </remarks>
    /// <param name="entryType">The kind of movement.</param>
    /// <param name="referenceType">What it points back at.</param>
    /// <param name="reference">The id or discriminator of that document.</param>
    public static string SourceKey(string entryType, string referenceType, object reference)
        => string.Create(CultureInfo.InvariantCulture, $"{entryType}:{referenceType}:{reference}");

    /// <summary>Whether a movement with this key has already been posted.</summary>
    /// <remarks>
    /// The tenant is written into the predicate rather than left to the global filter, and the vendor
    /// filter is dropped: a background handler runs with no caller, and a check that silently saw
    /// nothing would let a redelivery post a second credit.
    /// </remarks>
    private Task<bool> ExistsAsync(string sourceKey, CancellationToken cancellationToken)
        => context.LedgerEntries
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(
                entry => entry.TenantId == context.TenantId && entry.SourceKey == sourceKey,
                cancellationToken);

    /// <summary>Turns one posting into the entry that records it.</summary>
    private static LedgerEntry Build(
        LedgerPosting posting,
        Guid vendorId,
        string currencyCode,
        string referenceType,
        Guid? referenceId,
        DateTimeOffset occurredAt,
        string? keySuffix = null)
        => LedgerEntry
            .Post(
                vendorId,
                posting.EntryType,
                posting.Direction,
                posting.Amount,
                currencyCode,
                referenceType,
                referenceId,
                SourceKey(
                    posting.EntryType,
                    referenceType,
                    keySuffix ?? referenceId?.ToString() ?? Guid.CreateVersion7().ToString()),
                occurredAt)
            .Taxed(posting.TaxableValue)
            .Describe(posting.Note);

    /// <summary>
    /// A commission for a sub-order whose lines were frozen without one.
    /// </summary>
    /// <remarks>
    /// Resolved once for the whole sub-order rather than per line, and only when at least one line
    /// needs it. The rate depends on the category and the unit price, so resolving it from the first
    /// uncommissioned line is an approximation — and it is the right one, because the alternative is
    /// charging nothing at all for a sale the platform did make.
    /// </remarks>
    private async ValueTask<CommissionQuote?> FallbackCommissionAsync(
        SubOrderSettlementView sale,
        CancellationToken cancellationToken)
    {
        var uncommissioned = sale.Lines.FirstOrDefault(line =>
            line.CommissionAmount <= 0m && line.Quantity > line.QuantityCancelled);

        if (uncommissioned is null)
        {
            return null;
        }

        var quote = await commissions
            .ResolveAsync(sale.VendorId, uncommissioned.CategoryId, uncommissioned.UnitPrice, cancellationToken)
            .ConfigureAwait(false);

        if (quote is null)
        {
            NoCommissionPlan(logger, sale.SubOrderNumber, sale.VendorId);
        }

        return quote;
    }

    [LoggerMessage(EventId = 1800, Level = LogLevel.Information,
        Message = "Settled {SubOrderNumber} for vendor {VendorId}: credited {Amount}.")]
    private static partial void Earned(ILogger logger, string subOrderNumber, Guid vendorId, decimal amount);

    [LoggerMessage(EventId = 1801, Level = LogLevel.Information,
        Message = "Reversed {Amount} against sub-order {SubOrderId} for vendor {VendorId}.")]
    private static partial void Reversed(ILogger logger, Guid subOrderId, Guid vendorId, decimal amount);

    [LoggerMessage(EventId = 1802, Level = LogLevel.Warning,
        Message = "Sub-order {SubOrderId} could not be read, so nothing was settled against it.")]
    private static partial void SaleNotFound(ILogger logger, Guid subOrderId);

    [LoggerMessage(EventId = 1803, Level = LogLevel.Warning,
        Message = "Nothing has been credited for sub-order {SubOrderId}, so there is nothing to reverse.")]
    private static partial void NothingToReverse(ILogger logger, Guid subOrderId);

    [LoggerMessage(EventId = 1804, Level = LogLevel.Warning,
        Message = "Sub-order {SubOrderNumber} has lines with no commission and vendor {VendorId} has no "
                  + "commission plan, so no commission was charged on them.")]
    private static partial void NoCommissionPlan(ILogger logger, string subOrderNumber, Guid vendorId);
}
