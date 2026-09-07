using KlaraHome.Modules.Settlements.Domain;

namespace KlaraHome.Modules.Settlements.Application;

/// <summary>One movement on a seller's account, as an API caller sees it.</summary>
/// <param name="Id">The entry.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="EntryType">What kind of movement.</param>
/// <param name="Direction">Which way it moved the balance: <c>Credit</c> or <c>Debit</c>.</param>
/// <param name="Amount">How much, always positive.</param>
/// <param name="SignedAmount">The same figure with its sign, for a caller that wants to add them up.</param>
/// <param name="TaxableValue">What the tax on it was computed on, or zero where it had none.</param>
/// <param name="CurrencyCode">ISO 4217 code the amounts are in.</param>
/// <param name="ReferenceType">What it points back at.</param>
/// <param name="ReferenceId">The id of that document.</param>
/// <param name="SubOrderId">The seller's part of the order it came from.</param>
/// <param name="SettlementCycleId">The period it was drawn into, or null while it is unsettled.</param>
/// <param name="Note">What it is, in words.</param>
/// <param name="OccurredAt">When it happened.</param>
internal sealed record LedgerEntryResponse(
    Guid Id,
    Guid VendorId,
    LedgerEntryType EntryType,
    LedgerDirection Direction,
    decimal Amount,
    decimal SignedAmount,
    decimal TaxableValue,
    string CurrencyCode,
    string ReferenceType,
    Guid? ReferenceId,
    Guid? SubOrderId,
    Guid? SettlementCycleId,
    string? Note,
    DateTimeOffset OccurredAt);

/// <summary>A seller's statement for a stretch of their ledger.</summary>
/// <remarks>
/// Opening balance, movements, closing balance — a bank statement, because that is the document a
/// seller already knows how to read and the one their accountant will ask for.
/// </remarks>
/// <param name="VendorId">The seller.</param>
/// <param name="From">The first instant covered.</param>
/// <param name="To">The first instant not covered.</param>
/// <param name="OpeningBalance">What was owed at the start.</param>
/// <param name="ClosingBalance">What was owed at the end.</param>
/// <param name="CurrentBalance">What is owed now, over the whole ledger and not only this window.</param>
/// <param name="Totals">What the movements came to, by kind.</param>
/// <param name="CurrencyCode">ISO 4217 code every amount is in.</param>
/// <param name="Entries">The movements themselves, oldest first.</param>
internal sealed record LedgerStatementResponse(
    Guid VendorId,
    DateTimeOffset From,
    DateTimeOffset To,
    decimal OpeningBalance,
    decimal ClosingBalance,
    decimal CurrentBalance,
    IReadOnlyList<LedgerTotalResponse> Totals,
    string CurrencyCode,
    IReadOnlyList<LedgerEntryResponse> Entries);

/// <summary>What one kind of movement came to over a window.</summary>
/// <param name="EntryType">The kind.</param>
/// <param name="Count">How many of them there were.</param>
/// <param name="Amount">What they came to, unsigned.</param>
/// <param name="SignedAmount">What they moved the balance by.</param>
internal sealed record LedgerTotalResponse(
    LedgerEntryType EntryType,
    int Count,
    decimal Amount,
    decimal SignedAmount);

/// <summary>A settlement period, as an API caller sees it.</summary>
/// <param name="Id">The cycle.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="VendorCode">Their short code, where it could be resolved.</param>
/// <param name="VendorName">Their legal name, where it could be resolved.</param>
/// <param name="PeriodStart">The first instant covered.</param>
/// <param name="PeriodEnd">The first instant not covered.</param>
/// <param name="Status">Where it stands: <c>Open</c>, <c>Closed</c> or <c>Paid</c>.</param>
/// <param name="OpeningBalance">What was carried in.</param>
/// <param name="GrossSales">Supplies in the period, inclusive of tax.</param>
/// <param name="TaxableSales">What those supplies were worth before tax.</param>
/// <param name="TotalCommission">Commission and the GST on the platform's charges.</param>
/// <param name="TotalFees">Marketplace, gateway and freight charges.</param>
/// <param name="TotalRefunds">What came back off the period.</param>
/// <param name="TotalAdjustments">Corrections a human posted, net of direction.</param>
/// <param name="TotalPayouts">Money sent inside the period.</param>
/// <param name="Tcs">Tax collected at source.</param>
/// <param name="Tds">Tax deducted at source.</param>
/// <param name="NetPayable">What the seller is owed for the period.</param>
/// <param name="CurrencyCode">ISO 4217 code every amount is in.</param>
/// <param name="EntryCount">How many ledger entries were drawn in.</param>
/// <param name="ClosedAt">When it was closed.</param>
/// <param name="PaidAt">When money against it left.</param>
/// <param name="PayoutBatchId">The batch it is in, or that paid it.</param>
internal sealed record SettlementCycleResponse(
    Guid Id,
    Guid VendorId,
    string? VendorCode,
    string? VendorName,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    SettlementCycleStatus Status,
    decimal OpeningBalance,
    decimal GrossSales,
    decimal TaxableSales,
    decimal TotalCommission,
    decimal TotalFees,
    decimal TotalRefunds,
    decimal TotalAdjustments,
    decimal TotalPayouts,
    decimal Tcs,
    decimal Tds,
    decimal NetPayable,
    string CurrencyCode,
    int EntryCount,
    DateTimeOffset? ClosedAt,
    DateTimeOffset? PaidAt,
    Guid? PayoutBatchId);

