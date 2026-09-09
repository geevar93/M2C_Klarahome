using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Pricing;
using KlaraHome.Contracts.Shipping;
using KlaraHome.Modules.Carts.Application.Carts;
using KlaraHome.Modules.Carts.Domain;
using KlaraHome.Modules.Carts.Infrastructure.Checkout;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.UnitTests.Carts;

/// <summary>
/// The checkout session's own rules: what a change of address invalidates, and when cash on
/// delivery may be chosen (docs/03-database-design.md §4.7).
/// </summary>
/// <remarks>
/// Tested while writing them under the build sprint's rule 1. Both are cheap to assert and
/// expensive to get wrong: a delivery choice kept across a change of destination charges Mumbai's
/// shipping for a parcel to Shillong, and a cash-on-delivery rule that lets one through is an
/// uncollectable order.
/// </remarks>
public sealed class CheckoutSessionTests
{
    private static readonly DateTimeOffset Morning = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid Cart = Guid.Parse("00000000-0000-0000-0000-00000000132a");
    private static readonly Guid Customer = Guid.Parse("00000000-0000-0000-0000-00000000132b");
    private static readonly Guid Vendor = Guid.Parse("00000000-0000-0000-0000-00000000132c");
    private static readonly Guid Maharashtra = Guid.Parse("00000000-0000-0000-0000-00000000132d");
    private static readonly Guid Meghalaya = Guid.Parse("00000000-0000-0000-0000-00000000132e");

    [Fact]
    public void Choosing_an_address_sets_the_place_of_supply()
    {
        var session = Open();

        session.SetAddresses(Address(Maharashtra, "400001"), Address(Maharashtra, "400001"), gstin: null);

        Assert.Equal(Maharashtra, session.PlaceOfSupplyStateId);
        Assert.Equal(CheckoutStatus.AddressSet, session.Status);
    }

    [Fact]
    public void Moving_the_destination_throws_away_the_delivery_choice()
    {
        var session = Open();
        session.SetAddresses(Address(Maharashtra, "400001"), Address(Maharashtra, "400001"), gstin: null);
        session.SetShipments([Shipment(120m)]);

        Assert.Equal(120m, session.ShippingTotal);

        session.SetAddresses(Address(Meghalaya, "793001"), Address(Meghalaya, "793001"), gstin: null);

        Assert.Empty(session.Shipments);
        Assert.Equal(0m, session.ShippingTotal);
        Assert.Equal(CheckoutStatus.AddressSet, session.Status);
    }

    [Fact]
    public void Correcting_an_address_within_one_pincode_keeps_the_delivery_choice()
    {
        var session = Open();
        session.SetAddresses(Address(Maharashtra, "400001"), Address(Maharashtra, "400001"), gstin: null);
        session.SetShipments([Shipment(120m)]);
        session.SetPaymentMethod(CheckoutPaymentMethod.Prepaid);

        var corrected = Address(Maharashtra, "400001");
        corrected.RecipientName = "Somebody Else";

        session.SetAddresses(corrected, corrected, gstin: null);

        Assert.Single(session.Shipments);
        Assert.Equal(CheckoutStatus.PaymentSet, session.Status);
    }

    [Fact]
    public void A_failed_placement_hands_the_session_back_at_the_payment_step()
    {
        var session = Open();
        session.SetAddresses(Address(Maharashtra, "400001"), Address(Maharashtra, "400001"), gstin: null);
        session.SetShipments([Shipment(0m)]);
        session.SetPaymentMethod(CheckoutPaymentMethod.Prepaid);

        session.BeginPlacing();
        Assert.False(session.IsOpen);

        session.AbandonPlacing();

        Assert.Equal(CheckoutStatus.PaymentSet, session.Status);
        Assert.True(session.IsOpen);
    }

    [Fact]
    public void A_key_that_failed_may_be_used_again()
    {
        var session = Open();
        var placement = session.BeginPlacement("key-1", "hash-1", Morning);

        placement.Fail("CARD_DECLINED", Morning.AddSeconds(2));
        Assert.Equal(PlacementStatus.Failed, placement.Status);

        placement.Restart(Morning.AddMinutes(1));

        Assert.Equal(PlacementStatus.InProgress, placement.Status);
        Assert.Null(placement.FailureCode);
        Assert.Null(placement.CompletedAt);
    }

    [Fact]
    public void Cash_on_delivery_is_refused_when_the_store_does_not_offer_it()
    {
        var reason = CheckoutWorkflow.CodRefusalReason(
            CartWith(total: 100m, codAllowed: true),
            Open(),
            new CommerceSettings { CodEnabled = false },
            CashCollectable);

        Assert.NotNull(reason);
    }

