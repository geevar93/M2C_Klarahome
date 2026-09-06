using KlaraHome.Modules.Carts.Domain;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.UnitTests.Carts;

/// <summary>
/// The basket arithmetic: quantity clamping, the add-what-is-already-there rule, and the merge on
/// login (docs/03-database-design.md §4.7).
/// </summary>
/// <remarks>
/// Tested while writing it under the build sprint's rule 1. The merge is the piece worth asserting:
/// it runs exactly once per shopper, at the moment they sign in, and every way of getting it wrong —
/// dropping a line, doubling a quantity, overwriting a coupon — is silent, is discovered by the
/// customer, and is discovered at the till.
/// </remarks>
public sealed class CartTests
{
    private static readonly DateTimeOffset Morning = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    private static readonly Guid Customer = Guid.Parse("00000000-0000-0000-0000-00000000130a");
    private static readonly Guid Vendor = Guid.Parse("00000000-0000-0000-0000-00000000130b");
    private static readonly Guid OtherVendor = Guid.Parse("00000000-0000-0000-0000-00000000130c");
    private static readonly Guid ListingA = Guid.Parse("00000000-0000-0000-0000-00000000131a");
    private static readonly Guid ListingB = Guid.Parse("00000000-0000-0000-0000-00000000131b");

    [Fact]
    public void Adding_the_same_offer_twice_increases_one_line()
    {
        var cart = Cart.ForCustomer(Customer, Money.Inr, Morning, Lifetime);

        cart.Add(ListingA, Vendor, 2, 100m, maxQuantity: 10, Morning, Lifetime);
        cart.Add(ListingA, Vendor, 3, 100m, maxQuantity: 10, Morning, Lifetime);

        Assert.Single(cart.Lines);
        Assert.Equal(5, cart.Lines[0].Quantity);
    }

    [Fact]
    public void Adding_more_than_the_ceiling_clamps_rather_than_refusing()
    {
        var cart = Cart.ForCustomer(Customer, Money.Inr, Morning, Lifetime);

        cart.Add(ListingA, Vendor, 40, 100m, maxQuantity: 10, Morning, Lifetime);

        Assert.Equal(10, cart.Lines[0].Quantity);
    }

    [Fact]
    public void Adding_something_set_aside_brings_it_back()
    {
        var cart = Cart.ForCustomer(Customer, Money.Inr, Morning, Lifetime);

        var line = cart.Add(ListingA, Vendor, 1, 100m, maxQuantity: 10, Morning, Lifetime);
        line.SetSavedForLater(true);

        cart.Add(ListingA, Vendor, 1, 100m, maxQuantity: 10, Morning, Lifetime);

        Assert.False(cart.Lines[0].SavedForLater);
        Assert.Equal(2, cart.Lines[0].Quantity);
    }

    [Fact]
    public void The_badge_count_ignores_lines_set_aside()
    {
        var cart = Cart.ForCustomer(Customer, Money.Inr, Morning, Lifetime);

        cart.Add(ListingA, Vendor, 1, 100m, maxQuantity: 10, Morning, Lifetime);
        var second = cart.Add(ListingB, OtherVendor, 1, 250m, maxQuantity: 10, Morning, Lifetime);

        second.SetSavedForLater(true);
        cart.Touch(Morning, Lifetime);

        Assert.Equal(2, cart.Lines.Count);
        Assert.Equal(1, cart.LineCount);
    }

    [Fact]
    public void A_merge_sums_a_shared_line_and_carries_the_rest_across()
    {
        var own = Cart.ForCustomer(Customer, Money.Inr, Morning, Lifetime);
        own.Add(ListingA, Vendor, 1, 100m, maxQuantity: 10, Morning, Lifetime);

        var guest = Cart.ForGuest("hash", Money.Inr, Morning, Lifetime);
        guest.Add(ListingA, Vendor, 2, 100m, maxQuantity: 10, Morning, Lifetime);
        guest.Add(ListingB, OtherVendor, 1, 250m, maxQuantity: 10, Morning, Lifetime);

        own.MergeFrom(guest, maxQuantityPerLine: 10, Morning, Lifetime);

        Assert.Equal(2, own.Lines.Count);
        Assert.Equal(3, own.Lines.First(line => line.ListingId == ListingA).Quantity);
        Assert.Equal(1, own.Lines.First(line => line.ListingId == ListingB).Quantity);
    }

    [Fact]
    public void A_merge_clamps_the_summed_quantity()
    {
        var own = Cart.ForCustomer(Customer, Money.Inr, Morning, Lifetime);
        own.Add(ListingA, Vendor, 8, 100m, maxQuantity: 10, Morning, Lifetime);

        var guest = Cart.ForGuest("hash", Money.Inr, Morning, Lifetime);
        guest.Add(ListingA, Vendor, 7, 100m, maxQuantity: 10, Morning, Lifetime);

        own.MergeFrom(guest, maxQuantityPerLine: 10, Morning, Lifetime);

        Assert.Equal(10, own.Lines[0].Quantity);
    }

    [Fact]
    public void A_merge_never_overwrites_a_coupon_the_shopper_already_typed()
    {
        var own = Cart.ForCustomer(Customer, Money.Inr, Morning, Lifetime);
        own.SetCoupon("SIGNEDIN", Morning, Lifetime);

        var guest = Cart.ForGuest("hash", Money.Inr, Morning, Lifetime);
        guest.SetCoupon("GUEST", Morning, Lifetime);

        own.MergeFrom(guest, maxQuantityPerLine: 10, Morning, Lifetime);

        Assert.Equal("SIGNEDIN", own.CouponCode);
    }

    [Fact]
    public void A_merge_carries_the_guest_coupon_when_there_is_none_of_its_own()
    {
        var own = Cart.ForCustomer(Customer, Money.Inr, Morning, Lifetime);

        var guest = Cart.ForGuest("hash", Money.Inr, Morning, Lifetime);
        guest.SetCoupon("guest10", Morning, Lifetime);

        own.MergeFrom(guest, maxQuantityPerLine: 10, Morning, Lifetime);

        Assert.Equal("GUEST10", own.CouponCode);
    }

    [Fact]
    public void Claiming_a_guest_basket_drops_the_token_that_reached_it()
    {
        var guest = Cart.ForGuest("hash", Money.Inr, Morning, Lifetime);

        guest.AttachTo(Customer, Morning, Lifetime);

        Assert.Equal(Customer, guest.CustomerId);
        Assert.Null(guest.AnonymousTokenHash);
    }

    [Fact]
    public void A_basket_is_abandoned_once_and_not_once_per_sweep()
    {
        var cart = Cart.ForCustomer(Customer, Money.Inr, Morning, Lifetime);

        Assert.True(cart.MarkAbandoned(Morning));
        Assert.False(cart.MarkAbandoned(Morning.AddHours(1)));
    }

    [Fact]
    public void A_converted_basket_is_never_retired_over_the_top()
    {
        var cart = Cart.ForCustomer(Customer, Money.Inr, Morning, Lifetime);
        cart.MarkConverted(Guid.CreateVersion7(), Morning);

        Assert.False(cart.MarkExpired(Morning.AddDays(90)));
        Assert.Equal(CartStatus.Converted, cart.Status);
    }
}
