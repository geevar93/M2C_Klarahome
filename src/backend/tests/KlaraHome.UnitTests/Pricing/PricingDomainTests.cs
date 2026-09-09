using KlaraHome.Modules.Pricing.Domain;
using KlaraHome.Modules.Pricing.Infrastructure.Calculation;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.UnitTests.Pricing;

/// <summary>
/// The window predicates and the wallet arithmetic, which decide money and need nothing but a
/// constructor.
/// </summary>
/// <remarks>
/// <para>
/// These are the boundaries the integration suite exercises through a quote and can only assert
/// indirectly: whether a window that starts today applies today, whether one that ends today still
/// does, and whether a debit of exactly the balance is taken whole. Each of them is one comparison
/// operator away from being wrong in a way that is invisible until the day it matters.
/// </para>
/// <para>
/// The asymmetry between the price-list window and the tax-rate window is deliberate and is the
/// most valuable thing here. A price list is an instant range, half-open, because a sale that ends
/// "at midnight" must not still be running at midnight; a tax rate is a range of <em>days</em>,
/// closed at both ends, because the last day a rate applies is a day on which supplies were made
/// under it.
/// </para>
/// </remarks>
public sealed class PricingDomainTests
{
    private static readonly DateTimeOffset Noon = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_price_list_applies_from_the_instant_it_opens_and_stops_at_the_instant_it_closes()
    {
        var list = PriceList.Create(vendorId: null, "DIWALI", "Diwali", PriceListType.Scheduled, Money.Inr);

        list.Update("Diwali", PriceListType.Scheduled, priority: 10, Noon, Noon.AddHours(6));

        Assert.False(list.AppliesAt(Noon.AddTicks(-1)));

        // Inclusive at the start: a campaign that opens at noon is open at noon.
        Assert.True(list.AppliesAt(Noon));
        Assert.True(list.AppliesAt(Noon.AddHours(5)));

        // Exclusive at the end: one that closes at six is shut at six, not a tick afterwards.
        Assert.False(list.AppliesAt(Noon.AddHours(6)));
    }

    [Fact]
    public void A_price_list_with_no_window_applies_until_somebody_switches_it_off()
    {
        var list = PriceList.Create(vendorId: null, "BASE", "Standing prices", PriceListType.Base, Money.Inr);

        Assert.True(list.AppliesAt(Noon));

        // The switch and the window are independent, and neither implies the other: a scheduled
        // list is active long before it opens, and an expired one is active until somebody says
        // otherwise.
        list.SetActive(false);
        Assert.False(list.AppliesAt(Noon));

        list.SetActive(true);
        Assert.True(list.AppliesAt(Noon));
    }

    [Fact]
    public void A_platform_list_prices_every_sellers_offers_and_a_sellers_list_prices_only_their_own()
    {
        var mine = Guid.CreateVersion7();
        var theirs = Guid.CreateVersion7();

        var platform = PriceList.Create(vendorId: null, "ALL", "Platform", PriceListType.Base, Money.Inr);
        var seller = PriceList.Create(mine, "MINE", "Mine", PriceListType.Base, Money.Inr);

        Assert.True(platform.AppliesTo(mine));
        Assert.True(platform.AppliesTo(theirs));

        Assert.True(seller.AppliesTo(mine));
        Assert.False(seller.AppliesTo(theirs));
    }

