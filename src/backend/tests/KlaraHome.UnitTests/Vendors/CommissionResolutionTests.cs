using KlaraHome.Modules.Vendors.Application.Validation;
using KlaraHome.Modules.Vendors.Domain;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.UnitTests.Vendors;

/// <summary>
/// Commission resolution: most specific category, then price band, then the plan default
/// (docs/03-database-design.md §4.3).
/// </summary>
/// <remarks>
/// This decides money, and it is a pure function over the plan, so it is tested here rather than
/// deferred — the build sprint's rule 1 names pricing maths as the case where a unit test is the
/// cheapest way to get it right while writing it.
/// </remarks>
public sealed class CommissionResolutionTests
{
    private static readonly Guid Electronics = Guid.CreateVersion7();
    private static readonly Guid Furniture = Guid.CreateVersion7();

    [Fact]
    public void With_no_rules_the_plan_default_applies()
    {
        var plan = Plan(defaultRate: 10m);

        var (rate, fee, matched) = plan.Resolve(Electronics, 1_000m);

        Assert.Equal(10m, rate);
        Assert.Equal(0m, fee);
        Assert.Null(matched);
    }

    [Fact]
    public void A_category_rule_beats_the_default()
    {
        var plan = Plan(defaultRate: 10m);
        plan.ReplaceRules([Rule(plan.Id, category: Electronics, rate: 6m)]);

        var (rate, _, matched) = plan.Resolve(Electronics, 1_000m);

        Assert.Equal(6m, rate);
        Assert.Equal(Electronics, matched);
    }

    [Fact]
    public void A_category_rule_does_not_apply_to_another_category()
    {
        var plan = Plan(defaultRate: 10m);
        plan.ReplaceRules([Rule(plan.Id, category: Electronics, rate: 6m)]);

        var (rate, _, matched) = plan.Resolve(Furniture, 1_000m);

        Assert.Equal(10m, rate);
        Assert.Null(matched);
    }

    [Fact]
    public void A_rule_naming_a_category_and_a_band_beats_one_naming_only_the_category()
    {
        var plan = Plan(defaultRate: 10m);

        plan.ReplaceRules(
        [
            Rule(plan.Id, category: Electronics, rate: 8m),
            Rule(plan.Id, category: Electronics, min: 10_000m, rate: 4m),
        ]);

        Assert.Equal(8m, plan.Resolve(Electronics, 9_999m).RatePercent);
        Assert.Equal(4m, plan.Resolve(Electronics, 10_000m).RatePercent);
    }

    [Fact]
    public void A_category_beats_a_price_band_at_the_same_specificity()
    {
        var plan = Plan(defaultRate: 10m);

        plan.ReplaceRules(
        [
            Rule(plan.Id, category: Electronics, rate: 6m),
            Rule(plan.Id, min: 5_000m, rate: 3m),
        ]);

        // Both name exactly one thing. "Electronics, any price" is the more deliberate statement
        // than "anything over five thousand", so it wins.
        Assert.Equal(6m, plan.Resolve(Electronics, 20_000m).RatePercent);

        // …and the band still catches a category the rule does not name.
        Assert.Equal(3m, plan.Resolve(Furniture, 20_000m).RatePercent);
    }

    [Fact]
    public void A_price_band_is_inclusive_at_the_floor_and_exclusive_at_the_ceiling()
    {
        var plan = Plan(defaultRate: 10m);

        plan.ReplaceRules(
        [
            Rule(plan.Id, min: 0m, max: 1_000m, rate: 12m),
            Rule(plan.Id, min: 1_000m, max: 5_000m, rate: 8m),
            Rule(plan.Id, min: 5_000m, rate: 5m),
        ]);

        // The ladder has no gap at a boundary and no overlap either, which is what a half-open
        // band buys: one rupee is charged one rate.
        Assert.Equal(12m, plan.Resolve(null, 999.99m).RatePercent);
        Assert.Equal(8m, plan.Resolve(null, 1_000m).RatePercent);
        Assert.Equal(8m, plan.Resolve(null, 4_999.99m).RatePercent);
        Assert.Equal(5m, plan.Resolve(null, 5_000m).RatePercent);
    }

    [Fact]
    public void The_narrower_band_wins_when_two_equally_specific_rules_both_match()
    {
        var plan = Plan(defaultRate: 10m);

        plan.ReplaceRules(
        [
            Rule(plan.Id, min: 0m, max: 100_000m, rate: 9m),
            Rule(plan.Id, min: 1_000m, max: 2_000m, rate: 4m),
        ]);

        // A badly configured plan still resolves deterministically rather than depending on the
        // order the rows came back in.
        Assert.Equal(4m, plan.Resolve(null, 1_500m).RatePercent);
        Assert.Equal(9m, plan.Resolve(null, 50_000m).RatePercent);
    }

