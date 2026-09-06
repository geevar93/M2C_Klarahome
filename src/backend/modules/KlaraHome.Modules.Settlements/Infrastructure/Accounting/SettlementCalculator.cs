using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Modules.Settlements.Domain;

namespace KlaraHome.Modules.Settlements.Infrastructure.Accounting;

/// <summary>One movement the calculator says should be posted.</summary>
/// <remarks>
/// Deliberately not a <see cref="LedgerEntry"/>. The calculator is arithmetic and nothing else — it
/// reads no database, writes no row and has no clock — and keeping its output a plain description
/// is what makes it testable without any of those.
/// </remarks>
/// <param name="EntryType">What kind of movement: one of <see cref="LedgerEntryTypes"/>.</param>
/// <param name="Direction">Which way it moves the balance.</param>
/// <param name="Amount">How much, always positive.</param>
/// <param name="TaxableValue">What the tax on it was computed on, or zero where it had none.</param>
/// <param name="Note">What it is, in the words a statement shows.</param>
internal sealed record LedgerPosting(
    string EntryType,
    LedgerDirection Direction,
    decimal Amount,
    decimal TaxableValue,
    string Note);

/// <summary>What the platform charged on one sale, before the statutory deductions.</summary>
/// <param name="Commission">The category commission, exclusive of the GST on it.</param>
/// <param name="PlatformFee">The marketplace fee, exclusive of the GST on it.</param>
/// <param name="PaymentFee">The gateway's cut, exclusive of the GST on it, where the seller bears it.</param>
/// <param name="ShippingFee">The freight charged back, which carries its own tax already.</param>
/// <param name="Tax">The GST on the first three together, which the platform invoices for.</param>
internal sealed record SettlementCharges(
    decimal Commission,
    decimal PlatformFee,
    decimal PaymentFee,
    decimal ShippingFee,
    decimal Tax)
{
    /// <summary>Nothing charged at all.</summary>
    public static readonly SettlementCharges None = new(0m, 0m, 0m, 0m, 0m);

    /// <summary>Everything the platform kept out of the sale.</summary>
    public decimal Total => Commission + PlatformFee + PaymentFee + ShippingFee + Tax;

    /// <summary>
    /// Everything except the freight — what is given back when goods come back.
    /// </summary>
    /// <remarks>
    /// The freight is deliberately excluded. A parcel that was delivered and then returned was still
    /// delivered: the courier was paid, and giving the seller back a cost the platform actually
    /// incurred would make every return a small loss for the platform on top of the lost sale.
    /// </remarks>
    public decimal Reversible => Commission + PlatformFee + PaymentFee + Tax;
}

/// <summary>
/// What one delivered sale earns a seller, and what comes off it.
/// </summary>
/// <remarks>
/// <para>
/// The single arithmetic in this module, and it is deliberately a pure function of its arguments: a
/// view of the sale, the store's settlement policy, and — only where a line was frozen without one —
/// a commission quote. No clock, no database, no configuration read inside it. That is what lets the
/// same code answer "what will this earn me" on a seller's dashboard and "what did this earn them"
/// in the settlement run without the two being able to disagree.
/// </para>
/// <para>
/// <b>Commission is read, never recomputed.</b> What the platform charges was resolved from the
/// seller's plan when the order was placed and frozen onto the order line. Re-resolving it here would
/// charge last month's sale at today's rate, which is the one thing a settlement statement must never
/// do. The resolver is consulted only for a line that carries no commission at all — a listing sold
/// before its seller had a plan — and that case is recorded rather than hidden.
/// </para>
/// <para>
/// <b>The seller is credited what the shopper paid, delivery included, and the delivery is then
/// charged back.</b> Two entries rather than one net figure, because the two answer different
/// questions: what the sale was worth is a supply and appears on a GST return, and what the courier
/// cost is a charge and appears on the platform's invoice. Netting them would lose both.
/// </para>
/// <para>
/// Every amount is rounded to two decimal places, half away from zero, at the moment it becomes an
/// entry. Bankers' rounding is right for a long series of independent figures and wrong for a charge
/// somebody is going to be shown: a seller reading 12.345 charged as 12.34 on one statement and 12.35
/// on the next has found a bug, whatever the arithmetic says.
/// </para>
/// </remarks>
internal static class SettlementCalculator
{
    /// <summary>How many decimal places money is rounded to before it becomes an entry.</summary>
    public const int MoneyPrecision = 2;

