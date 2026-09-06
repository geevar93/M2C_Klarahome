using KlaraHome.Modules.Shipping.Domain;

namespace KlaraHome.UnitTests.Shipping;

/// <summary>
/// The arithmetic that decides what a customer pays for delivery, and the geometry beneath it.
/// </summary>
/// <remarks>
/// Tested while writing it under the build sprint's rule 1. Every failure mode here is silent and
/// costs money on every order: a part-kilogram rounded the wrong way makes the platform pay the
/// difference on every heavy parcel, a free-shipping threshold that also zeroes the cash-handling
/// fee gives away what the courier charges, and a volumetric weight computed on the wrong divisor
/// underprices every bulky box. None of them throws; they just leave a figure nobody notices until
/// a courier's invoice arrives.
/// </remarks>
public sealed class ShippingRateTests
{
    [Fact]
    public void The_base_rate_buys_the_bands_floor_weight()
    {
        var rate = Band(minWeightGrams: 0, maxWeightGrams: 500, baseRate: 49m, perKgRate: 30m);

        // A parcel at or under the floor costs the base and nothing else.
        Assert.Equal(49m, rate.AmountFor(chargeableWeightGrams: 500, orderValue: 100m, isCod: false));
        Assert.Equal(49m, rate.AmountFor(chargeableWeightGrams: 1, orderValue: 100m, isCod: false));
    }

    [Fact]
    public void A_part_kilogram_above_the_floor_is_charged_whole()
    {
        var rate = Band(minWeightGrams: 500, maxWeightGrams: 5_000, baseRate: 69m, perKgRate: 30m);

        // 501 g is one gram into the second kilogram, and every courier in India charges for it.
        // Rounding down here would have the platform absorb the difference on every heavy parcel.
        Assert.Equal(99m, rate.AmountFor(chargeableWeightGrams: 501, orderValue: 100m, isCod: false));
        Assert.Equal(99m, rate.AmountFor(chargeableWeightGrams: 1_500, orderValue: 100m, isCod: false));
        Assert.Equal(129m, rate.AmountFor(chargeableWeightGrams: 1_501, orderValue: 100m, isCod: false));
    }

    [Fact]
    public void Free_shipping_zeroes_the_freight_and_not_the_cash_handling_fee()
    {
        var rate = Band(minWeightGrams: 0, maxWeightGrams: 5_000, baseRate: 69m, perKgRate: 30m);
        rate.Price(baseRate: 69m, perKgRate: 30m, freeAbove: 999m, codFee: 39m, isCodAllowed: true);

        // Under the threshold: freight, and the fee on top of it.
        Assert.Equal(69m, rate.AmountFor(400, orderValue: 998m, isCod: false));
        Assert.Equal(108m, rate.AmountFor(400, orderValue: 998m, isCod: true));

        // At and over it: no freight. The fee survives, because it is what the courier charges to
        // handle money and a store that gives away delivery has not agreed to hand that over too.
        Assert.Equal(0m, rate.AmountFor(400, orderValue: 999m, isCod: false));
        Assert.Equal(39m, rate.AmountFor(400, orderValue: 999m, isCod: true));
    }

    [Fact]
    public void A_rule_applies_only_inside_both_of_its_bands()
    {
        var rate = Band(minWeightGrams: 500, maxWeightGrams: 2_000, baseRate: 69m, perKgRate: 30m);
        rate.Band(500, 2_000, minOrderValue: 100m, maxOrderValue: 5_000m);

        Assert.True(rate.Applies(chargeableWeightGrams: 500, orderValue: 100m));
        Assert.True(rate.Applies(chargeableWeightGrams: 2_000, orderValue: 5_000m));

        // Both bounds are inclusive and both bands must match — a parcel in the weight band but
        // outside the value band is not this rule's, and the resolver has to fall through.
        Assert.False(rate.Applies(chargeableWeightGrams: 499, orderValue: 200m));
        Assert.False(rate.Applies(chargeableWeightGrams: 2_001, orderValue: 200m));
        Assert.False(rate.Applies(chargeableWeightGrams: 1_000, orderValue: 99m));
        Assert.False(rate.Applies(chargeableWeightGrams: 1_000, orderValue: 5_001m));
    }

