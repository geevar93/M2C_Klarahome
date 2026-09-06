using KlaraHome.Contracts.Pricing;
using KlaraHome.Modules.Pricing.Domain;
using KlaraHome.Modules.Pricing.Infrastructure.Calculation;

namespace KlaraHome.UnitTests.Pricing;

/// <summary>
/// The promotion engine: what applies, in what order, and for how much
/// (docs/03-database-design.md §4.6).
/// </summary>
/// <remarks>
/// Tested while writing it under the build sprint's rule 1. Discount arithmetic is the part of a
/// marketplace that is argued about most and verified least: its failures are silent, they are
/// money, and by the time anybody notices the campaign is over. The evaluator is pure, so testing
/// it costs a constructor call.
/// </remarks>
public sealed class PromotionEvaluatorTests
{
    private static readonly Guid Vendor = Guid.Parse("00000000-0000-0000-0000-0000000012a0");
    private static readonly Guid OtherVendor = Guid.Parse("00000000-0000-0000-0000-0000000012a1");
    private static readonly Guid Sofa = Guid.Parse("00000000-0000-0000-0000-0000000012b0");
    private static readonly Guid Lamp = Guid.Parse("00000000-0000-0000-0000-0000000012b1");
    private static readonly Guid Furniture = Guid.Parse("00000000-0000-0000-0000-0000000012c0");
    private static readonly Guid Sofas = Guid.Parse("00000000-0000-0000-0000-0000000012c1");
    private static readonly Guid Lighting = Guid.Parse("00000000-0000-0000-0000-0000000012c2");
    private static readonly Guid Brand = Guid.Parse("00000000-0000-0000-0000-0000000012d0");
    private static readonly Guid Customer = Guid.Parse("00000000-0000-0000-0000-0000000012e0");

    [Fact]
    public void A_percentage_off_the_order_is_allocated_across_the_matching_lines()
    {
        var basket = Basket([SofaLine(quantity: 1, unitPrice: 1000m), LampLine(quantity: 1, unitPrice: 500m)]);
        var promotion = Percentage(10m, PromotionApplication.Order);

        var outcome = PromotionEvaluator.Evaluate([promotion], basket);

        // 150 off, split 100/50 on the lines' gross — the allocation is what lets the tax on the
        // discount land on the lines whose rates it changed.
        Assert.Equal(100m, outcome.OrderDiscounts[basket.Lines[0].LineId]);
        Assert.Equal(50m, outcome.OrderDiscounts[basket.Lines[1].LineId]);
        Assert.Equal(150m, outcome.OrderDiscounts.Values.Sum());
    }

    [Fact]
    public void A_percentage_off_a_line_comes_off_each_matching_line_separately()
    {
        var basket = Basket([SofaLine(1, 1000m), LampLine(1, 500m)]);
        var promotion = Percentage(10m, PromotionApplication.Line);

        var outcome = PromotionEvaluator.Evaluate([promotion], basket);

        Assert.Equal(100m, outcome.LineDiscounts[basket.Lines[0].LineId]);
        Assert.Equal(50m, outcome.LineDiscounts[basket.Lines[1].LineId]);
        Assert.Equal(0m, outcome.OrderDiscounts.Values.Sum());
    }

    [Fact]
    public void A_max_discount_caps_a_runaway_percentage()
    {
        var basket = Basket([SofaLine(1, 10_000m)]);
        var promotion = Percentage(50m, PromotionApplication.Order);
        promotion.Update(
            promotion.Name, null, promotion.Type, promotion.AppliesTo, 50m,
            promotion.Scope, promotion.Conditions, promotion.Stacking, promotion.Priority,
            promotion.StartsAt, null, null, null, 0m, maxDiscount: 1_000m);

        var outcome = PromotionEvaluator.Evaluate([promotion], basket);

        Assert.Equal(1_000m, outcome.OrderDiscounts.Values.Sum());
    }