    /// <summary>
    /// What a delivered sub-order posts to its seller's account.
    /// </summary>
    /// <remarks>
    /// The units counted are those actually delivered — ordered less cancelled. Returns are not
    /// deducted here even when some have already been recorded: a return that has happened posts its
    /// own reversal, and netting it into the original credit would leave the ledger unable to say
    /// what was sold and what came back.
    /// </remarks>
    /// <param name="sale">The sub-order as the ordering module describes it.</param>
    /// <param name="policy">The store's settlement policy.</param>
    /// <param name="fallback">
    /// The commission to apply to a line frozen without one, or null when none could be resolved.
    /// </param>
    public static IReadOnlyList<LedgerPosting> Earning(
        SubOrderSettlementView sale,
        SettlementSettings policy,
        CommissionQuote? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentNullException.ThrowIfNull(policy);

        var goods = GoodsValue(sale);
        var shipping = Round(sale.ShippingTotal);
        var credited = Round(goods.Gross + shipping);

        if (credited <= 0m)
        {
            return [];
        }

        var charges = Charge(sale, goods, policy, fallback);
        var postings = new List<LedgerPosting>(6)
        {
            new(
                LedgerEntryTypes.Sale,
                LedgerDirection.Credit,
                credited,
                goods.Taxable,
                $"Sale on {sale.SubOrderNumber}"),
        };

        AddCharges(postings, charges, sale.SubOrderNumber);

        return postings;
    }

    /// <summary>
    /// What a reversal posts: the supply that came back, and the charges that come back with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Proportional to what is being reversed rather than recomputed from the sale, and the
    /// proportion is taken on money rather than on units. A shopper returning one of three items has
    /// not necessarily returned a third of the value — three items are rarely the same price — and a
    /// reversal computed on quantity would give back the wrong commission every time.
    /// </para>
    /// <para>
    /// The ratio is clamped to one. A credit note larger than the sale it credits is a data problem
    /// somewhere upstream, and the honest response is to reverse everything that was charged rather
    /// than to hand the seller back more commission than they ever paid.
    /// </para>
    /// </remarks>
    /// <param name="reversedAmount">What is coming off, inclusive of tax.</param>
    /// <param name="reversedTaxable">What that was worth before tax.</param>
    /// <param name="originalSale">What the sale was credited at.</param>
    /// <param name="reversibleCharges">What was charged on it, freight excluded.</param>
    /// <param name="note">What the reversal is, in the words a statement shows.</param>
    public static IReadOnlyList<LedgerPosting> Reversal(
        decimal reversedAmount,
        decimal reversedTaxable,
        decimal originalSale,
        decimal reversibleCharges,
        string note)
    {
        var amount = Round(Math.Max(0m, reversedAmount));

        if (amount <= 0m)
        {
            return [];
        }

        var postings = new List<LedgerPosting>(2)
        {
            new(
                LedgerEntryTypes.Refund,
                LedgerDirection.Debit,
                amount,
                Round(Math.Max(0m, reversedTaxable)),
                note),
        };

        var ratio = originalSale > 0m ? Math.Min(1m, amount / originalSale) : 0m;
        var givenBack = Round(reversibleCharges * ratio);

        if (givenBack > 0m)
        {
            postings.Add(new LedgerPosting(
                LedgerEntryTypes.RefundCommissionReversal,
                LedgerDirection.Credit,
                givenBack,
                TaxableValue: 0m,
                $"Charges reversed on {note}"));
        }

        return postings;
    }

    /// <summary>
    /// What the platform keeps out of one sale.
    /// </summary>
    /// <remarks>
    /// Exposed separately from <see cref="Earning"/> because a seller's dashboard asks exactly this
    /// question about a listing they have not sold yet, and because the reversal needs the same
    /// figures to give a proportion of them back.
    /// </remarks>
    /// <param name="sale">The sub-order.</param>
    /// <param name="goods">What the goods on it came to.</param>
    /// <param name="policy">The store's settlement policy.</param>
    /// <param name="fallback">The commission for a line frozen without one.</param>
    public static SettlementCharges Charge(
        SubOrderSettlementView sale,
        GoodsValue goods,
        SettlementSettings policy,
        CommissionQuote? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentNullException.ThrowIfNull(goods);
        ArgumentNullException.ThrowIfNull(policy);

        var commission = Round(Commission(sale, fallback));

        var platformFee = Round(
            (goods.Gross * Percent(policy.PlatformFeePercent)) + policy.PlatformFeeFixed);

        var paymentFee = policy.ChargeGatewayFeeToVendor
            ? Round((goods.Gross + sale.ShippingTotal) * Percent(policy.PaymentGatewayFeePercent))
            : 0m;

        var shippingFee = policy.ChargeShippingToVendor ? Round(sale.ShippingTotal) : 0m;

        // One tax figure over the three service charges, because the platform raises one tax invoice
        // for them. The freight is excluded: what was charged for delivery already carries the GST on
        // freight, and taxing it again here would tax it twice.
        var tax = Round((commission + platformFee + paymentFee) * Percent(policy.PlatformServiceGstRate));

        return new SettlementCharges(commission, platformFee, paymentFee, shippingFee, tax);
    }

