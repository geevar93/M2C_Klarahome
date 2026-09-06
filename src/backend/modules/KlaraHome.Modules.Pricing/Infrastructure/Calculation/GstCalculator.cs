namespace KlaraHome.Modules.Pricing.Infrastructure.Calculation;

/// <summary>
/// One amount, broken into the figures an Indian tax invoice has to show.
/// </summary>
/// <remarks>
/// The invariant that makes this type worth having: <see cref="TaxableValue"/> plus
/// <see cref="Cess"/> plus <see cref="TaxTotal"/> equals the gross it was computed from, exactly,
/// at two decimal places. Every rounding decision below exists to keep that true, because an
/// invoice whose parts do not add up to its total is one a GST auditor rejects.
/// </remarks>
/// <param name="TaxableValue">The value the tax was computed on, with the tax taken back out of the gross.</param>
/// <param name="Cgst">Central GST. Zero on an inter-state supply.</param>
/// <param name="Sgst">State GST. Zero on an inter-state supply.</param>
/// <param name="Igst">Integrated GST. Zero on an intra-state supply.</param>
/// <param name="Cess">Compensation cess.</param>
internal readonly record struct TaxSplit(
    decimal TaxableValue,
    decimal Cgst,
    decimal Sgst,
    decimal Igst,
    decimal Cess)
{
    /// <summary>Every tax figure together.</summary>
    public decimal TaxTotal => Cgst + Sgst + Igst + Cess;

    /// <summary>The gross this split came from: the taxable value plus all of the tax.</summary>
    public decimal Gross => TaxableValue + TaxTotal;

    /// <summary>A supply with nothing on it — a zero line, or a zero shipping charge.</summary>
    public static TaxSplit Zero { get; }
}

/// <summary>
/// The GST engine (docs/02-domain-model.md §7).
/// </summary>
/// <remarks>
/// <para>
/// Prices in Indian retail are quoted <b>inclusive</b> of GST, so every figure here is
/// back-calculated out of the gross rather than added to it. That is not a presentation choice: a
/// shopper who is shown ₹999 pays ₹999, and the tax is whatever was already inside it.
/// </para>
/// <para>
/// Deliberately static and free of every dependency — no clock, no context, no database. The GST
/// arithmetic is the one part of this platform that has a right answer independent of anything
/// else, and keeping it that way is what makes it testable line by line, which under the build
/// sprint's rule 1 is the only kind of test this step writes.
/// </para>
/// </remarks>
internal static class GstCalculator
{
    /// <summary>Money is presented and invoiced at two decimal places.</summary>
    /// <remarks>
    /// The database stores four (docs/03-database-design.md §1) and intermediate arithmetic keeps
    /// them; this is the scale at which a figure becomes a number on a document, and rounding at
    /// any other point is how two systems end up a paisa apart.
    /// </remarks>
    public const int MoneyScale = 2;