    [Fact]
    public void A_fixed_fee_is_carried_with_the_rate_of_the_rule_that_won()
    {
        var plan = Plan(defaultRate: 10m);
        plan.ReplaceRules([Rule(plan.Id, category: Furniture, rate: 7m, fixedFee: 25m)]);

        var (rate, fee, _) = plan.Resolve(Furniture, 3_000m);

        Assert.Equal(7m, rate);
        Assert.Equal(25m, fee);
    }

    [Fact]
    public void A_sale_with_no_category_still_matches_a_band()
    {
        var plan = Plan(defaultRate: 10m);

        plan.ReplaceRules(
        [
            Rule(plan.Id, category: Electronics, rate: 5m),
            Rule(plan.Id, min: 500m, rate: 7m),
        ]);

        var (rate, _, matched) = plan.Resolve(null, 900m);

        Assert.Equal(7m, rate);
        Assert.Null(matched);
    }

    private static CommissionPlan Plan(decimal defaultRate)
        => CommissionPlan.Create("standard", "Standard commission", CommissionPlanType.Tiered, defaultRate);

    private static CommissionPlanRule Rule(
        Guid planId,
        Guid? category = null,
        decimal? min = null,
        decimal? max = null,
        decimal rate = 0m,
        decimal fixedFee = 0m)
        => CommissionPlanRule.Create(planId, category, min, max, rate, Money.Rupees(fixedFee));
}

/// <summary>
/// The Indian identifier formats, which are the other thing here that is cheaper to get right with
/// a test than by reading a regular expression.
/// </summary>
public sealed class VendorFormatTests
{
    [Theory]
    [InlineData("ABCDE1234F", true)]
    [InlineData("ABCDE1234", false)]
    [InlineData("ABCD01234F", false)]
    [InlineData("abcde1234f", false)]
    [InlineData("ABCDE1234FG", false)]
    public void A_pan_is_five_letters_four_digits_and_a_letter(string candidate, bool valid)
        => Assert.Equal(valid, VendorFormats.Pan().IsMatch(candidate));

    [Theory]
    [InlineData("27ABCDE1234F1Z5", true)]
    [InlineData("27ABCDE1234F1X5", false)]
    [InlineData("27ABCDE1234F1Z", false)]
    [InlineData("2ABCDE1234F1Z55", false)]
    public void A_gstin_has_a_state_code_a_pan_an_entity_number_a_z_and_a_check_digit(
        string candidate,
        bool valid)
        => Assert.Equal(valid, VendorFormats.Gstin().IsMatch(candidate));

    [Theory]
    [InlineData("27ABCDE1234F1Z5", true)]
    [InlineData("01ABCDE1234F1Z5", true)]
    [InlineData("38ABCDE1234F1Z5", true)]
    [InlineData("97ABCDE1234F1Z5", true)]
    [InlineData("99ABCDE1234F1Z5", true)]
    [InlineData("00ABCDE1234F1Z5", false)]
    [InlineData("55ABCDE1234F1Z5", false)]
    public void A_gstin_state_code_must_be_one_that_exists(string gstin, bool known)
        => Assert.Equal(known, VendorFormats.HasKnownStateCode(gstin));

    [Fact]
    public void A_gstin_must_embed_the_pan_it_is_stored_beside()
    {
        Assert.True(VendorFormats.PanMatchesGstin("ABCDE1234F", "27ABCDE1234F1Z5"));
        Assert.False(VendorFormats.PanMatchesGstin("ZZZZZ9999Z", "27ABCDE1234F1Z5"));

        // Nothing to contradict when one of them is absent; the format rules have already spoken.
        Assert.True(VendorFormats.PanMatchesGstin(null, "27ABCDE1234F1Z5"));
        Assert.True(VendorFormats.PanMatchesGstin("ABCDE1234F", null));
    }

    [Theory]
    [InlineData("HDFC0001234", true)]
    [InlineData("HDFC1001234", false)]
    [InlineData("HDF00001234", false)]
    [InlineData("HDFC000123", false)]
    public void An_ifsc_is_four_letters_a_zero_and_six_characters(string candidate, bool valid)
        => Assert.Equal(valid, VendorFormats.Ifsc().IsMatch(candidate));

    [Theory]
    [InlineData("Café Décor", "cafe-decor")]
    [InlineData("  Acme   Furnishings  ", "acme-furnishings")]
    [InlineData("A&B Home!!", "a-b-home")]
    public void A_slug_folds_diacritics_rather_than_dropping_the_letters(string name, string expected)
        => Assert.Equal(expected, VendorFormats.ToSlug(name));

    [Fact]
    public void A_masked_identifier_shows_only_its_last_four_characters()
    {
        Assert.Equal("••••••234F", VendorKycDocument.Mask("ABCDE1234F"));

        // A short value is masked entirely: showing four of six characters is a hint, not a mask.
        Assert.Equal("••••••", VendorKycDocument.Mask("123456"));
        Assert.Null(VendorKycDocument.Mask(null));
    }
}