    /// <summary>
    /// What the goods on a sub-order came to, counting only the units that were delivered.
    /// </summary>
    /// <remarks>
    /// Pro-rated by quantity from the frozen line, because a line's total is for the whole line and a
    /// partly cancelled line supplied only part of it. The taxable value is pro-rated the same way
    /// rather than back-calculated from a rate: the rate on the line is a percentage and the money is
    /// what was actually charged, and re-deriving one from the other reintroduces a rounding the
    /// invoice has already resolved.
    /// </remarks>
    /// <param name="sale">The sub-order.</param>
    public static GoodsValue GoodsValue(SubOrderSettlementView sale)
    {
        ArgumentNullException.ThrowIfNull(sale);

        var gross = 0m;
        var taxable = 0m;

        foreach (var line in sale.Lines)
        {
            var delivered = Math.Max(0, line.Quantity - line.QuantityCancelled);

            if (delivered == 0 || line.Quantity == 0)
            {
                continue;
            }

            var share = delivered == line.Quantity ? 1m : (decimal)delivered / line.Quantity;

            gross += line.LineTotal * share;
            taxable += line.TaxableValue * share;
        }

        return new GoodsValue(Round(gross), Round(taxable));
    }

    /// <summary>
    /// The commission on the delivered units of a sub-order.
    /// </summary>
    /// <remarks>
    /// Frozen first, resolved second. A line that carries a commission amount is charged that amount,
    /// pro-rated for cancellations; a line that carries none is charged from the quote the caller
    /// resolved, if it resolved one at all. A line with neither is charged nothing, which is the
    /// honest answer — the platform did not agree a rate with that seller when the sale was made, and
    /// inventing one now would be charging a fee nobody was told about.
    /// </remarks>
    private static decimal Commission(SubOrderSettlementView sale, CommissionQuote? fallback)
    {
        var total = 0m;

        foreach (var line in sale.Lines)
        {
            var delivered = Math.Max(0, line.Quantity - line.QuantityCancelled);

            if (delivered == 0 || line.Quantity == 0)
            {
                continue;
            }

            if (line.CommissionAmount > 0m)
            {
                var share = delivered == line.Quantity ? 1m : (decimal)delivered / line.Quantity;
                total += line.CommissionAmount * share;
                continue;
            }

            if (fallback is null)
            {
                continue;
            }

            var lineValue = line.UnitPrice * delivered;
            total += (lineValue * Percent(fallback.RatePercent)) + (fallback.FixedFee * delivered);
        }

        return total;
    }

    /// <summary>Turns the charges into the entries a statement shows, skipping the ones that are nil.</summary>
    private static void AddCharges(List<LedgerPosting> postings, SettlementCharges charges, string subOrderNumber)
    {
        Add(LedgerEntryTypes.Commission, charges.Commission, $"Commission on {subOrderNumber}");
        Add(LedgerEntryTypes.PlatformFee, charges.PlatformFee, $"Marketplace fee on {subOrderNumber}");
        Add(LedgerEntryTypes.PaymentFee, charges.PaymentFee, $"Payment gateway fee on {subOrderNumber}");
        Add(LedgerEntryTypes.ShippingFee, charges.ShippingFee, $"Delivery cost on {subOrderNumber}");
        Add(LedgerEntryTypes.PlatformTax, charges.Tax, $"GST on platform charges for {subOrderNumber}");

        void Add(string entryType, decimal amount, string note)
        {
            if (amount > 0m)
            {
                postings.Add(new LedgerPosting(entryType, LedgerDirection.Debit, amount, 0m, note));
            }
        }
    }

    /// <summary>A percentage as the fraction it multiplies by.</summary>
    private static decimal Percent(decimal rate) => rate / 100m;

    /// <summary>
    /// Money as it is stored: two places, half away from zero.
    /// </summary>
    /// <remarks>
    /// The column holds four decimal places and this rounds to two on purpose. The extra places exist
    /// so an intermediate calculation does not lose precision; a figure that reaches a statement has
    /// stopped being intermediate, and a seller cannot be paid a hundredth of a paisa.
    /// </remarks>
    public static decimal Round(decimal amount)
        => Math.Round(amount, MoneyPrecision, MidpointRounding.AwayFromZero);
}

/// <summary>What the goods on a sub-order came to, gross and before tax.</summary>
/// <param name="Gross">What the shopper paid for them, inclusive of tax.</param>
/// <param name="Taxable">What the seller's GST was computed on.</param>
internal sealed record GoodsValue(decimal Gross, decimal Taxable)
{
    /// <summary>Nothing at all.</summary>
    public static readonly GoodsValue Empty = new(0m, 0m);
}