    [Fact]
    public void An_inverted_band_is_straightened_rather_than_stored()
    {
        var rate = ShippingRate.Create(Guid.NewGuid(), ShippingMethod.Standard, vendorId: null);

        // A maximum below the minimum would match nothing at all, which is a rate card with an
        // invisible hole in it. The domain clamps rather than accepting one.
        rate.Band(minWeightGrams: 2_000, maxWeightGrams: 500, minOrderValue: 500m, maxOrderValue: 100m);

        Assert.Equal(2_000, rate.MinWeightGrams);
        Assert.Equal(2_000, rate.MaxWeightGrams);
        Assert.Equal(500m, rate.MinOrderValue);
        Assert.Equal(500m, rate.MaxOrderValue);
    }

    [Fact]
    public void A_promise_never_ends_before_it_starts()
    {
        var rate = ShippingRate.Create(Guid.NewGuid(), ShippingMethod.Express, vendorId: null);

        rate.Promise(minDays: 7, maxDays: 2);

        Assert.Equal(7, rate.EtaMinDays);
        Assert.Equal(7, rate.EtaMaxDays);
    }

    [Fact]
    public void Volumetric_weight_is_the_indian_divisor_and_rounds_up()
    {
        var box = new ShipmentDimensions { LengthCm = 30m, WidthCm = 20m, HeightCm = 10m };

        // 30 × 20 × 10 ÷ 5000 = 1.2 kg. A bulky, light parcel is priced on this rather than on what
        // it weighs, which is the whole reason the measurements are kept.
        Assert.Equal(1_200, box.VolumetricGrams(5_000));

        // A different aggregator, a different contract: the divisor is configuration for a reason.
        Assert.Equal(1_500, box.VolumetricGrams(4_000));
    }

    [Fact]
    public void An_unmeasured_box_has_no_volumetric_weight()
    {
        // Nobody measured it, so there is nothing to compute — and a zero here is what makes the
        // chargeable weight fall back to what the parcel actually weighs.
        Assert.Equal(0, new ShipmentDimensions().VolumetricGrams(5_000));
        Assert.Equal(0, new ShipmentDimensions { LengthCm = 30m, WidthCm = 20m }.VolumetricGrams(5_000));
    }

    [Fact]
    public void A_zone_matches_by_state_or_by_pincode_range_and_a_catch_all_matches_everything()
    {
        var karnataka = Guid.NewGuid();
        var zone = ShippingZone.Create("south", "South", priority: 10);

        zone.Redefine([karnataka], [new PincodeRange("600001", "699999")]);

        Assert.True(zone.Covers(karnataka, "110001"));
        Assert.True(zone.Covers(stateId: null, "600001"));
        Assert.True(zone.Covers(stateId: null, "699999"));
        Assert.False(zone.Covers(Guid.NewGuid(), "110001"));

        // A zone that names nowhere in particular covers everywhere: the rest-of-India fallback
        // every rate card needs.
        var everywhere = ShippingZone.Create("rest", "Rest of India", priority: 900);

        Assert.True(everywhere.IsCatchAll);
        Assert.True(everywhere.Covers(Guid.NewGuid(), "999999"));
    }

    private static ShippingRate Band(int minWeightGrams, int maxWeightGrams, decimal baseRate, decimal perKgRate)
    {
        var rate = ShippingRate.Create(Guid.NewGuid(), ShippingMethod.Standard, vendorId: null);

        rate.Band(minWeightGrams, maxWeightGrams, minOrderValue: 0m, maxOrderValue: null);
        rate.Price(baseRate, perKgRate, freeAbove: null, codFee: 39m, isCodAllowed: true);

        return rate;
    }
}
