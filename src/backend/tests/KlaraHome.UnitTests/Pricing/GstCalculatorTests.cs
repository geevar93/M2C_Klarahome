using KlaraHome.Modules.Pricing.Infrastructure.Calculation;

namespace KlaraHome.UnitTests.Pricing;

/// <summary>
/// The GST engine (docs/02-domain-model.md §7, docs/03-database-design.md §4.6).
/// </summary>
/// <remarks>
/// Tested while writing it under the build sprint's rule 1. This is the arithmetic every invoice
/// this platform issues is built from, it has one right answer independent of everything else, and
/// its failure mode is not an exception — it is a tax return that is quietly wrong. The full
/// golden-file suite of ~30 scenarios the card asks for is Step 29's; what is here is the set of
/// cases that had to be right for the code to be written at all.
/// </remarks>
public sealed class GstCalculatorTests
{
    [Fact]
    public void Tax_is_taken_out_of_an_inclusive_price_not_added_to_it()
    {
        // Indian retail quotes tax-inclusive: a shopper shown 1180 pays 1180, and the tax is
        // whatever was already inside it.
        var split = GstCalculator.SplitInclusive(1180m, gstRate: 18m, cessRate: 0m, isIntraState: false);

        Assert.Equal(1000m, split.TaxableValue);
        Assert.Equal(180m, split.Igst);
        Assert.Equal(1180m, split.Gross);
    }

    [Fact]
    public void An_intra_state_supply_splits_the_tax_into_cgst_and_sgst()
    {
        var split = GstCalculator.SplitInclusive(1180m, gstRate: 18m, cessRate: 0m, isIntraState: true);

        Assert.Equal(90m, split.Cgst);
        Assert.Equal(90m, split.Sgst);
        Assert.Equal(0m, split.Igst);
        Assert.Equal(180m, split.TaxTotal);
    }

    [Fact]
    public void An_inter_state_supply_charges_the_whole_rate_as_igst()
    {
        var split = GstCalculator.SplitInclusive(1180m, gstRate: 18m, cessRate: 0m, isIntraState: false);

        Assert.Equal(0m, split.Cgst);
        Assert.Equal(0m, split.Sgst);
        Assert.Equal(180m, split.Igst);
    }

    [Fact]
    public void Cess_sits_on_the_same_taxable_value_as_gst_and_belongs_in_the_divisor()
    {
        // 1000 + 28% + 12% = 1400. Dividing by 1.28 instead of 1.40 would overstate the taxable
        // value by nearly 10%, which is the classic way a marketplace under-remits.
        var split = GstCalculator.SplitInclusive(1400m, gstRate: 28m, cessRate: 12m, isIntraState: false);

        Assert.Equal(1000m, split.TaxableValue);
        Assert.Equal(120m, split.Cess);
        Assert.Equal(280m, split.Igst);
        Assert.Equal(1400m, split.Gross);
    }

    [Theory]
    [InlineData(999.99, 18)]
    [InlineData(1.01, 12)]
    [InlineData(4567.89, 5)]
    [InlineData(333.33, 28)]
    [InlineData(0.03, 18)]
    public void The_parts_always_add_back_up_to_the_gross(decimal gross, decimal rate)
    {
        // The invariant the whole class exists to keep. An invoice whose lines do not sum to its
        // total is one a GST auditor rejects, whatever the individual figures say.
        var intra = GstCalculator.SplitInclusive(gross, rate, cessRate: 0m, isIntraState: true);
        var inter = GstCalculator.SplitInclusive(gross, rate, cessRate: 0m, isIntraState: false);

        Assert.Equal(GstCalculator.Round(gross), intra.Gross);
        Assert.Equal(GstCalculator.Round(gross), inter.Gross);
    }

    [Fact]
    public void An_odd_paisa_of_tax_goes_to_cgst_so_the_halves_still_sum_exactly()
    {
        // A tax of 1.01 must split 0.51 / 0.50, not 0.51 / 0.51. Rounding both halves the same way
        // would invent a paisa on every line that landed here.
        var split = GstCalculator.SplitInclusive(6.63m, gstRate: 18m, cessRate: 0m, isIntraState: true);

        Assert.Equal(split.TaxTotal, split.Cgst + split.Sgst);
        Assert.Equal(GstCalculator.Round(6.63m), split.Gross);
        Assert.True(split.Cgst >= split.Sgst);
    }

