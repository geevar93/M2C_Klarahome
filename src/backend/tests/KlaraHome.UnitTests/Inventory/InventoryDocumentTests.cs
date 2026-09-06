using KlaraHome.Modules.Inventory.Domain;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.UnitTests.Inventory;

/// <summary>
/// The three document life cycles this module owns, and the receipt arithmetic behind them
/// (docs/03-database-design.md §4.5).
/// </summary>
/// <remarks>
/// Tested while writing them under the build sprint's rule 1: transition tables and the
/// ordered/received/outstanding sums are exactly the kind of thing that is cheaper to assert than
/// to re-derive, and a wrong receipt status is invisible until a buyer chases a supplier for goods
/// that already arrived.
/// </remarks>
public sealed class InventoryDocumentTests
{
    private static readonly DateTimeOffset Morning = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid Supplier = Guid.Parse("00000000-0000-0000-0000-00000000220a");
    private static readonly Guid Warehouse = Guid.Parse("00000000-0000-0000-0000-00000000220b");
    private static readonly Guid Vendor = Guid.Parse("00000000-0000-0000-0000-00000000220c");
    private static readonly Guid ListingA = Guid.Parse("00000000-0000-0000-0000-00000000221a");
    private static readonly Guid ListingB = Guid.Parse("00000000-0000-0000-0000-00000000221b");

    [Fact]
    public void A_purchase_order_totals_its_lines_with_tax()
    {
        var order = Raise();

        order.SetLines([
            Line(order.Id, ListingA, quantity: 10, unitCost: 100m, taxRate: 18m),
            Line(order.Id, ListingB, quantity: 4, unitCost: 250m, taxRate: 12m),
        ]);

        Assert.Equal(2000m, order.Subtotal.Amount);
        Assert.Equal(300m, order.TaxTotal.Amount);
        Assert.Equal(2300m, order.Total.Amount);
    }

    [Fact]
    public void An_empty_purchase_order_cannot_be_submitted()
    {
        var order = Raise();

        Assert.False(order.Submit(Morning));
        Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
    }

    [Fact]
    public void Submitting_freezes_the_document()
    {
        var order = Raise();
        order.SetLines([Line(order.Id, ListingA, 10, 100m, 18m)]);

        Assert.True(order.Submit(Morning));
        Assert.Equal(PurchaseOrderStatus.Submitted, order.Status);
        Assert.Equal(Morning, order.SubmittedAt);
        Assert.False(order.IsEditable);
        Assert.True(order.IsReceivable);

        // Once, and only once. A supplier who has been sent the order and then finds the quantities
        // changed underneath them is a dispute nobody wins.
        Assert.False(order.Submit(Morning.AddHours(1)));
    }

    [Fact]
    public void A_receipt_clamps_to_what_is_still_outstanding()
    {
        var line = Line(Guid.Empty, ListingA, quantity: 10, unitCost: 100m, taxRate: 18m);

        Assert.Equal(4, line.Receive(4));
        Assert.Equal(6, line.QuantityOutstanding);

        // Ten more arrive against six outstanding. Six are booked in; the rest never happened, and
        // the check constraint that says so is never reached.
        Assert.Equal(6, line.Receive(10));
        Assert.Equal(10, line.QuantityReceived);
        Assert.Equal(0, line.QuantityOutstanding);
        Assert.True(line.IsFullyReceived);

        Assert.Equal(0, line.Receive(1));
    }

    [Fact]
    public void A_partly_filled_order_is_partially_received_and_a_filled_one_is_received()
    {
        var order = Raise();
        var first = Line(order.Id, ListingA, 10, 100m, 18m);
        var second = Line(order.Id, ListingB, 5, 250m, 12m);

        order.SetLines([first, second]);
        order.Submit(Morning);

        first.Receive(10);
        order.RefreshReceiptStatus();
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, order.Status);

        second.Receive(5);
        order.RefreshReceiptStatus();
        Assert.Equal(PurchaseOrderStatus.Received, order.Status);