    [Fact]
    public void Cash_on_delivery_is_refused_above_the_value_ceiling_and_names_it()
    {
        var reason = CheckoutWorkflow.CodRefusalReason(
            CartWith(total: 9000m, codAllowed: true),
            Open(),
            new CommerceSettings { CodEnabled = true, CodOrderValueLimit = 5000m },
            CashCollectable);

        Assert.NotNull(reason);
        Assert.Contains("5000", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Cash_on_delivery_is_refused_by_a_single_item_and_names_it()
    {
        var reason = CheckoutWorkflow.CodRefusalReason(
            CartWith(total: 100m, codAllowed: false),
            Open(),
            new CommerceSettings { CodEnabled = true, CodOrderValueLimit = 5000m },
            CashCollectable);

        Assert.NotNull(reason);
        Assert.Contains("Teak table", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Cash_on_delivery_is_allowed_when_every_rule_holds()
    {
        var reason = CheckoutWorkflow.CodRefusalReason(
            CartWith(total: 100m, codAllowed: true),
            Open(),
            new CommerceSettings { CodEnabled = true, CodOrderValueLimit = 5000m },
            CashCollectable);

        Assert.Null(reason);
    }

    [Fact]
    public void Cash_on_delivery_is_refused_where_no_courier_will_collect_cash()
    {
        var reason = CheckoutWorkflow.CodRefusalReason(
            CartWith(total: 100m, codAllowed: true),
            Open(),
            new CommerceSettings { CodEnabled = true, CodOrderValueLimit = 5000m },
            CashRefused);

        Assert.Equal(CheckoutWorkflow.CodNotCollected, reason);
    }

    [Fact]
    public void Cash_on_delivery_is_allowed_before_an_address_has_been_chosen()
    {
        // No destination to ask about yet. Refusing here would grey the option out on a screen the
        // shopper reaches before they have said where they live.
        var reason = CheckoutWorkflow.CodRefusalReason(
            CartWith(total: 100m, codAllowed: true),
            Open(),
            new CommerceSettings { CodEnabled = true, CodOrderValueLimit = 5000m },
            destination: null);

        Assert.Null(reason);
    }

    private static CheckoutSession Open()
        => CheckoutSession.Open(Cart, Customer, Money.Inr, Morning.AddHours(1));

    /// <summary>A destination a courier will both reach and take cash at.</summary>
    private static DeliveryCheck CashCollectable => Destination(codAvailable: true);

    /// <summary>A destination a courier will reach and will not take cash at.</summary>
    private static DeliveryCheck CashRefused => Destination(codAvailable: false);

    /// <summary>What the logistics seam answers about one PIN code.</summary>
    private static DeliveryCheck Destination(bool codAvailable)
        => new(
            "400001",
            Deliverable: true,
            Covered: true,
            Serviceable: true,
            codAvailable,
            City: "Mumbai",
            State: "Maharashtra",
            EtaDays: 3,
            codAvailable ? DeliveryRefusal.None : DeliveryRefusal.CodUnavailable,
            Message: null);

    private static AddressSnapshot Address(Guid stateId, string pincode)
        => new()
        {
            SourceAddressId = Guid.CreateVersion7(),
            RecipientName = "A Shopper",
            Mobile = "+919000000000",
            Line1 = "1 Example Road",
            City = "Somewhere",
            StateId = stateId,
            Pincode = pincode,
        };

    private static CheckoutShipment Shipment(decimal amount)
        => CheckoutShipment.Create(
            Cart,
            Vendor,
            "standard",
            "Standard delivery",
            carrier: null,
            amount,
            taxAmount: 0m,
            dispatchSlaHours: 24,
            promisedMinDays: 3,
            promisedMaxDays: 7);

    /// <summary>A rendered basket carrying only what the cash-on-delivery rules actually read.</summary>
    private static CartResponse CartWith(decimal total, bool codAllowed)
        => new(
            Cart,
            Customer,
            nameof(CartStatus.Active),
            Money.Inr,
            CouponCode: null,
            LineCount: 1,
            Morning.AddDays(30),
            [
                new CartLineResponse(
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7(),
                    Vendor,
                    "A Seller",
                    "SKU-1",
                    "Teak table",
                    ImageFileId: null,
                    Quantity: 1,
                    SavedForLater: false,
                    UnitMrp: total,
                    UnitPrice: total,
                    UnitPriceWhenAdded: total,
                    LineTotal: total,
                    QuantityAvailable: 5,
                    codAllowed,
                    []),
            ],
            [],
            [],
            QuoteWith(total),
            [],
            IsReadyForCheckout: true);

    /// <summary>A quote carrying only the grand total, which is the one figure the rules read.</summary>
    private static QuoteResult QuoteWith(decimal total)
        => new(
            [],
            [],
            [],
            Money.Inr,
            IsIntraState: true,
            PlaceOfSupplyStateCode: "27",
            Subtotal: total,
            LineDiscountTotal: 0m,
            OrderDiscountTotal: 0m,
            DiscountTotal: 0m,
            TaxableValue: total,
            CgstTotal: 0m,
            SgstTotal: 0m,
            IgstTotal: 0m,
            CessTotal: 0m,
            TaxTotal: 0m,
            Shipping: 0m,
            ShippingDiscount: 0m,
            ShippingTax: 0m,
            CodFee: 0m,
            WalletApplied: 0m,
            RoundingAdjustment: 0m,
            GrandTotal: total,
            AmountPayable: total,
            CouponRejection: null);
}