    [Fact]
    public void A_nil_rated_supply_is_all_taxable_value_and_no_tax()
    {
        var split = GstCalculator.SplitInclusive(500m, gstRate: 0m, cessRate: 0m, isIntraState: true);

        Assert.Equal(500m, split.TaxableValue);
        Assert.Equal(0m, split.TaxTotal);
    }

    [Fact]
    public void A_zero_value_line_produces_nothing_at_all()
    {
        var split = GstCalculator.SplitInclusive(0m, gstRate: 18m, cessRate: 0m, isIntraState: true);

        Assert.Equal(TaxSplit.Zero, split);
    }

    [Theory]
    [InlineData(2.5, 3, 0.5)]
    [InlineData(2.4, 2, -0.4)]
    [InlineData(1234.51, 1235, 0.49)]
    [InlineData(1234.49, 1234, -0.49)]
    public void The_grand_total_rounds_to_a_whole_rupee_and_reports_what_that_cost(
        decimal value,
        decimal expectedTotal,
        decimal expectedAdjustment)
    {
        // Section 170 of the CGST Act. The adjustment is returned rather than absorbed, because it
        // is a line on the invoice: a total that silently disagrees with the sum above it is the
        // single most common complaint about a badly built checkout.
        var (total, adjustment) = GstCalculator.RoundToRupee(value);

        Assert.Equal(expectedTotal, total);
        Assert.Equal(expectedAdjustment, adjustment);
    }

    [Fact]
    public void Money_rounds_half_away_from_zero_not_to_even()
    {
        // Banker's rounding is .NET's default and would make 2.125 into 2.12. Indian commercial
        // practice rounds half up, and being right by a different convention is still being
        // different from the accounting package the shopkeeper reconciles against.
        Assert.Equal(2.13m, GstCalculator.Round(2.125m));
        Assert.Equal(2.12m, GstCalculator.Round(2.124m));
    }

    [Fact]
    public void The_place_of_supply_comes_from_the_first_two_characters_of_a_gstin()
    {
        Assert.Equal("27", GstCalculator.StateCodeOf("27AAPFU0939F1ZV"));
        Assert.Null(GstCalculator.StateCodeOf(null));
        Assert.Null(GstCalculator.StateCodeOf(" "));
    }

    [Fact]
    public void A_supply_with_an_unknown_state_on_either_side_is_treated_as_intra_state()
    {
        // A shopper with no address yet is being shown a price on a product page, and the store's
        // own state is the only defensible assumption. The figure is re-quoted the moment they
        // enter an address.
        Assert.True(GstCalculator.IsIntraState(null, "27"));
        Assert.True(GstCalculator.IsIntraState("27", null));
        Assert.True(GstCalculator.IsIntraState("27", "27"));
        Assert.False(GstCalculator.IsIntraState("29", "27"));
    }

    [Fact]
    public void An_allocation_sums_exactly_to_the_amount_it_split()
    {
        // Three equal shares of a hundred cannot each be 33.33 and still make 100. The residue goes
        // to the largest weight, where a one-paisa correction is least visible.
        var parts = GstCalculator.AllocateProportionally(100m, [50m, 30m, 20m]);

        Assert.Equal(100m, parts.Sum());
        Assert.Equal(50m, parts[0]);
    }

    [Fact]
    public void An_allocation_that_does_not_divide_evenly_still_sums_exactly()
    {
        var parts = GstCalculator.AllocateProportionally(10m, [1m, 1m, 1m]);

        Assert.Equal(10m, parts.Sum());
    }

    [Fact]
    public void Allocating_across_nothing_allocates_nothing()
    {
        Assert.Empty(GstCalculator.AllocateProportionally(100m, []));
        Assert.Equal(new[] { 0m, 0m }, GstCalculator.AllocateProportionally(100m, [0m, 0m]));
    }
}