    [Fact]
    public void Setting_a_price_twice_restates_the_tier_rather_than_adding_a_second_one()
    {
        var list = PriceList.Create(vendorId: null, "TIERS", "Tiers", PriceListType.Base, Money.Inr);
        var listing = Guid.CreateVersion7();

        list.SetPrice(listing, new Money(900m, Money.Inr), minQuantity: 1);
        list.SetPrice(listing, new Money(850m, Money.Inr), minQuantity: 1);
        list.SetPrice(listing, new Money(750m, Money.Inr), minQuantity: 5);

        // Two rows, not three: the tier is the key, so a restated price is an update. A second row
        // for the same tier would be two answers to what one offer costs.
        Assert.Equal(2, list.Items.Count);
        Assert.Equal(850m, list.Items.Single(item => item.MinQuantity == 1).Price.Amount);
        Assert.Equal(750m, list.Items.Single(item => item.MinQuantity == 5).Price.Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_quantity_tier_below_one_is_stored_as_one(int requested)
    {
        var list = PriceList.Create(vendorId: null, "FLOOR", "Floor", PriceListType.Base, Money.Inr);

        // The tier is "from this many units", and a tier of zero would mean the same as one while
        // reading as something else. The database says the same thing in a CHECK.
        Assert.Equal(1, list.SetPrice(Guid.CreateVersion7(), new Money(10m, Money.Inr), requested).MinQuantity);
    }

    [Fact]
    public void A_tax_rate_applies_on_the_first_day_and_on_the_last_day_of_its_window()
    {
        var rate = TaxRate.Create("940360", 12m, cessRate: 0m, new DateOnly(2024, 1, 1));

        rate.Update(null, 12m, 0m, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31), isActive: true);

        Assert.False(rate.AppliesOn(new DateOnly(2023, 12, 31)));

        // Closed at both ends, unlike a price list's window: the last day a rate applies is a day on
        // which supplies were made under it, and they have to reprint at that rate for ever.
        Assert.True(rate.AppliesOn(new DateOnly(2024, 1, 1)));
        Assert.True(rate.AppliesOn(new DateOnly(2024, 12, 31)));
        Assert.False(rate.AppliesOn(new DateOnly(2025, 1, 1)));
    }

    [Fact]
    public void An_open_ended_tax_rate_applies_for_ever_until_it_is_closed_or_deactivated()
    {
        var rate = TaxRate.Create("940360", 18m, cessRate: 0m, new DateOnly(2025, 1, 1));

        Assert.True(rate.AppliesOn(new DateOnly(2099, 1, 1)));

        rate.Update(null, 18m, 0m, new DateOnly(2025, 1, 1), effectiveTo: null, isActive: false);
        Assert.False(rate.AppliesOn(new DateOnly(2099, 1, 1)));
    }

    [Fact]
    public void A_rate_is_clamped_to_a_hundred_per_cent_and_an_hsn_loses_the_spaces_it_was_pasted_with()
    {
        var rate = TaxRate.Create(" 9403 60 ", 18m, cessRate: 0m, new DateOnly(2025, 1, 1));

        // The separators come from a government PDF; the code that matters is the digits.
        Assert.Equal("940360", rate.HsnCode);

        rate.Update(null, 250m, 400m, new DateOnly(2025, 1, 1), null, isActive: true);

        Assert.Equal(TaxRate.MaxRate, rate.Rate);
        Assert.Equal(TaxRate.MaxRate, rate.CessRate);
    }

    [Fact]
    public void A_promotion_is_live_from_the_instant_it_opens_and_shut_at_the_instant_it_closes()
    {
        var promotion = Promotion.Create("FLASH", "Flash sale", PromotionType.Percentage, PromotionApplication.Order);

        Update(promotion, startsAt: Noon, endsAt: Noon.AddHours(2));
        promotion.SetActive(true);

        Assert.False(promotion.IsLiveAt(Noon.AddTicks(-1)));
        Assert.True(promotion.IsLiveAt(Noon));
        Assert.False(promotion.IsLiveAt(Noon.AddHours(2)));

        promotion.SetActive(false);
        Assert.False(promotion.IsLiveAt(Noon.AddHours(1)));
    }

    [Fact]
    public void A_code_is_the_same_code_however_it_was_typed_and_a_blank_one_is_no_code_at_all()
    {
        Assert.Equal("DIWALI25", Promotion.NormalizeCode(" diwali25 "));
        Assert.Equal("DIWALI25", Promotion.NormalizeCode("Diwali25"));

        // An automatic cart rule carries no code, and the partial unique index depends on that being
        // a null rather than an empty string — several empty strings would be a duplicate.
        Assert.Null(Promotion.NormalizeCode(null));
        Assert.Null(Promotion.NormalizeCode("   "));
    }

    [Fact]
    public void A_promotions_limits_are_clamped_so_a_meaningless_one_reads_as_no_limit()
    {
        var promotion = Promotion.Create(null, "Rule", PromotionType.Percentage, PromotionApplication.Order);

        Update(promotion, value: -5m, priority: 5000, usageLimitTotal: 0, usageLimitPerCustomer: -1, maxDiscount: 0m);

        // A limit of zero would mean "nobody may ever use this", which nobody types on purpose; a
        // ceiling of zero would mean "take nothing off". Both read as "no limit", and the database
        // refuses the literal zero so the two cannot disagree.
        Assert.Equal(0m, promotion.Value);
        Assert.Equal(Promotion.MaxPriority, promotion.Priority);
        Assert.Null(promotion.UsageLimitTotal);
        Assert.Null(promotion.UsageLimitPerCustomer);
        Assert.Null(promotion.MaxDiscount);
    }

    [Fact]
    public void A_promotion_with_no_total_limit_always_has_uses_left()
    {
        var promotion = Promotion.Create(null, "Rule", PromotionType.Percentage, PromotionApplication.Order);

        Assert.True(promotion.HasUsesLeft);

        Update(promotion, usageLimitTotal: 1);
        Assert.True(promotion.HasUsesLeft);
    }

    [Fact]
    public void A_redemption_is_reversed_once_and_says_so_the_second_time()
    {
        var redemption = PromotionRedemption.Create(
            Guid.CreateVersion7(),
            "DIWALI25",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            100m,
            Noon);

        Assert.Equal(RedemptionStatus.Redeemed, redemption.Status);

        Assert.True(redemption.Reverse(Noon.AddHours(1)));
        Assert.Equal(RedemptionStatus.Reversed, redemption.Status);
        Assert.Equal(Noon.AddHours(1), redemption.ReversedAt);

        // The second cancellation of an already-cancelled order gives nothing further back, which is
        // what stops a reversal crediting a campaign's budget twice.
        Assert.False(redemption.Reverse(Noon.AddHours(2)));
        Assert.Equal(Noon.AddHours(1), redemption.ReversedAt);
    }

    [Fact]
    public void A_wallet_debit_is_all_or_nothing_and_records_what_was_left()
    {
        var wallet = Wallet.Open(Guid.CreateVersion7(), Money.Inr);

        var credited = wallet.Apply(WalletTransactionType.Credit, 500m, "refund", "return", Guid.CreateVersion7(), Noon);

        Assert.NotNull(credited);
        Assert.Equal(500m, wallet.Balance.Amount);
        Assert.Equal(500m, credited.BalanceAfter.Amount);

        // More than there is: nothing at all, rather than as much as there was. A partial debit
        // would leave the order underpaid by a figure nobody quoted.
        Assert.Null(wallet.Apply(WalletTransactionType.Debit, 500.01m, "order-payment", "order", Guid.CreateVersion7(), Noon));
        Assert.Equal(500m, wallet.Balance.Amount);

        // Exactly what there is: taken whole.
        var spent = wallet.Apply(WalletTransactionType.Debit, 500m, "order-payment", "order", Guid.CreateVersion7(), Noon);

        Assert.NotNull(spent);
        Assert.Equal(0m, wallet.Balance.Amount);
        Assert.Equal(0m, spent.BalanceAfter.Amount);
        Assert.Equal(500m, spent.Amount.Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void A_wallet_movement_of_nothing_or_less_is_no_movement(decimal amount)
    {
        var wallet = Wallet.Open(Guid.CreateVersion7(), Money.Inr);

        // The amount is always positive and the type carries the sign. A signed amount plus a type
        // is two places to read the direction from, and they eventually disagree.
        Assert.Null(wallet.Apply(WalletTransactionType.Credit, amount, "refund", "return", Guid.CreateVersion7(), Noon));
        Assert.Equal(0m, wallet.Balance.Amount);
    }

    [Fact]
    public void Expiring_credit_takes_it_away_and_reversing_a_debit_gives_it_back()
    {
        var wallet = Wallet.Open(Guid.CreateVersion7(), Money.Inr);

        wallet.Apply(WalletTransactionType.Credit, 300m, "refund", "return", Guid.CreateVersion7(), Noon);

        // Expiry is a debit in everything but name: credit that lapsed unspent has left the wallet.
        Assert.NotNull(wallet.Apply(WalletTransactionType.Expiry, 100m, "refund", null, null, Noon));
        Assert.Equal(200m, wallet.Balance.Amount);

        // A reversal is a credit: the order it paid for was cancelled, so the money comes back.
        Assert.NotNull(wallet.Apply(WalletTransactionType.Reversal, 50m, "order-cancelled", "order", Guid.CreateVersion7(), Noon));
        Assert.Equal(250m, wallet.Balance.Amount);

        // And an expiry larger than the balance takes nothing, on the same all-or-nothing rule.
        Assert.Null(wallet.Apply(WalletTransactionType.Expiry, 250.01m, "refund", null, null, Noon));
        Assert.Equal(250m, wallet.Balance.Amount);
    }

    [Theory]
    [InlineData(15_000, 28, 12)]
    [InlineData(999.99, 28, 12)]
    [InlineData(1.01, 28, 12)]
    [InlineData(4567.89, 12, 5)]
    [InlineData(0.03, 18, 1)]
    public void A_cess_bearing_line_still_adds_back_up_to_the_gross(decimal gross, decimal rate, decimal cess)
    {
        // Both levies sit on the same base, so both belong in the divisor, and the cess is rounded
        // before GST takes the residue. Getting the divisor wrong overstates the taxable value on
        // every cess-bearing line, which is the classic way a marketplace under-remits.
        var intra = GstCalculator.SplitInclusive(gross, rate, cess, isIntraState: true);
        var inter = GstCalculator.SplitInclusive(gross, rate, cess, isIntraState: false);

        Assert.Equal(GstCalculator.Round(gross), intra.Gross);
        Assert.Equal(GstCalculator.Round(gross), inter.Gross);

        // The cess is the same figure whichever side of a state line the supply crosses; only the
        // GST is split differently.
        Assert.Equal(intra.Cess, inter.Cess);
        Assert.Equal(intra.TaxableValue, inter.TaxableValue);
        Assert.Equal(intra.Cgst + intra.Sgst, inter.Igst);
    }

    /// <summary>Restates a promotion, leaving everything the caller did not name at its default.</summary>
    /// <param name="promotion">The promotion.</param>
    /// <param name="value">The percentage or amount.</param>
    /// <param name="priority">Evaluation order.</param>
    /// <param name="startsAt">When it opens.</param>
    /// <param name="endsAt">When it closes.</param>
    /// <param name="usageLimitTotal">The most times it may ever be redeemed.</param>
    /// <param name="usageLimitPerCustomer">The most times one shopper may redeem it.</param>
    /// <param name="maxDiscount">The most it will ever take off.</param>
    private static void Update(
        Promotion promotion,
        decimal value = 10m,
        int priority = 100,
        DateTimeOffset? startsAt = null,
        DateTimeOffset? endsAt = null,
        int? usageLimitTotal = null,
        int? usageLimitPerCustomer = null,
        decimal? maxDiscount = null)
        => promotion.Update(
            promotion.Name,
            description: null,
            promotion.Type,
            promotion.AppliesTo,
            value,
            new PromotionScope(),
            new PromotionConditions(),
            StackingMode.Exclusive,
            priority,
            startsAt ?? Noon,
            endsAt,
            usageLimitTotal,
            usageLimitPerCustomer,
            minOrderValue: 0m,
            maxDiscount);
}