/// <summary>A payout run, as an API caller sees it.</summary>
/// <param name="Id">The batch.</param>
/// <param name="Reference">Its human-readable reference.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="TotalAmount">What it is worth.</param>
/// <param name="SettledAmount">What has actually reached a seller.</param>
/// <param name="VendorCount">How many sellers it pays.</param>
/// <param name="CurrencyCode">ISO 4217 code the amounts are in.</param>
/// <param name="Provider">Which rail sent it.</param>
/// <param name="ProviderBatchId">The gateway's own id for the run.</param>
/// <param name="RequestedBy">Who built it.</param>
/// <param name="RequestedAt">When.</param>
/// <param name="ApprovedBy">Who signed it off.</param>
/// <param name="ApprovedAt">When.</param>
/// <param name="ProcessedAt">When it was handed to the gateway.</param>
/// <param name="CompletedAt">When every transfer had stopped moving.</param>
/// <param name="CancelledReason">Why it was abandoned, when it was.</param>
/// <param name="NextStatuses">
/// What this caller may move it to next. The buttons an admin screen draws come off the transition
/// table rather than out of a developer's head, so a button for an edge that does not exist is
/// impossible rather than merely unlikely.
/// </param>
/// <param name="Items">The transfers in it.</param>
internal sealed record PayoutBatchResponse(
    Guid Id,
    string Reference,
    PayoutBatchStatus Status,
    decimal TotalAmount,
    decimal SettledAmount,
    int VendorCount,
    string CurrencyCode,
    string? Provider,
    string? ProviderBatchId,
    Guid? RequestedBy,
    DateTimeOffset RequestedAt,
    Guid? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? ProcessedAt,
    DateTimeOffset? CompletedAt,
    string? CancelledReason,
    IReadOnlyList<string> NextStatuses,
    IReadOnlyList<PayoutItemResponse> Items);

/// <summary>One seller's transfer within a run.</summary>
/// <param name="Id">The transfer.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="VendorCode">Their short code, frozen when the batch was built.</param>
/// <param name="VendorName">Their legal name, frozen when the batch was built.</param>
/// <param name="SettlementCycleId">The period it discharges.</param>
/// <param name="Amount">What was sent.</param>
/// <param name="CurrencyCode">ISO 4217 code the amount is in.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="DestinationLast4">The last four digits of the account it went to.</param>
/// <param name="ProviderPayoutId">The gateway's id for the transfer.</param>
/// <param name="Utr">The bank's unique transaction reference.</param>
/// <param name="FailureReason">Why it did not go, when it did not.</param>
/// <param name="SentAt">When it was handed to the gateway.</param>
/// <param name="SettledAt">When it stopped moving, either way.</param>
internal sealed record PayoutItemResponse(
    Guid Id,
    Guid VendorId,
    string? VendorCode,
    string? VendorName,
    Guid? SettlementCycleId,
    decimal Amount,
    string CurrencyCode,
    PayoutItemStatus Status,
    string? DestinationLast4,
    string? ProviderPayoutId,
    string? Utr,
    string? FailureReason,
    DateTimeOffset? SentAt,
    DateTimeOffset? SettledAt);

