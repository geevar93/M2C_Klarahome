using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Shipping;
using KlaraHome.Modules.Shipping.Infrastructure.Serviceability;

namespace KlaraHome.UnitTests.Shipping;

/// <summary>
/// The rule that decides where this store will sell (ADR-018).
/// </summary>
/// <remarks>
/// Tested while writing it under the build sprint's rule 1. It is pure matching over a settings
/// record, it has a right answer, and both of its failure modes are silent and expensive: a rule
/// that matches too little refuses every order and looks exactly like a courier outage, and one
/// that matches too much sells to addresses nobody has agreed to deliver to. Neither throws.
/// </remarks>
public sealed class DeliveryCoverageTests
{
    [Fact]
    public void The_shipped_default_is_hyderabad()
    {
        var policy = new DeliveryCoverageSettings();

        // The 500 range, which is Hyderabad proper.
        Assert.True(DeliveryCoverageService.Covers(policy, "500081", city: null));
        Assert.True(DeliveryCoverageService.Covers(policy, "500001", city: null));

        // Bengaluru, Mumbai, Chennai: outside the area, and refused with nothing else consulted.
        Assert.False(DeliveryCoverageService.Covers(policy, "560001", city: null));
        Assert.False(DeliveryCoverageService.Covers(policy, "400001", city: null));

        // 501 and 502 are the outer districts of the metropolitan region and are deliberately NOT
        // included. Widening to them is an operator's decision, not a guess made here.
        Assert.False(DeliveryCoverageService.Covers(policy, "501301", city: null));
        Assert.False(DeliveryCoverageService.Covers(policy, "502032", city: null));
    }

    [Fact]
    public void A_known_city_is_enough_on_its_own()
    {
        var policy = new DeliveryCoverageSettings();

        // Outside every allowed prefix, but the reference data says it is Hyderabad. The city rule
        // is what keeps a PIN code the postal service renumbered from being refused.
        Assert.True(DeliveryCoverageService.Covers(policy, "509999", city: "Hyderabad"));
        Assert.True(DeliveryCoverageService.Covers(policy, "509999", city: "secunderabad"));
        Assert.False(DeliveryCoverageService.Covers(policy, "509999", city: "Warangal"));
    }

    [Fact]
    public void A_block_beats_every_allow_rule()
    {
        var policy = new DeliveryCoverageSettings
        {
            AllowedPincodes = ["560001"],
            BlockedPincodes = ["500081"],
        };

        // Inside the allowed prefix and inside the allowed city, and still refused. A block is how
        // an operator excludes one address after a courier has failed on it repeatedly, and an
        // allow rule that could override it would make the exclusion meaningless.
        Assert.False(DeliveryCoverageService.Covers(policy, "500081", city: "Hyderabad"));

        // An explicit allow reaches outside the prefixes, which is how a second city is opened for
        // one address before the whole range is.
        Assert.True(DeliveryCoverageService.Covers(policy, "560001", city: null));
    }

    [Fact]
    public void Turning_coverage_off_restores_national_trading()
    {
        var policy = new DeliveryCoverageSettings { Enabled = false };

        Assert.True(DeliveryCoverageService.Covers(policy, "560001", city: null));
        Assert.True(DeliveryCoverageService.Covers(policy, "781001", city: "Guwahati"));

        // Even a blocked PIN code: the whole policy is off, and a half-applied rule would be worse
        // than either state.
        Assert.True(DeliveryCoverageService.Covers(
            policy with { BlockedPincodes = ["560001"] },
            "560001",
            city: null));
    }

    [Fact]
    public void An_enabled_policy_with_no_allow_rule_refuses_everything()
    {
        // The state the settings validator exists to prevent. Asserted here so that if the
        // validator is ever relaxed, the consequence is written down rather than discovered by a
        // store that stopped taking orders.
        var policy = new DeliveryCoverageSettings
        {
            AllowedCities = [],
            AllowedPincodePrefixes = [],
            AllowedPincodes = [],
        };

        Assert.False(DeliveryCoverageService.Covers(policy, "500081", city: "Hyderabad"));
    }

    [Fact]
    public void Coverage_is_reported_before_serviceability_when_both_refuse()
    {
        // Both wrong: the operator hears the half they can act on. Telling a shopper "no courier
        // goes there" about an address the store had already decided not to serve is true and
        // misleading, and it sends an operator looking for a logistics problem they do not have.
        Assert.Equal(
            DeliveryRefusal.NotCovered,
            DeliveryCoverageService.RefusalFor(covered: false, serviceable: false, codWanted: false, codAvailable: false));

        Assert.Equal(
            DeliveryRefusal.NotServiceable,
            DeliveryCoverageService.RefusalFor(covered: true, serviceable: false, codWanted: false, codAvailable: false));

        // Deliverable, but not for cash. Prepaid is still on offer, so this is not a refusal of the
        // address — it is a refusal of one way of paying for it.
        Assert.Equal(
            DeliveryRefusal.CodUnavailable,
            DeliveryCoverageService.RefusalFor(covered: true, serviceable: true, codWanted: true, codAvailable: false));

        Assert.Equal(
            DeliveryRefusal.None,
            DeliveryCoverageService.RefusalFor(covered: true, serviceable: true, codWanted: true, codAvailable: true));
    }
}