    /// <summary>
    /// Rounds a monetary figure the way an Indian invoice does: half away from zero.
    /// </summary>
    /// <remarks>
    /// Not banker's rounding, which is .NET's default and would round 2.5 to 2. Indian commercial
    /// practice and every accounting package a shopkeeper reconciles against round half up, and
    /// being right by a different convention is still being different.
    /// </remarks>
    /// <param name="value">The figure.</param>
    public static decimal Round(decimal value) => decimal.Round(value, MoneyScale, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Splits a tax-inclusive gross into its taxable value, its GST and its cess.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The taxable value is <c>gross / (1 + (rate + cess) / 100)</c>: both levies sit on the same
    /// base, so both belong in the divisor. Getting that wrong overstates the taxable value on
    /// every cess-bearing line, which is the classic way a marketplace under-remits.
    /// </para>
    /// <para>
    /// The residue is assigned rather than recomputed — cess is rounded, and GST is then whatever
    /// is left between the rounded taxable value and the gross. That is what guarantees the parts
    /// add up. For an intra-state supply CGST takes the rounded half and SGST takes the remainder,
    /// so a tax of ₹1.01 splits 0.51/0.50 rather than 0.51/0.51.
    /// </para>
    /// </remarks>
    /// <param name="grossInclusive">The amount the shopper pays, tax already inside it.</param>
    /// <param name="gstRate">The GST percentage, for example <c>18.0000</c>.</param>
    /// <param name="cessRate">The compensation cess percentage. Usually zero.</param>
    /// <param name="isIntraState">Whether the place of supply matches the supplier's state.</param>
    public static TaxSplit SplitInclusive(
        decimal grossInclusive,
        decimal gstRate,
        decimal cessRate,
        bool isIntraState)
    {
        var gross = Round(grossInclusive);

        if (gross == 0m)
        {
            return TaxSplit.Zero;
        }

        var rate = Math.Max(0m, gstRate);
        var cess = Math.Max(0m, cessRate);
        var divisor = 1m + ((rate + cess) / 100m);

        if (divisor <= 1m)
        {
            // Nothing is levied. The whole amount is the taxable value, which is what a nil-rated
            // or exempt supply looks like — and it is not the same as a zero-value line.
            return new TaxSplit(gross, 0m, 0m, 0m, 0m);
        }

        var taxableValue = Round(gross / divisor);
        var cessAmount = Round(taxableValue * cess / 100m);
        var gstAmount = gross - taxableValue - cessAmount;

        if (!isIntraState)
        {
            return new TaxSplit(taxableValue, 0m, 0m, gstAmount, cessAmount);
        }

        var cgst = Round(gstAmount / 2m);

        return new TaxSplit(taxableValue, cgst, gstAmount - cgst, 0m, cessAmount);
    }

    /// <summary>
    /// Rounds a grand total to a whole rupee and reports what that cost.
    /// </summary>
    /// <remarks>
    /// Section 170 of the CGST Act rounds the amount payable to the nearest rupee. The adjustment
    /// is returned rather than absorbed because it is a line on the invoice: a total that silently
    /// disagrees with the sum above it is the single most common complaint about a badly built
    /// checkout.
    /// </remarks>
    /// <param name="value">The computed total.</param>
    /// <returns>The rounded total, and what was added to or taken off it.</returns>
    public static (decimal Total, decimal Adjustment) RoundToRupee(decimal value)
    {
        var rounded = decimal.Round(value, 0, MidpointRounding.AwayFromZero);

        return (rounded, Round(rounded - value));
    }

    /// <summary>
    /// The GST state code inside a GSTIN — its first two characters.
    /// </summary>
    /// <remarks>
    /// The place-of-supply rule needs the supplier's state, and a GSTIN carries it by construction:
    /// <c>27</c> is Maharashtra, <c>29</c> Karnataka. Reading it here rather than storing a second
    /// copy on the seller means the two can never disagree.
    /// </remarks>
    /// <param name="gstin">The registration number, or null for an unregistered supplier.</param>
    public static string? StateCodeOf(string? gstin)
        => string.IsNullOrWhiteSpace(gstin) || gstin.Length < 2 ? null : gstin[..2];

    /// <summary>
    /// Whether a supply is intra-state: CGST + SGST if it is, IGST if it is not.
    /// </summary>
    /// <remarks>
    /// When either code is unknown the answer is <b>intra-state</b>. A shopper with no address yet
    /// is being shown a price on a product page, and the store's own state is the only defensible
    /// assumption; the figure is re-quoted the moment they enter an address, and Orders freezes
    /// that one rather than this.
    /// </remarks>
    /// <param name="placeOfSupplyStateCode">The GST code of the shipping address's state.</param>
    /// <param name="supplierStateCode">The GST code of the seller's registration.</param>
    public static bool IsIntraState(string? placeOfSupplyStateCode, string? supplierStateCode)
        => string.IsNullOrWhiteSpace(placeOfSupplyStateCode)
           || string.IsNullOrWhiteSpace(supplierStateCode)
           || string.Equals(placeOfSupplyStateCode, supplierStateCode, StringComparison.Ordinal);

    /// <summary>
    /// Splits an amount across weights so the parts sum to the whole, exactly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is how an order-level discount reaches the lines that have to carry its tax effect.
    /// Rounding each share independently loses or gains paise, and the loss lands on the tax
    /// figures, so the largest share absorbs the residue. Largest rather than first, because a
    /// one-paisa correction is least visible on the biggest line.
    /// </para>
    /// <para>
    /// A zero total weight allocates nothing, which is right: there is nothing to allocate across.
    /// </para>
    /// </remarks>
    /// <param name="amount">What to split.</param>
    /// <param name="weights">The relative shares, one per part.</param>
    public static decimal[] AllocateProportionally(decimal amount, IReadOnlyList<decimal> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);

        var parts = new decimal[weights.Count];

        if (weights.Count == 0 || amount == 0m)
        {
            return parts;
        }

        var total = 0m;
        var largestIndex = 0;

        for (var index = 0; index < weights.Count; index++)
        {
            total += weights[index];

            if (weights[index] > weights[largestIndex])
            {
                largestIndex = index;
            }
        }

        if (total <= 0m)
        {
            return parts;
        }

        var target = Round(amount);
        var assigned = 0m;

        for (var index = 0; index < weights.Count; index++)
        {
            parts[index] = Round(target * weights[index] / total);
            assigned += parts[index];
        }

        parts[largestIndex] += target - assigned;

        return parts;
    }
}