/// <summary>One seller's line of the TCS/TDS extract.</summary>
/// <remarks>
/// The shape a GSTR-8 and a 26Q/27EQ filing both need, on one row, because the two are prepared from
/// the same period by the same person. Both bases are published beside their tax: a return reports
/// the value the tax was taken on as well as the tax, and an extract that gave only the tax would be
/// an extract somebody had to recompute the rest of.
/// </remarks>
/// <param name="VendorId">The seller.</param>
/// <param name="VendorCode">Their short code.</param>
/// <param name="VendorName">Their legal name.</param>
/// <param name="Gstin">Their GST registration, under which the supplies were made.</param>
/// <param name="Pan">Their PAN, which decided the TDS rate.</param>
/// <param name="PeriodStart">The first instant covered.</param>
/// <param name="PeriodEnd">The first instant not covered.</param>
/// <param name="GrossSales">Supplies in the period, inclusive of tax.</param>
/// <param name="Refunds">What came back off them.</param>
/// <param name="NetTaxableSupplies">The TCS base under section 52(1).</param>
/// <param name="Tcs">Tax collected at source.</param>
/// <param name="NetGrossSales">The TDS base under section 194-O.</param>
/// <param name="Tds">Tax deducted at source.</param>
/// <param name="CurrencyCode">ISO 4217 code every amount is in.</param>
internal sealed record StatutoryExtractRow(
    Guid VendorId,
    string? VendorCode,
    string? VendorName,
    string? Gstin,
    string? Pan,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    decimal GrossSales,
    decimal Refunds,
    decimal NetTaxableSupplies,
    decimal Tcs,
    decimal NetGrossSales,
    decimal Tds,
    string CurrencyCode);

/// <summary>The TCS/TDS extract for a period.</summary>
/// <param name="From">The first instant covered.</param>
/// <param name="To">The first instant not covered.</param>
/// <param name="TotalTcs">Tax collected at source across every seller.</param>
/// <param name="TotalTds">Tax deducted at source across every seller.</param>
/// <param name="CurrencyCode">ISO 4217 code every amount is in.</param>
/// <param name="Rows">One line per seller.</param>
internal sealed record StatutoryExtractResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    decimal TotalTcs,
    decimal TotalTds,
    string CurrencyCode,
    IReadOnlyList<StatutoryExtractRow> Rows);

/// <summary>What the platform itself earned over a window.</summary>
/// <remarks>
/// The mirror image of a seller's statement. Every figure here is a debit on somebody's ledger, which
/// is what makes the two reconcile: the platform's revenue is exactly what the sellers were charged,
/// and there is no third place either number could come from.
/// </remarks>
/// <param name="From">The first instant covered.</param>
/// <param name="To">The first instant not covered.</param>
/// <param name="GrossMerchandiseValue">What shoppers paid for goods that were settled in the window.</param>
/// <param name="Commission">Commission charged, exclusive of tax.</param>
/// <param name="PlatformFee">Marketplace fees charged, exclusive of tax.</param>
/// <param name="PaymentFee">Gateway fees charged on to sellers.</param>
/// <param name="ShippingFee">Freight charged back to sellers.</param>
/// <param name="Tax">The GST on the platform's own charges.</param>
/// <param name="Refunds">Supplies reversed in the window.</param>
/// <param name="ChargesReversed">What was given back to sellers with those reversals.</param>
/// <param name="NetRevenue">What the platform kept, after reversals and excluding its own output tax.</param>
/// <param name="Tcs">Tax collected at source, which the platform remits rather than keeps.</param>
/// <param name="Tds">Tax deducted at source, which the platform remits rather than keeps.</param>
/// <param name="PaidOut">What was actually sent to sellers in the window.</param>
/// <param name="CurrencyCode">ISO 4217 code every amount is in.</param>
internal sealed record PlatformRevenueResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    decimal GrossMerchandiseValue,
    decimal Commission,
    decimal PlatformFee,
    decimal PaymentFee,
    decimal ShippingFee,
    decimal Tax,
    decimal Refunds,
    decimal ChargesReversed,
    decimal NetRevenue,
    decimal Tcs,
    decimal Tds,
    decimal PaidOut,
    string CurrencyCode);

