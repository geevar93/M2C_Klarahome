using KlaraHome.Contracts.Platform;

namespace KlaraHome.Modules.Settlements.Infrastructure.Accounting;

/// <summary>What the state takes out of a period, and the basis each figure was taken on.</summary>
/// <remarks>
/// The bases are carried alongside the amounts because the extract has to publish them. A GSTR-8
/// return reports the net value of taxable supplies as well as the tax collected on it, and a
/// 26Q/27EQ statement reports the amount paid as well as the tax deducted — an extract that gave only
/// the tax would be an extract somebody had to recompute the rest of.
/// </remarks>
/// <param name="Tcs">Tax collected at source under section 52 of the CGST Act.</param>
/// <param name="TcsBase">The net value of taxable supplies it was collected on.</param>
/// <param name="TcsRate">The rate applied, as a percentage.</param>
/// <param name="Tds">Tax deducted at source under section 194-O of the Income-tax Act.</param>
/// <param name="TdsBase">The gross amount of sales it was deducted from.</param>
/// <param name="TdsRate">The rate applied, as a percentage.</param>
/// <param name="TdsWithheldReason">
/// Why no tax was deducted, when none was: below the annual threshold, or switched off. Null when
/// tax was deducted.
/// </param>
internal sealed record StatutoryDeduction(
    decimal Tcs,
    decimal TcsBase,
    decimal TcsRate,
    decimal Tds,
    decimal TdsBase,
    decimal TdsRate,
    string? TdsWithheldReason)
{
    /// <summary>A period on which nothing was collected or deducted.</summary>
    public static readonly StatutoryDeduction None = new(0m, 0m, 0m, 0m, 0m, 0m, null);

    /// <summary>Both deductions together, which is what comes off the seller's net.</summary>
    public decimal Total => Tcs + Tds;
}

/// <summary>
/// The two deductions an Indian marketplace is obliged to make
/// (docs/02-domain-model.md §7.3).
/// </summary>
/// <remarks>
/// <para>
/// They are computed together and they are computed from two different bases, which is the single
/// most common way this arithmetic goes wrong. <b>TCS under section 52 of the CGST Act</b> is
/// collected on the <em>net value of taxable supplies</em> — the consideration for the supply, which
/// excludes the GST inside the price, reduced by supplies returned. <b>TDS under section 194-O of the
/// Income-tax Act</b> is deducted from the <em>gross amount of sales</em>, which the CBDT's own
/// circular says includes the GST. On the same period, on the same seller, the two figures are
/// therefore taken on different numbers, and a statement that showed them on one would be wrong twice.
/// </para>
/// <para>
/// Both rates are settings rather than constants. They are set by a notification in the Gazette and
/// have both moved since the provisions were introduced; a deployment that needed a rebuild to follow
/// a rate change would file a wrong return in the meantime.
/// </para>
/// <para>
/// Pure arithmetic, like the settlement calculator beside it: it takes the period's figures, the
/// seller's PAN status and their running total for the financial year, and returns numbers. It does
/// not know what a cycle is and cannot read one.
/// </para>
/// </remarks>
internal static class StatutoryDeductions
{
    /// <summary>What was not deducted because the seller is under the annual threshold.</summary>
    public const string BelowThreshold = "below-annual-threshold";

    /// <summary>What was not deducted because the store has the deduction switched off.</summary>
    public const string Disabled = "disabled";

    /// <summary>
    /// What the state takes out of one seller's period.
    /// </summary>
    /// <param name="netTaxableSupplies">
    /// Taxable supplies in the period less supplies returned. The TCS base under section 52(1).
    /// </param>
    /// <param name="netGrossSales">
    /// Gross sales in the period less what came back, inclusive of GST. The TDS base under
    /// section 194-O.
    /// </param>
    /// <param name="yearToDateGrossSales">
    /// The seller's gross sales in the financial year so far, <em>including</em> this period. Read by
    /// the annual threshold, which is a test on the year and not on the period: a seller who crosses
    /// it in week forty is deducted from then on, not retrospectively.
    /// </param>
    /// <param name="hasPan">
    /// Whether the seller has furnished a permanent account number. Section 206AA requires a higher
    /// rate where they have not, which is why it is a separate rate and not a multiple.
    /// </param>
    /// <param name="policy">The store's settlement policy, which carries both rates.</param>
    public static StatutoryDeduction For(
        decimal netTaxableSupplies,
        decimal netGrossSales,
        decimal yearToDateGrossSales,
        bool hasPan,
        SettlementSettings policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var tcsBase = Math.Max(0m, netTaxableSupplies);
        var tdsBase = Math.Max(0m, netGrossSales);

        var tcsRate = policy.TcsEnabled ? policy.TcsRatePercent : 0m;
        var tcs = SettlementCalculator.Round(tcsBase * tcsRate / 100m);

        var tdsRate = hasPan ? policy.TdsRatePercent : policy.TdsRateWithoutPanPercent;

        var withheld = !policy.TdsEnabled
            ? Disabled
            : policy.TdsAnnualThreshold > 0m && yearToDateGrossSales <= policy.TdsAnnualThreshold
                ? BelowThreshold
                : null;

        var tds = withheld is null
            ? SettlementCalculator.Round(tdsBase * tdsRate / 100m)
            : 0m;

        return new StatutoryDeduction(
            tcs,
            tcsBase,
            tcsRate,
            tds,
            tdsBase,
            withheld is null ? tdsRate : 0m,
            withheld);
    }
}
