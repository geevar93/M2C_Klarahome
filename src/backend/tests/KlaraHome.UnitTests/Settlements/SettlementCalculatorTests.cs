using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Modules.Settlements.Domain;
using KlaraHome.Modules.Settlements.Infrastructure.Accounting;

namespace KlaraHome.UnitTests.Settlements;

/// <summary>
/// What one delivered sale earns a seller, and what comes off it.
/// </summary>
/// <remarks>
/// Tested while writing it under the build sprint's rule 1. It is the single arithmetic in the module
/// and it is a pure function of its arguments, so it can be got right now for the price of reading
/// it; every other route to the same answer goes through a database, a gateway and a background job.
/// </remarks>
public sealed class SettlementCalculatorTests
{
    /// <summary>
    /// The seller is credited what the shopper paid, and the delivery is charged straight back.
    /// </summary>
    /// <remarks>
    /// Two entries rather than one net figure, because the two answer different questions: what the
    /// sale was worth is a supply and appears on a GST return, and what the courier cost is a charge
    /// and appears on the platform's invoice. Netting them would lose both.
    /// </remarks>
    [Fact]
    public void A_delivered_sale_credits_the_goods_and_the_freight_and_charges_the_freight_back()
    {
        var sale = Sale(Line(quantity: 2, lineTotal: 1180m, taxableValue: 1000m, commission: 100m));
        var policy = new SettlementSettings();

        var postings = SettlementCalculator.Earning(sale, policy);

        var credit = Single(postings, LedgerEntryTypes.Sale);
        Assert.Equal(LedgerDirection.Credit, credit.Direction);

        // 1180 of goods plus 50 of delivery, and the taxable value is the goods' alone.
        Assert.Equal(1230m, credit.Amount);
        Assert.Equal(1000m, credit.TaxableValue);

        Assert.Equal(50m, Single(postings, LedgerEntryTypes.ShippingFee).Amount);
        Assert.Equal(100m, Single(postings, LedgerEntryTypes.Commission).Amount);

        // Eighteen per cent on the commission, which is the platform's own output tax.
        Assert.Equal(18m, Single(postings, LedgerEntryTypes.PlatformTax).Amount);

        // Nothing is posted for a charge that is nil: the marketplace fee and the gateway fee are
        // both zero by default, and a statement full of zero rows is a statement nobody reads.
        Assert.DoesNotContain(postings, posting => posting.EntryType == LedgerEntryTypes.PlatformFee);
        Assert.DoesNotContain(postings, posting => posting.EntryType == LedgerEntryTypes.PaymentFee);
    }

    /// <summary>Only the units that were actually delivered earn anything.</summary>
    /// <remarks>
    /// Pro-rated from the frozen line by quantity, including the commission. A line cancelled in half
    /// earns half and is charged half, and the alternative — crediting the whole line and reversing
    /// the cancelled part separately — would show a seller a sale that never happened.
    /// </remarks>
    [Fact]
    public void A_partly_cancelled_line_earns_only_what_was_delivered()
    {
        var sale = Sale(Line(quantity: 4, lineTotal: 2000m, taxableValue: 1600m, commission: 200m) with
        {
            QuantityCancelled = 1,
        });

        var postings = SettlementCalculator.Earning(sale, new SettlementSettings());

        // Three of four units: 1500 of goods plus 50 of delivery.
        Assert.Equal(1550m, Single(postings, LedgerEntryTypes.Sale).Amount);
        Assert.Equal(1200m, Single(postings, LedgerEntryTypes.Sale).TaxableValue);
        Assert.Equal(150m, Single(postings, LedgerEntryTypes.Commission).Amount);
    }

    /// <summary>
    /// A line frozen with no commission is charged from the resolved quote, or from nothing at all.
    /// </summary>
    /// <remarks>
    /// The honest answer when neither exists is to charge nothing. The platform did not agree a rate
    /// with that seller when the sale was made, and inventing one at settlement would be charging a
    /// fee nobody was told about.
    /// </remarks>
    [Fact]
    public void An_uncommissioned_line_falls_back_to_the_quote_and_otherwise_charges_nothing()
    {
        var sale = Sale(Line(quantity: 2, lineTotal: 1000m, taxableValue: 847.46m, commission: 0m) with
        {
            UnitPrice = 500m,
        });

        var withoutQuote = SettlementCalculator.Earning(sale, new SettlementSettings());
        Assert.DoesNotContain(withoutQuote, posting => posting.EntryType == LedgerEntryTypes.Commission);

        var quote = new CommissionQuote(Guid.CreateVersion7(), "Default", 10m, 5m, MatchedCategoryId: null);
        var withQuote = SettlementCalculator.Earning(sale, new SettlementSettings(), quote);

        // Ten per cent of 1000, plus five rupees a unit on two units.
        Assert.Equal(110m, Single(withQuote, LedgerEntryTypes.Commission).Amount);
    }

