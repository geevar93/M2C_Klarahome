using KlaraHome.Contracts.Inventory;
using KlaraHome.Modules.Identity.Infrastructure.Seeding;
using KlaraHome.Modules.Inventory.Domain;

namespace KlaraHome.UnitTests.Inventory;

/// <summary>
/// The reservation life cycle, and the permission catalogue this module's endpoints depend on.
/// </summary>
/// <remarks>
/// Tested while writing them under the build sprint's rule 1. The settle transition is the one
/// place idempotency is decided: integration events arrive at least once, and a redelivered
/// cancellation that released the units a second time would put stock back on sale that had already
/// been sold. The permission assertion is the cheap guard on the deliberate duplication between
/// <c>InventoryPermissions</c> and the Identity catalogue — a module may not reference another, so
/// nothing but a test keeps the two lists in step.
/// </remarks>
public sealed class StockReservationTests
{
    private static readonly DateTimeOffset Morning = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid StockItem = Guid.Parse("00000000-0000-0000-0000-00000000330a");
    private static readonly Guid Listing = Guid.Parse("00000000-0000-0000-0000-00000000330b");
    private static readonly Guid Cart = Guid.Parse("00000000-0000-0000-0000-00000000330c");
    private static readonly Guid CartLine = Guid.Parse("00000000-0000-0000-0000-00000000330d");

    [Fact]
    public void A_new_hold_is_live_and_unsettled()
    {
        var reservation = Hold(quantity: 3);

        Assert.Equal(ReservationStatus.Held, reservation.Status);
        Assert.True(reservation.IsHeld);
        Assert.Null(reservation.SettledAt);
        Assert.Equal(Morning.AddMinutes(15), reservation.ExpiresAt);
    }

    [Theory]
    [InlineData((int)ReservationStatus.Committed)]
    [InlineData((int)ReservationStatus.Released)]
    [InlineData((int)ReservationStatus.Expired)]
    public void A_live_hold_settles_once_and_records_when(int status)
    {
        // Boxed through int: the enum is internal to its module, and a public test method cannot
        // take a less accessible parameter type.
        var outcome = (ReservationStatus)status;
        var reservation = Hold(3);

        Assert.True(reservation.Settle(outcome, Morning.AddMinutes(5)));
        Assert.Equal(outcome, reservation.Status);
        Assert.Equal(Morning.AddMinutes(5), reservation.SettledAt);
        Assert.False(reservation.IsHeld);
    }

    [Fact]
    public void A_settled_hold_refuses_to_settle_again()
    {
        var reservation = Hold(3);
        reservation.Settle(ReservationStatus.Committed, Morning.AddMinutes(5));

        // The false return is what makes settling idempotent under at-least-once delivery: the
        // caller reads it and moves no stock.
        Assert.False(reservation.Settle(ReservationStatus.Released, Morning.AddMinutes(9)));
        Assert.Equal(ReservationStatus.Committed, reservation.Status);
        Assert.Equal(Morning.AddMinutes(5), reservation.SettledAt);
    }

    [Fact]
    public void A_hold_cannot_be_settled_back_into_being_held()
    {
        var reservation = Hold(3);

        Assert.False(reservation.Settle(ReservationStatus.Held, Morning));
        Assert.Equal(ReservationStatus.Held, reservation.Status);
        Assert.Null(reservation.SettledAt);
    }

    [Fact]
    public void Extending_a_hold_only_ever_moves_the_expiry_forward()
    {
        var reservation = Hold(3);

        reservation.ExtendTo(Morning.AddMinutes(30));
        Assert.Equal(Morning.AddMinutes(30), reservation.ExpiresAt);

        // A shorter extension is not an extension. Accepting one would let a retry shorten a hold
        // the shopper is still using.
        reservation.ExtendTo(Morning.AddMinutes(5));
        Assert.Equal(Morning.AddMinutes(30), reservation.ExpiresAt);
    }

    [Fact]
    public void A_settled_hold_cannot_be_extended()
    {
        var reservation = Hold(3);
        reservation.Settle(ReservationStatus.Expired, Morning.AddMinutes(15));

        reservation.ExtendTo(Morning.AddHours(1));

        Assert.Equal(Morning.AddMinutes(15), reservation.ExpiresAt);
    }

    [Fact]
    public void Every_permission_the_inventory_endpoints_declare_is_in_the_catalogue()
    {
        // The duplication is what the module boundary costs: a module may not reference another, so
        // InventoryPermissions and the Identity catalogue are two lists of the same facts. A
        // permission an endpoint asks for and the catalogue does not declare is a route nobody can
        // ever be granted.
        string[] declared =
        [
            "inventory.stock.read",
            "inventory.stock.adjust",
            "inventory.warehouse.manage",
            "inventory.purchasing.manage",
            "inventory.stock-take.manage",
        ];

        Assert.All(
            declared,
            permission => Assert.True(
                PermissionCatalog.Contains(permission),
                $"Endpoint permission '{permission}' is not in the Identity catalogue."));
    }

    [Fact]
    public void The_reservation_reference_types_are_the_two_the_check_constraint_allows()
    {
        // The database restricts reference_type to these two. A third written anywhere in the
        // platform would fail on insert, in the middle of somebody's checkout.
        Assert.Equal("cart", ReservationReferenceTypes.Cart);
        Assert.Equal("order", ReservationReferenceTypes.Order);
    }

    private static StockReservation Hold(int quantity)
        => StockReservation.Hold(
            StockItem,
            Listing,
            quantity,
            ReservationReferenceTypes.Cart,
            Cart,
            CartLine,
            Morning.AddMinutes(15));
}