        // Terminal: a further receipt cannot walk it backwards.
        order.RefreshReceiptStatus();
        Assert.Equal(PurchaseOrderStatus.Received, order.Status);
    }

    [Fact]
    public void A_submitted_order_with_nothing_received_stays_submitted()
    {
        var order = Raise();
        order.SetLines([Line(order.Id, ListingA, 10, 100m, 18m)]);
        order.Submit(Morning);

        order.RefreshReceiptStatus();

        Assert.Equal(PurchaseOrderStatus.Submitted, order.Status);
    }

    [Fact]
    public void An_order_cannot_be_cancelled_once_goods_have_arrived()
    {
        var order = Raise();
        var line = Line(order.Id, ListingA, 10, 100m, 18m);

        order.SetLines([line]);
        order.Submit(Morning);

        line.Receive(3);
        order.RefreshReceiptStatus();

        // The units already on the shelf are real, and this document is the only record of where
        // they came from.
        Assert.False(order.Cancel());
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, order.Status);
    }

    [Fact]
    public void A_draft_or_submitted_order_can_be_cancelled_once()
    {
        var draft = Raise();
        Assert.True(draft.Cancel());
        Assert.Equal(PurchaseOrderStatus.Cancelled, draft.Status);
        Assert.False(draft.Cancel());

        var sent = Raise();
        sent.SetLines([Line(sent.Id, ListingA, 1, 10m, 0m)]);
        sent.Submit(Morning);
        Assert.True(sent.Cancel());
    }

    [Fact]
    public void A_goods_receipt_posts_once()
    {
        var receipt = GoodsReceipt.Open("GRN-000001", Guid.NewGuid(), Warehouse, Vendor, Morning, null);

        Assert.Equal(GoodsReceiptStatus.Draft, receipt.Status);
        Assert.True(receipt.Post(Morning));
        Assert.Equal(GoodsReceiptStatus.Posted, receipt.Status);

        // Posting twice would double the stock, and un-posting would leave a ledger entry pointing
        // at a document that denies it.
        Assert.False(receipt.Post(Morning.AddHours(1)));
    }

    [Fact]
    public void A_stock_take_freezes_the_book_figure_and_computes_the_variance()
    {
        var take = StockTake.Schedule("STK-000001", Warehouse, Vendor, Morning, null);
        var itemId = Guid.NewGuid();

        Assert.True(take.BeginCounting([StockTakeLine.Expect(take.Id, itemId, "SKU-000001", 40)]));
        Assert.Equal(StockTakeStatus.Counting, take.Status);

        var line = take.Lines.Single();
        Assert.Null(line.CountedQuantity);
        Assert.Null(line.Variance);

        line.Count(37, "three missing from the top shelf");

        Assert.Equal(37, line.CountedQuantity);
        Assert.Equal(-3, line.Variance);
    }

    [Fact]
    public void A_stock_take_cannot_reopen_counting_or_submit_twice()
    {
        var take = StockTake.Schedule("STK-000002", Warehouse, Vendor, null, null);
        take.BeginCounting([StockTakeLine.Expect(take.Id, Guid.NewGuid(), "SKU-000002", 5)]);

        // Reopening would replace the frozen book figures, and every sale since would become a
        // phantom variance.
        Assert.False(take.BeginCounting([]));

        Assert.True(take.Submit(Morning, null));
        Assert.Equal(StockTakeStatus.Submitted, take.Status);
        Assert.False(take.Submit(Morning.AddHours(1), null));
        Assert.False(take.Cancel());
    }

    [Fact]
    public void A_stock_take_cannot_be_submitted_before_counting_opens()
    {
        var take = StockTake.Schedule("STK-000003", Warehouse, Vendor, null, null);

        Assert.False(take.Submit(Morning, null));
        Assert.Equal(StockTakeStatus.Draft, take.Status);

        Assert.True(take.Cancel());
        Assert.Equal(StockTakeStatus.Cancelled, take.Status);
    }

    private static PurchaseOrder Raise()
        => PurchaseOrder.Raise("PO-000001", Supplier, Warehouse, Vendor);

    private static PurchaseOrderLine Line(
        Guid orderId,
        Guid listingId,
        int quantity,
        decimal unitCost,
        decimal taxRate)
        => PurchaseOrderLine.Add(
            orderId,
            listingId,
            "SKU-000001",
            "A cushion cover",
            quantity,
            Money.Rupees(unitCost),
            taxRate);
}