    [Fact]
    public void A_promotion_scoped_to_a_parent_category_reaches_everything_beneath_it()
    {
        // The sofa's path is /furniture/sofas/, the lamp's is /lighting/. Scoping to Furniture must
        // catch the sofa without Pricing having to walk Catalog's tree.
        var basket = Basket([SofaLine(1, 1000m), LampLine(1, 500m)]);
        var promotion = Percentage(10m, PromotionApplication.Line);
        promotion.Scope.CategoryIds.Add(Furniture);

        var outcome = PromotionEvaluator.Evaluate([promotion], basket);

        Assert.Equal(100m, outcome.LineDiscounts[basket.Lines[0].LineId]);
        Assert.Equal(0m, outcome.LineDiscounts[basket.Lines[1].LineId]);
    }

    [Fact]
    public void An_exclusion_beats_every_inclusion()
    {
        var basket = Basket([SofaLine(1, 1000m)]);
        var promotion = Percentage(10m, PromotionApplication.Line);
        promotion.Scope.CategoryIds.Add(Furniture);
        promotion.Scope.ExcludedListingIds.Add(Sofa);

        var outcome = PromotionEvaluator.Evaluate([promotion], basket);

        Assert.Equal(0m, outcome.LineDiscounts[basket.Lines[0].LineId]);
        Assert.False(outcome.Promotions[0].Applied);
    }

    [Fact]
    public void Scope_dimensions_intersect_rather_than_union()
    {
        // Scoped to a brand and a seller: the line has the brand but a different seller, so it does
        // not qualify. Reading the dimensions as alternatives would discount half the marketplace.
        var basket = Basket([SofaLine(1, 1000m)]);
        var promotion = Percentage(10m, PromotionApplication.Line);
        promotion.Scope.BrandIds.Add(Brand);
        promotion.Scope.VendorIds.Add(OtherVendor);

        var outcome = PromotionEvaluator.Evaluate([promotion], basket);

        Assert.False(outcome.Promotions[0].Applied);
    }

    [Fact]
    public void An_exclusive_promotion_stops_everything_after_it()
    {
        var basket = Basket([SofaLine(1, 1000m)]);
        var first = Percentage(10m, PromotionApplication.Order, priority: 10, StackingMode.Exclusive);
        var second = Percentage(5m, PromotionApplication.Order, priority: 20, StackingMode.Stackable);

        var outcome = PromotionEvaluator.Evaluate([second, first], basket);

        Assert.Equal(100m, outcome.OrderDiscounts.Values.Sum());
        Assert.True(Report(outcome, first).Applied);
        Assert.False(Report(outcome, second).Applied);
    }

    [Fact]
    public void An_exclusive_promotion_cannot_apply_after_something_else_already_has()
    {
        // Both directions. "Exclusive" must not degrade into "first in the list", which is not what
        // a merchandiser thinks they are choosing.
        var basket = Basket([SofaLine(1, 1000m)]);
        var stackable = Percentage(5m, PromotionApplication.Order, priority: 10, StackingMode.Stackable);
        var exclusive = Percentage(10m, PromotionApplication.Order, priority: 20, StackingMode.Exclusive);

        var outcome = PromotionEvaluator.Evaluate([stackable, exclusive], basket);

        Assert.Equal(50m, outcome.OrderDiscounts.Values.Sum());
        Assert.True(Report(outcome, stackable).Applied);
        Assert.False(Report(outcome, exclusive).Applied);
    }

    [Fact]
    public void Two_stackable_promotions_both_apply_in_priority_order()
    {
        var basket = Basket([SofaLine(1, 1000m)]);
        var first = Percentage(10m, PromotionApplication.Order, priority: 10, StackingMode.Stackable);
        var second = Percentage(10m, PromotionApplication.Order, priority: 20, StackingMode.Stackable);

        var outcome = PromotionEvaluator.Evaluate([second, first], basket);

        // The second is computed on what is left, not on the original gross: 100 then 90, not 200.
        Assert.Equal(190m, outcome.OrderDiscounts.Values.Sum());
    }

