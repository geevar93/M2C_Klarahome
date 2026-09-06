using KlaraHome.Contracts.Inventory;
using KlaraHome.Modules.Inventory.Domain;

namespace KlaraHome.UnitTests.Inventory;

/// <summary>
/// The availability arithmetic and the low-stock alert (docs/02-domain-model.md §4.2).
/// </summary>
/// <remarks>
/// Tested while writing it under the build sprint's rule 1. Two things here are cheap to test and
/// expensive to get wrong: <c>available = on hand − reserved</c> floored at zero, which is the
/// number every shopper-facing surface renders, and the alert-on-crossing rule, whose failure mode
/// is not an error but a seller receiving one email per sale until they stop reading them.
/// </remarks>
public sealed class StockItemTests
{
    private static readonly DateTimeOffset Morning = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid Listing = Guid.Parse("00000000-0000-0000-0000-00000000110a");
    private static readonly Guid Warehouse = Guid.Parse("00000000-0000-0000-0000-00000000110b");
    private static readonly Guid Vendor = Guid.Parse("00000000-0000-0000-0000-00000000110c");

    [Fact]
    public void A_new_stock_item_starts_empty_and_unavailable()
    {
        var item = Open();

        Assert.Equal(0, item.QuantityOnHand);
        Assert.Equal(0, item.QuantityReserved);
        Assert.Equal(0, item.QuantityAvailable);
        Assert.False(item.IsAvailable);

        // A reorder level of zero means "do not alert", not "alert when empty".
        Assert.False(item.IsLow);
    }

    [Fact]
    public void Available_is_on_hand_less_reserved()
    {
        var item = Open();
        item.SyncQuantities(quantityOnHand: 10, quantityReserved: 4);

        Assert.Equal(6, item.QuantityAvailable);
        Assert.True(item.IsAvailable);
    }

    [Fact]
    public void Available_is_floored_at_zero_when_a_backordered_row_is_in_deficit()
    {
        var item = Open();
        item.Configure(0, 0, allowBackorder: true, allowPreorder: false, null, StockTrackingMode.None);
        item.SyncQuantities(quantityOnHand: 2, quantityReserved: 7);

        // Negative availability is a number no caller wants to reason about, and summing it across
        // warehouses would let one deficit eat into what another location genuinely has.
        Assert.Equal(0, item.QuantityAvailable);

        // Still sellable: that is exactly what the backorder flag means.
        Assert.True(item.IsAvailable);
    }

    [Fact]
    public void An_empty_row_is_available_only_when_backorder_or_preorder_says_so()
    {
        var plain = Open();
        Assert.False(plain.IsAvailable);

        var preorder = Open();
        preorder.Configure(0, 0, false, allowPreorder: true, Morning, StockTrackingMode.None);
        Assert.True(preorder.IsAvailable);
    }

    [Fact]
    public void A_preorder_date_is_dropped_when_the_flag_is_turned_off()
    {
        var item = Open();
        item.Configure(0, 0, false, allowPreorder: true, Morning, StockTrackingMode.None);
        Assert.Equal(Morning, item.PreorderAvailableAt);

        item.Configure(0, 0, false, allowPreorder: false, Morning, StockTrackingMode.None);

        // A date that outlives its flag is how a storefront promises a ship date for stock already
        // on the shelf.
        Assert.Null(item.PreorderAvailableAt);
    }

    [Fact]
    public void The_low_stock_alert_fires_once_on_the_crossing_and_not_on_every_sale()
    {
        var item = Open();
        item.Configure(5, 20, false, false, null, StockTrackingMode.None);
        item.SyncQuantities(quantityOnHand: 5, quantityReserved: 0);

        Assert.True(item.IsLow);
        Assert.True(item.TryRaiseLowStock(Morning));

        // Two further sales below the level. Neither is news.
        item.SyncQuantities(quantityOnHand: 4, quantityReserved: 0);
        Assert.False(item.TryRaiseLowStock(Morning.AddMinutes(1)));

        item.SyncQuantities(quantityOnHand: 3, quantityReserved: 0);
        Assert.False(item.TryRaiseLowStock(Morning.AddMinutes(2)));
    }

    [Fact]
    public void The_low_stock_alert_arms_again_once_the_item_is_replenished()
    {
        var item = Open();
        item.Configure(5, 20, false, false, null, StockTrackingMode.None);
        item.SyncQuantities(5, 0);

        Assert.True(item.TryRaiseLowStock(Morning));

        // Back above the level: the flag clears, and the next crossing is news again.
        item.SyncQuantities(50, 0);
        Assert.False(item.TryRaiseLowStock(Morning.AddHours(1)));
        Assert.Null(item.LowStockNotifiedAt);

        item.SyncQuantities(2, 0);
        Assert.True(item.TryRaiseLowStock(Morning.AddHours(2)));
    }

    [Fact]
    public void Raising_the_reorder_level_above_the_current_count_rearms_the_alert()
    {
        var item = Open();
        item.Configure(5, 20, false, false, null, StockTrackingMode.None);
        item.SyncQuantities(50, 0);

        Assert.False(item.IsLow);

        // The seller just moved the goalposts. Whether this item is low is now a different
        // question, so the previous answer must not suppress the next alert.
        item.Configure(80, 20, false, false, null, StockTrackingMode.None);

        Assert.True(item.IsLow);
        Assert.True(item.TryRaiseLowStock(Morning));
    }

    [Fact]
    public void Reserved_units_count_against_the_low_stock_threshold()
    {
        var item = Open();
        item.Configure(5, 20, false, false, null, StockTrackingMode.None);

        // Ten on the shelf but eight of them spoken for. Two are sellable, and the seller needs to
        // know now rather than after the holds convert.
        item.SyncQuantities(quantityOnHand: 10, quantityReserved: 8);

        Assert.True(item.IsLow);
    }

    [Theory]
    [InlineData(0, 0, false, false, 3, false)]
    [InlineData(3, 0, false, false, 3, true)]
    [InlineData(3, 1, false, false, 3, false)]
    [InlineData(0, 0, true, false, 99, true)]
    [InlineData(0, 0, false, true, 99, true)]
    public void CanFulfil_answers_the_question_a_cart_actually_has(
        int onHand,
        int reserved,
        bool backorder,
        bool preorder,
        int wanted,
        bool expected)
    {
        var availability = new StockAvailability(
            Listing,
            onHand,
            reserved,
            Math.Max(0, onHand - reserved),
            backorder,
            preorder,
            PreorderAvailableAt: null,
            IsTracked: true);

        Assert.Equal(expected, availability.CanFulfil(wanted));
    }

    [Fact]
    public void An_untracked_offer_can_never_be_fulfilled()
    {
        // "No stock row" is not the same as zero: it means Inventory has never heard of the offer,
        // and selling something nobody has stocked is the mistake this flag exists to prevent.
        var untracked = new StockAvailability(Listing, 0, 0, 0, true, true, null, IsTracked: false);

        Assert.False(untracked.CanFulfil(1));
    }

    private static StockItem Open() => StockItem.Open(Listing, Warehouse, Vendor, "SKU-000001");
}