    /// <summary>A reversal gives back the charges in proportion, and never the freight.</summary>
    /// <remarks>
    /// The proportion is taken on money rather than on units, because three items are rarely the same
    /// price. The freight is excluded because a parcel that was delivered and then returned was still
    /// delivered: the courier was paid, and refunding a cost the platform actually incurred would make
    /// every return a loss on top of the lost sale.
    /// </remarks>
    [Fact]
    public void A_reversal_returns_the_charges_in_proportion_but_not_the_freight()
    {
        // Half of a 1000-rupee sale on which 118 of charges were taken, freight excluded.
        var postings = SettlementCalculator.Reversal(
            reversedAmount: 500m,
            reversedTaxable: 423.73m,
            originalSale: 1000m,
            reversibleCharges: 118m,
            "Credit note CN/2026-27/00001");

        Assert.Equal(500m, Single(postings, LedgerEntryTypes.Refund).Amount);
        Assert.Equal(423.73m, Single(postings, LedgerEntryTypes.Refund).TaxableValue);
        Assert.Equal(LedgerDirection.Debit, Single(postings, LedgerEntryTypes.Refund).Direction);

        var givenBack = Single(postings, LedgerEntryTypes.RefundCommissionReversal);
        Assert.Equal(59m, givenBack.Amount);
        Assert.Equal(LedgerDirection.Credit, givenBack.Direction);
    }

    /// <summary>
    /// A reversal larger than the sale gives back everything that was charged and no more.
    /// </summary>
    /// <remarks>
    /// A credit note bigger than the sale it credits is a data problem upstream. The honest response
    /// is to reverse everything that was charged rather than hand the seller back more commission than
    /// they ever paid — which is what an unclamped ratio would do.
    /// </remarks>
    [Fact]
    public void A_reversal_never_gives_back_more_than_was_charged()
    {
        var postings = SettlementCalculator.Reversal(
            reversedAmount: 5000m,
            reversedTaxable: 4237.29m,
            originalSale: 1000m,
            reversibleCharges: 118m,
            "Credit note CN/2026-27/00002");

        Assert.Equal(118m, Single(postings, LedgerEntryTypes.RefundCommissionReversal).Amount);
    }

    /// <summary>A sale worth nothing posts nothing at all.</summary>
    /// <remarks>
    /// A wholly cancelled sub-order that somehow reaches delivery is not an error worth failing an
    /// event handler over; it is a fact about which there is nothing to say.
    /// </remarks>
    [Fact]
    public void A_sale_worth_nothing_posts_nothing()
    {
        var sale = Sale(Line(quantity: 2, lineTotal: 1000m, taxableValue: 847m, commission: 100m) with
        {
            QuantityCancelled = 2,
        }) with
        {
            ShippingTotal = 0m,
        };

        Assert.Empty(SettlementCalculator.Earning(sale, new SettlementSettings()));
        Assert.Empty(SettlementCalculator.Reversal(0m, 0m, 1000m, 100m, "nothing"));
    }

    /// <summary>The one posting of a given type, which is how many there should be.</summary>
    private static LedgerPosting Single(IReadOnlyList<LedgerPosting> postings, string entryType)
        => postings.Single(posting => posting.EntryType == entryType);

    /// <summary>A sub-order with fifty rupees of delivery on it and the given lines.</summary>
    private static SubOrderSettlementView Sale(params SettleableLine[] lines)
        => new(
            Guid.CreateVersion7(),
            "ORD-2609-000001",
            Guid.CreateVersion7(),
            "ORD-2609-000001-1",
            Guid.CreateVersion7(),
            "ACME",
            "Acme Trading Private Limited",
            "36AAAAA0000A1Z5",
            Guid.CreateVersion7(),
            "Delivered",
            "Prepaid",
            IsPaid: true,
            ItemsTotal: lines.Sum(line => line.LineTotal),
            DiscountTotal: 0m,
            ShippingTotal: 50m,
            ShippingTax: 7.63m,
            TaxableValue: lines.Sum(line => line.TaxableValue),
            TaxTotal: lines.Sum(line => line.TaxAmount),
            Total: lines.Sum(line => line.LineTotal) + 50m,
            "INR",
            DateTimeOffset.UtcNow.AddDays(-5),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(7),
            InvoiceId: null,
            InvoiceNumber: null,
            FinancialYear: "2026-27",
            lines);

    /// <summary>One line, with the commission frozen on it exactly as an order line carries it.</summary>
    private static SettleableLine Line(int quantity, decimal lineTotal, decimal taxableValue, decimal commission)
        => new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            CategoryId: null,
            "SKU-1",
            "A lamp",
            quantity,
            QuantityCancelled: 0,
            QuantityReturned: 0,
            UnitPrice: lineTotal / quantity,
            taxableValue,
            TaxAmount: lineTotal - taxableValue,
            lineTotal,
            CommissionRate: 10m,
            commission,
            CommissionPlanId: null);
}