    [Fact]
    public void No_combination_of_promotions_can_take_a_line_below_zero()
    {
        var basket = Basket([SofaLine(1, 100m)]);
        var first = Percentage(80m, PromotionApplication.Line, priority: 10, StackingMode.Stackable);
        var second = Percentage(80m, PromotionApplication.Line, priority: 20, StackingMode.Stackable);

        var outcome = PromotionEvaluator.Evaluate([first, second], basket);

        var total = outcome.LineDiscounts.Values.Sum() + outcome.OrderDiscounts.Values.Sum();
        Assert.True(total <= 100m, $"discounted {total} off a line worth 100");
    }

    [Fact]
    public void Buy_one_get_one_free_discounts_the_cheapest_units()
    {
        // Two sofas at 1000 and two lamps at 500, buy 1 get 1: four units, two sets, two free — and
        // the free ones are the cheapest, which is the universal retail convention.
        var basket = Basket([SofaLine(2, 1000m), LampLine(2, 500m)]);
        var promotion = Bogo(buy: 1, get: 1, percent: 100m);

        var outcome = PromotionEvaluator.Evaluate([promotion], basket);

        Assert.Equal(1000m, outcome.LineDiscounts[basket.Lines[1].LineId]);
        Assert.Equal(0m, outcome.LineDiscounts[basket.Lines[0].LineId]);
    }

    [Fact]
    public void Buy_two_get_one_half_price_earns_one_unit_per_three()
    {
        var basket = Basket([LampLine(6, 500m)]);
        var promotion = Bogo(buy: 2, get: 1, percent: 50m);

        var outcome = PromotionEvaluator.Evaluate([promotion], basket);

        // Six units, three-unit sets, two free units at half price: 2 x 250.
        Assert.Equal(500m, outcome.LineDiscounts[basket.Lines[0].LineId]);
    }

    [Fact]
    public void A_bundle_needs_every_member_in_the_basket()
    {
        var promotion = Bundle(1200m, Sofa, Lamp);

        var without = PromotionEvaluator.Evaluate([promotion], Basket([SofaLine(1, 1000m)]));
        Assert.False(without.Promotions[0].Applied);

        var with = PromotionEvaluator.Evaluate([promotion], Basket([SofaLine(1, 1000m), LampLine(1, 500m)]));
        Assert.True(with.Promotions[0].Applied);
        Assert.Equal(300m, with.LineDiscounts.Values.Sum());
    }

    [Fact]
    public void A_tier_ladder_takes_the_best_step_the_basket_clears_not_the_sum_of_them()
    {
        var basket = Basket([SofaLine(1, 5000m)]);
        var promotion = Tiered((1000m, 5m), (3000m, 10m));

        var outcome = PromotionEvaluator.Evaluate([promotion], basket);

        Assert.Equal(500m, outcome.OrderDiscounts.Values.Sum());
    }

    [Fact]
    public void Free_shipping_takes_the_whole_delivery_charge_off()
    {
        var basket = Basket(new[] { SofaLine(1, 1000m) }, shipping: 79m);
        var promotion = FreeShipping();

        var outcome = PromotionEvaluator.Evaluate([promotion], basket);

        Assert.Equal(79m, outcome.ShippingDiscount);
        Assert.Equal(0m, outcome.LineDiscounts.Values.Sum());
    }

