using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Settlements.Infrastructure.Accounting;

namespace KlaraHome.UnitTests.Settlements;

/// <summary>
/// The two deductions an Indian marketplace is obliged to make.
/// </summary>
/// <remarks>
/// Tested while writing them under the build sprint's rule 1. They are pure arithmetic with a right
/// answer, they are filed with two different authorities, and the single commonest way of getting
/// them wrong — computing both on the same base — produces figures that look entirely plausible and
/// are both incorrect. Nothing throws when it happens.
/// </remarks>
public sealed class StatutoryDeductionTests
{
    /// <summary>
    /// A hundred rupees of goods at 18%: the shopper paid 118, the supply was worth 100.
    /// </summary>
    /// <remarks>
    /// The whole point of the test. TCS under section 52 is half a per cent of <b>100</b>, because the
    /// GST inside the price is not consideration for the supply; TDS under section 194-O is a tenth of
    /// a per cent of <b>118</b>, because the CBDT's circular says the gross amount includes it. Fifty
    /// paise and about twelve paise, from the same sale, on two different numbers.
    /// </remarks>
    [Fact]
    public void The_two_deductions_are_taken_on_two_different_bases()
    {
        var policy = new SettlementSettings();

        var deduction = StatutoryDeductions.For(
            netTaxableSupplies: 100m,
            netGrossSales: 118m,
            yearToDateGrossSales: 118m,
            hasPan: true,
            policy);

        Assert.Equal(0.50m, deduction.Tcs);
        Assert.Equal(100m, deduction.TcsBase);

        Assert.Equal(0.12m, deduction.Tds);
        Assert.Equal(118m, deduction.TdsBase);

        Assert.Null(deduction.TdsWithheldReason);
        Assert.Equal(0.62m, deduction.Total);
    }

    /// <summary>
    /// Section 206AA: no PAN, and the rate is five per cent rather than a tenth of one.
    /// </summary>
    /// <remarks>
    /// A separate rate rather than a multiple, because the two are set by two different provisions and
    /// have moved independently. Fifty times the ordinary rate is not a rounding error somebody will
    /// spot later — it is the difference between deducting twelve paise and deducting nearly six
    /// rupees.
    /// </remarks>
    [Fact]
    public void A_seller_with_no_pan_is_deducted_at_the_higher_rate()
    {
        var policy = new SettlementSettings();

        var withPan = StatutoryDeductions.For(100m, 118m, 118m, hasPan: true, policy);
        var without = StatutoryDeductions.For(100m, 118m, 118m, hasPan: false, policy);

        Assert.Equal(0.12m, withPan.Tds);
        Assert.Equal(5.90m, without.Tds);

        // TCS is a GST provision and knows nothing about a PAN.
        Assert.Equal(withPan.Tcs, without.Tcs);
    }

    /// <summary>
    /// The annual threshold is a test on the year, not on the period.
    /// </summary>
    /// <remarks>
    /// A seller who crosses it in week forty is deducted from then on and not retrospectively, which
    /// is why the running total is passed in rather than derived from the period. The reason is
    /// published on the result, because "why was nothing deducted" is the first question an auditor
    /// asks about a nil row.
    /// </remarks>
    [Fact]
    public void No_tax_is_deducted_below_the_annual_threshold()
    {
        var policy = new SettlementSettings { TdsAnnualThreshold = 500_000m };

        var below = StatutoryDeductions.For(100m, 118m, yearToDateGrossSales: 400_000m, hasPan: true, policy);
        var above = StatutoryDeductions.For(100m, 118m, yearToDateGrossSales: 600_000m, hasPan: true, policy);

        Assert.Equal(0m, below.Tds);
        Assert.Equal(StatutoryDeductions.BelowThreshold, below.TdsWithheldReason);

        Assert.Equal(0.12m, above.Tds);
        Assert.Null(above.TdsWithheldReason);

        // TCS has no threshold at all. An electronic commerce operator collects it on the first rupee.
        Assert.Equal(0.50m, below.Tcs);
    }

    /// <summary>Switching a deduction off says so rather than reporting a zero rate.</summary>
    /// <remarks>
    /// A deployment that is not a marketplace is a real case; a nil filing that looks like a
    /// half-finished configuration is not. The reason distinguishes them.
    /// </remarks>
    [Fact]
    public void A_deduction_that_is_switched_off_says_why()
    {
        var policy = new SettlementSettings { TcsEnabled = false, TdsEnabled = false };

        var deduction = StatutoryDeductions.For(100m, 118m, 118m, hasPan: true, policy);

        Assert.Equal(0m, deduction.Tcs);
        Assert.Equal(0m, deduction.Tds);
        Assert.Equal(StatutoryDeductions.Disabled, deduction.TdsWithheldReason);
    }

    /// <summary>
    /// A period whose returns exceeded its sales collects nothing rather than refunding tax.
    /// </summary>
    /// <remarks>
    /// Section 52(1) nets supplies against returns, and a negative net is a real outcome for a small
    /// seller in a bad month. What it is not is a payment from the platform to the seller: the tax has
    /// already been remitted, and correcting it belongs in a GSTR-8 amendment rather than in a payout.
    /// </remarks>
    [Fact]
    public void A_negative_period_collects_nothing()
    {
        var policy = new SettlementSettings();

        var deduction = StatutoryDeductions.For(-100m, -118m, 0m, hasPan: true, policy);

        Assert.Equal(0m, deduction.Tcs);
        Assert.Equal(0m, deduction.Tds);
        Assert.Equal(0m, deduction.TcsBase);
        Assert.Equal(0m, deduction.TdsBase);
    }

    /// <summary>Rounding is half away from zero, not the .NET default.</summary>
    /// <remarks>
    /// Banker's rounding is right for a long series of independent figures and wrong for a charge
    /// somebody is shown: a seller reading the same figure rounded down on one statement and up on the
    /// next has found a bug, whatever the arithmetic says. 1250 at 0.1% is 1.25 exactly; 1255 is
    /// 1.255, which must become 1.26.
    /// </remarks>
    [Fact]
    public void Half_a_paisa_rounds_away_from_zero()
    {
        var policy = new SettlementSettings();

        Assert.Equal(1.25m, StatutoryDeductions.For(0m, 1250m, 1250m, hasPan: true, policy).Tds);
        Assert.Equal(1.26m, StatutoryDeductions.For(0m, 1255m, 1255m, hasPan: true, policy).Tds);
    }
}
