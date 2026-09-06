using KlaraHome.Modules.Orders.Domain;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.UnitTests.Orders;

/// <summary>
/// What a cancellation does to a line's counters and to the money coming off the bill.
/// </summary>
/// <remarks>
/// Tested while writing it under the build sprint's rule 1. It is arithmetic on money: a
/// pro-rata refund computed the obvious way — quantity times unit price — over-refunds every line
/// that carried an allocated order discount, and the error is invisible until somebody reconciles
/// a settlement.
/// </remarks>
public sealed class OrderCancellationTests
{
    private static readonly Guid SubOrder = Guid.Parse("00000000-0000-0000-0000-0000000014a1");
    private static readonly Guid Vendor = Guid.Parse("00000000-0000-0000-0000-0000000014a2");
    private static readonly Guid Listing = Guid.Parse("00000000-0000-0000-0000-0000000014a3");

    [Fact]
    public void Cancelling_some_units_leaves_the_line_partially_cancelled()
    {
        var line = Line(quantity: 5, lineTotal: 500m);

        Assert.Equal(2, line.Cancel(2));

        Assert.Equal(2, line.QuantityCancelled);
        Assert.Equal(3, line.QuantityLive);
        Assert.Equal(OrderLineStatus.PartiallyCancelled, line.Status);
        Assert.False(line.IsFullyCancelled);
    }

    [Fact]
    public void Cancelling_every_unit_closes_the_line()
    {
        var line = Line(quantity: 5, lineTotal: 500m);

        line.Cancel(5);

        Assert.Equal(OrderLineStatus.Cancelled, line.Status);
        Assert.True(line.IsFullyCancelled);
        Assert.Equal(0, line.QuantityLive);
    }

    /// <summary>
    /// Clamped rather than refused: a shopper cancelling a whole sub-order asks for everything on
    /// every line, and a request for more than remains means the same thing as a request for all
    /// of it.
    /// </summary>
    [Fact]
    public void Cancelling_more_than_remains_takes_only_what_is_left()
    {
        var line = Line(quantity: 3, lineTotal: 300m);
        line.Cancel(2);

        Assert.Equal(1, line.Cancel(9));
        Assert.Equal(3, line.QuantityCancelled);
        Assert.Equal(0, line.Cancel(1));
    }

    /// <summary>
    /// The line total is already net of the discount allocated to it, so the value of a cancelled
    /// unit is a share of that total — not the undiscounted unit price, which would give the shopper
    /// back more than they paid.
    /// </summary>
    [Fact]
    public void The_cancelled_value_is_a_share_of_the_discounted_line_total()
    {
        // Four units at 250 list, discounted to 900 for the line.
        var line = Line(quantity: 4, lineTotal: 900m, unitPrice: 250m);

        line.Cancel(1);

        Assert.Equal(225m, line.CancelledValue);
        Assert.NotEqual(line.UnitPrice, line.CancelledValue);
    }

    [Fact]
    public void A_line_with_nothing_cancelled_is_worth_nothing_back()
        => Assert.Equal(0m, Line(quantity: 2, lineTotal: 200m).CancelledValue);

    [Fact]
    public void The_cancelled_value_of_a_whole_line_is_the_whole_line_total()
    {
        var line = Line(quantity: 3, lineTotal: 999.99m);

        line.Cancel(3);

        Assert.Equal(999.99m, line.CancelledValue);
    }

    /// <summary>
    /// A third of a rupee-odd total does not divide evenly. The point of the assertion is that the
    /// rounding is defined and to four decimal places, which is what the money column stores.
    /// </summary>
    [Fact]
    public void An_uneven_share_is_rounded_to_the_stored_scale()
    {
        var line = Line(quantity: 3, lineTotal: 100m);

        line.Cancel(1);

        Assert.Equal(33.3333m, line.CancelledValue);
    }

    private static OrderLine Line(int quantity, decimal lineTotal, decimal unitPrice = 100m)
    {
        var line = OrderLine.Create(SubOrder, Vendor, Listing, "SKU-1", quantity);

        line.Capture(UuidV7.New(), new ProductSnapshot { Name = "Cotton cushion cover" });
        line.Price(
            unitMrp: unitPrice,
            unitPrice: unitPrice,
            discountAmount: (unitPrice * quantity) - lineTotal,
            taxableValue: lineTotal,
            gstRate: 18m,
            cgst: 0m,
            sgst: 0m,
            igst: 0m,
            cess: 0m,
            lineTotal: lineTotal);

        return line;
    }
}