    [Fact]
    public void A_minimum_order_value_refuses_with_a_message_a_shopper_can_act_on()
    {
        var basket = Basket([SofaLine(1, 400m)]);
        var promotion = Percentage(10m, PromotionApplication.Order);
        promotion.Update(
            promotion.Name, null, promotion.Type, promotion.AppliesTo, 10m,
            promotion.Scope, promotion.Conditions, promotion.Stacking, promotion.Priority,
            promotion.StartsAt, null, null, null, minOrderValue: 500m, null);

        var outcome = PromotionEvaluator.Evaluate([promotion], basket);

        Assert.False(outcome.Promotions[0].Applied);
        Assert.Contains("500", outcome.Promotions[0].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_first_order_offer_is_refused_for_a_returning_customer_and_says_why()
    {
        var basket = Basket(new[] { SofaLine(1, 1000m) }, isFirstOrder: false);
        var promotion = Percentage(10m, PromotionApplication.Order);
        promotion.Conditions.FirstOrderOnly = true;

        var outcome = PromotionEvaluator.Evaluate([promotion], basket);

        Assert.False(outcome.Promotions[0].Applied);
        Assert.Contains("first order", outcome.Promotions[0].Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_per_customer_limit_counts_what_that_shopper_has_already_used()
    {
        var promotion = Percentage(10m, PromotionApplication.Order);
        promotion.Update(
            promotion.Name, null, promotion.Type, promotion.AppliesTo, 10m,
            promotion.Scope, promotion.Conditions, promotion.Stacking, promotion.Priority,
            promotion.StartsAt, null, null, usageLimitPerCustomer: 1, 0m, null);

        var used = Basket(
            new[] { SofaLine(1, 1000m) },
            redemptions: new Dictionary<Guid, int> { [promotion.Id] = 1 });

        var outcome = PromotionEvaluator.Evaluate([promotion], used);

        Assert.False(outcome.Promotions[0].Applied);
        Assert.Contains("already used", outcome.Promotions[0].Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_per_customer_limit_asks_an_anonymous_shopper_to_sign_in()
    {
        var promotion = Percentage(10m, PromotionApplication.Order);
        promotion.Update(
            promotion.Name, null, promotion.Type, promotion.AppliesTo, 10m,
            promotion.Scope, promotion.Conditions, promotion.Stacking, promotion.Priority,
            promotion.StartsAt, null, null, usageLimitPerCustomer: 1, 0m, null);

        var anonymous = Basket(new[] { SofaLine(1, 1000m) }, anonymous: true);

        var outcome = PromotionEvaluator.Evaluate([promotion], anonymous);

        Assert.False(outcome.Promotions[0].Applied);
        Assert.Contains("Sign in", outcome.Promotions[0].Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_cod_only_offer_is_refused_on_a_prepaid_basket()
    {
        var basket = Basket([SofaLine(1, 1000m)]);
        var promotion = Percentage(10m, PromotionApplication.Order);
        promotion.Conditions.PaymentMethods.Add(PromotionEvaluator.CodMethod);

        var outcome = PromotionEvaluator.Evaluate([promotion], basket);

        Assert.False(outcome.Promotions[0].Applied);
    }

    [Fact]
    public void A_code_that_matches_no_promotion_is_reported_as_invalid()
    {
        var basket = Basket(new[] { SofaLine(1, 1000m) }, couponCode: "NOSUCHCODE");

        var outcome = PromotionEvaluator.Evaluate([], basket);

        Assert.Equal("That code is not valid.", outcome.CouponRejection);
    }

    [Fact]
    public void A_code_that_matched_but_did_not_qualify_reports_the_real_reason()
    {
        var promotion = Percentage(10m, PromotionApplication.Order, code: "FIRST10");
        promotion.Conditions.FirstOrderOnly = true;

        var basket = Basket(new[] { SofaLine(1, 1000m) }, couponCode: "FIRST10", isFirstOrder: false);

        var outcome = PromotionEvaluator.Evaluate([promotion], basket);

        Assert.Contains("first order", outcome.CouponRejection!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_code_that_worked_reports_no_rejection()
    {
        var promotion = Percentage(10m, PromotionApplication.Order, code: "SAVE10");

        var basket = Basket(new[] { SofaLine(1, 1000m) }, couponCode: "SAVE10");

        var outcome = PromotionEvaluator.Evaluate([promotion], basket);

        Assert.Null(outcome.CouponRejection);
    }

    [Fact]
    public void Equal_priorities_resolve_the_same_way_every_time()
    {
        // Two campaigns at the same rank must not be decided by whatever order the database
        // returned them in, or the same basket would price differently on two page loads.
        var first = Percentage(10m, PromotionApplication.Order, priority: 50, StackingMode.Exclusive);
        var second = Percentage(20m, PromotionApplication.Order, priority: 50, StackingMode.Exclusive);

        var forwards = PromotionEvaluator.Evaluate([first, second], Basket([SofaLine(1, 1000m)]));
        var backwards = PromotionEvaluator.Evaluate([second, first], Basket([SofaLine(1, 1000m)]));

        Assert.Equal(
            forwards.Promotions.Select(promotion => promotion.PromotionId),
            backwards.Promotions.Select(promotion => promotion.PromotionId));
    }

    private static QuotePromotion Report(PromotionOutcome outcome, Promotion promotion)
        => outcome.Promotions.Single(report => report.PromotionId == promotion.Id);

    private static PromotionLine SofaLine(int quantity, decimal unitPrice)
        => new(Guid.CreateVersion7(), Sofa, Vendor, $"/{Furniture}/{Sofas}/", Brand, quantity, unitPrice);

    private static PromotionLine LampLine(int quantity, decimal unitPrice)
        => new(Guid.CreateVersion7(), Lamp, Vendor, $"/{Lighting}/", null, quantity, unitPrice);

    private static PromotionContext Basket(
        PromotionLine[] lines,
        bool anonymous = false,
        string? couponCode = null,
        decimal shipping = 0m,
        bool isFirstOrder = true,
        IReadOnlyDictionary<Guid, int>? redemptions = null)
        => new(
            lines,
            anonymous ? null : Customer,
            Segment: null,
            couponCode,
            QuotePaymentMethod.Prepaid,
            shipping,
            isFirstOrder,
            redemptions ?? new Dictionary<Guid, int>());

    private static Promotion Percentage(
        decimal value,
        PromotionApplication appliesTo,
        int priority = Promotion.DefaultPriority,
        StackingMode stacking = StackingMode.Stackable,
        string? code = null)
    {
        var promotion = Promotion.Create(code, "Test offer", PromotionType.Percentage, appliesTo);

        promotion.Update(
            "Test offer", null, PromotionType.Percentage, appliesTo, value,
            new PromotionScope(), new PromotionConditions(), stacking, priority,
            DateTimeOffset.MinValue, null, null, null, 0m, null);

        return promotion;
    }

    private static Promotion Bogo(int buy, int get, decimal percent)
    {
        var promotion = Promotion.Create(null, "BOGO", PromotionType.Bogo, PromotionApplication.Line);

        promotion.Update(
            "BOGO", null, PromotionType.Bogo, PromotionApplication.Line, 0m,
            new PromotionScope(),
            new PromotionConditions { BuyQuantity = buy, GetQuantity = get, GetDiscountPercent = percent },
            StackingMode.Stackable, Promotion.DefaultPriority,
            DateTimeOffset.MinValue, null, null, null, 0m, null);

        return promotion;
    }

    private static Promotion Bundle(decimal bundlePrice, params Guid[] members)
    {
        var promotion = Promotion.Create(null, "Bundle", PromotionType.Bundle, PromotionApplication.Line);

        promotion.Update(
            "Bundle", null, PromotionType.Bundle, PromotionApplication.Line, 0m,
            new PromotionScope(),
            new PromotionConditions { BundleListingIds = [.. members], BundlePrice = bundlePrice },
            StackingMode.Stackable, Promotion.DefaultPriority,
            DateTimeOffset.MinValue, null, null, null, 0m, null);

        return promotion;
    }

    private static Promotion Tiered(params (decimal MinAmount, decimal Value)[] steps)
    {
        var promotion = Promotion.Create(null, "Ladder", PromotionType.Tiered, PromotionApplication.Order);

        promotion.Update(
            "Ladder", null, PromotionType.Tiered, PromotionApplication.Order, 0m,
            new PromotionScope(),
            new PromotionConditions
            {
                Tiers = [.. steps.Select(step => new PromotionTier { MinAmount = step.MinAmount, Value = step.Value })],
                TiersArePercentage = true,
            },
            StackingMode.Stackable, Promotion.DefaultPriority,
            DateTimeOffset.MinValue, null, null, null, 0m, null);

        return promotion;
    }

    private static Promotion FreeShipping()
    {
        var promotion = Promotion.Create(
            null,
            "Free delivery",
            PromotionType.FreeShipping,
            PromotionApplication.Shipping);

        promotion.Update(
            "Free delivery", null, PromotionType.FreeShipping, PromotionApplication.Shipping, 0m,
            new PromotionScope(), new PromotionConditions(), StackingMode.Stackable,
            Promotion.DefaultPriority, DateTimeOffset.MinValue, null, null, null, 0m, null);

        return promotion;
    }

}