/// <summary>What a seller is owed right now, in one small object.</summary>
/// <remarks>
/// What a vendor portal's dashboard tile shows and what a support call answers. Deliberately not a
/// statement: it is three numbers, and building a page of movements to display three numbers is what
/// makes a dashboard slow.
/// </remarks>
/// <param name="VendorId">The seller.</param>
/// <param name="CurrentBalance">Everything on the ledger, credits less debits.</param>
/// <param name="UnsettledBalance">The part of it no cycle has drawn in yet.</param>
/// <param name="AwaitingPayout">The part that is in a closed cycle and waiting to be sent.</param>
/// <param name="CurrencyCode">ISO 4217 code every amount is in.</param>
/// <param name="LastCycleClosedAt">When their last period was closed.</param>
/// <param name="LastPaidAt">When they were last actually paid.</param>
internal sealed record VendorBalanceResponse(
    Guid VendorId,
    decimal CurrentBalance,
    decimal UnsettledBalance,
    decimal AwaitingPayout,
    string CurrencyCode,
    DateTimeOffset? LastCycleClosedAt,
    DateTimeOffset? LastPaidAt);

/// <summary>
/// Turns this module's entities into the shapes its endpoints return.
/// </summary>
/// <remarks>
/// One projection per entity, in one place, so two endpoints cannot return two different shapes for
/// the same row. Nothing here reads the database — a projection that needed a query would be a
/// projection that ran one per row.
/// </remarks>
internal static class SettlementProjection
{
    /// <summary>Projects one ledger entry.</summary>
    /// <param name="entry">The entry.</param>
    public static LedgerEntryResponse ToEntry(LedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new LedgerEntryResponse(
            entry.Id,
            entry.VendorId ?? Guid.Empty,
            LedgerEntryTypes.ToEntryType(entry.EntryType),
            entry.Direction,
            entry.Amount,
            entry.SignedAmount,
            entry.TaxableValue,
            entry.CurrencyCode,
            entry.ReferenceType,
            entry.ReferenceId,
            entry.SubOrderId,
            entry.SettlementCycleId,
            entry.Note,
            entry.OccurredAt);
    }

    /// <summary>Projects one settlement period.</summary>
    /// <param name="cycle">The cycle.</param>
    /// <param name="vendorCode">The seller's short code, where it could be resolved.</param>
    /// <param name="vendorName">The seller's legal name, where it could be resolved.</param>
    public static SettlementCycleResponse ToCycle(
        SettlementCycle cycle,
        string? vendorCode = null,
        string? vendorName = null)
    {
        ArgumentNullException.ThrowIfNull(cycle);

        return new SettlementCycleResponse(
            cycle.Id,
            cycle.VendorId ?? Guid.Empty,
            vendorCode,
            vendorName,
            cycle.PeriodStart,
            cycle.PeriodEnd,
            cycle.Status,
            cycle.OpeningBalance,
            cycle.GrossSales,
            cycle.TaxableSales,
            cycle.TotalCommission,
            cycle.TotalFees,
            cycle.TotalRefunds,
            cycle.TotalAdjustments,
            cycle.TotalPayouts,
            cycle.Tcs,
            cycle.Tds,
            cycle.NetPayable,
            cycle.CurrencyCode,
            cycle.EntryCount,
            cycle.ClosedAt,
            cycle.PaidAt,
            cycle.PayoutBatchId);
    }

    /// <summary>Projects one payout run, with the edges this caller may take from it.</summary>
    /// <param name="batch">The batch.</param>
    /// <param name="actor">Who is asking.</param>
    public static PayoutBatchResponse ToBatch(PayoutBatch batch, PayoutActor actor)
    {
        ArgumentNullException.ThrowIfNull(batch);

        return new PayoutBatchResponse(
            batch.Id,
            batch.Reference,
            batch.Status,
            batch.TotalAmount,
            batch.SettledAmount,
            batch.VendorCount,
            batch.CurrencyCode,
            batch.Provider,
            batch.ProviderBatchId,
            batch.RequestedBy,
            batch.RequestedAt,
            batch.ApprovedBy,
            batch.ApprovedAt,
            batch.ProcessedAt,
            batch.CompletedAt,
            batch.CancelledReason,
            [.. PayoutLifecycle.NextFor(batch.Status, actor).Select(status => status.ToString())],
            [.. batch.Items.Select(ToItem)]);
    }

    /// <summary>Projects one transfer.</summary>
    /// <param name="item">The transfer.</param>
    public static PayoutItemResponse ToItem(PayoutItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new PayoutItemResponse(
            item.Id,
            item.VendorId ?? Guid.Empty,
            item.VendorCode,
            item.VendorName,
            item.SettlementCycleId,
            item.Amount,
            item.CurrencyCode,
            item.Status,
            item.DestinationLast4,
            item.ProviderPayoutId,
            item.Utr,
            item.FailureReason,
            item.SentAt,
            item.SettledAt);
    }
}
