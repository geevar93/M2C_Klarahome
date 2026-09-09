using KlaraHome.Contracts.Orders;
using KlaraHome.Modules.Returns.Domain;
using KlaraHome.Modules.Returns.Infrastructure.Refunds;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The RMA transition table and the refund arithmetic, proved directly against the module's own
/// types rather than through the API — the pure half of Step 17's "unit + integration" debt rows.
/// </summary>
/// <remarks>
/// No database and no host: <see cref="ReturnLifecycle"/> is a static table and
/// <see cref="ReturnRefundCalculator"/> is arithmetic on frozen numbers, so both are provable without
/// either. The routes that drive the same table through the API live in
/// <see cref="ReturnWorkflowTests"/>.
/// </remarks>
public sealed class ReturnLifecycleTests
{
    [Fact]
    public void The_table_refuses_every_edge_it_does_not_have()
    {
        Assert.False(ReturnLifecycle.Exists(ReturnStatus.Requested, ReturnStatus.Refunded));
        Assert.False(ReturnLifecycle.Exists(ReturnStatus.Received, ReturnStatus.Requested));
        Assert.False(ReturnLifecycle.Exists(ReturnStatus.Rejected, ReturnStatus.Approved));
        Assert.False(ReturnLifecycle.Exists(ReturnStatus.Closed, ReturnStatus.Refunded));

        // The edges the card promises do exist.
        Assert.True(ReturnLifecycle.Exists(ReturnStatus.Requested, ReturnStatus.Approved));
        Assert.True(ReturnLifecycle.Exists(ReturnStatus.Approved, ReturnStatus.PickupScheduled));
        Assert.True(ReturnLifecycle.Exists(ReturnStatus.PickupScheduled, ReturnStatus.Picked));
        Assert.True(ReturnLifecycle.Exists(ReturnStatus.Picked, ReturnStatus.InTransit));
        Assert.True(ReturnLifecycle.Exists(ReturnStatus.InTransit, ReturnStatus.Received));
        Assert.True(ReturnLifecycle.Exists(ReturnStatus.Received, ReturnStatus.QcPassed));
        Assert.True(ReturnLifecycle.Exists(ReturnStatus.Received, ReturnStatus.QcFailed));
        Assert.True(ReturnLifecycle.Exists(ReturnStatus.QcPassed, ReturnStatus.Refunded));
        Assert.True(ReturnLifecycle.Exists(ReturnStatus.QcPassed, ReturnStatus.Replaced));
        Assert.True(ReturnLifecycle.Exists(ReturnStatus.Refunded, ReturnStatus.Closed));
    }

    [Fact]
    public void A_vendor_cannot_grade_their_own_return()
    {
        Assert.False(ReturnLifecycle.IsAllowed(ReturnStatus.Received, ReturnStatus.QcPassed, ReturnActor.Vendor));
        Assert.False(ReturnLifecycle.IsAllowed(ReturnStatus.Received, ReturnStatus.QcFailed, ReturnActor.Vendor));

        // Nor may the customer, obviously — only the platform grades.
        Assert.False(ReturnLifecycle.IsAllowed(ReturnStatus.Received, ReturnStatus.QcPassed, ReturnActor.Customer));
        Assert.True(ReturnLifecycle.IsAllowed(ReturnStatus.Received, ReturnStatus.QcPassed, ReturnActor.Platform));
    }

    [Fact]
    public void A_shopper_cannot_cancel_after_collection()
    {
        // Before collection, the shopper may withdraw.
        Assert.True(ReturnLifecycle.IsBeforeCollection(ReturnStatus.Requested));
        Assert.True(ReturnLifecycle.IsBeforeCollection(ReturnStatus.Approved));
        Assert.True(ReturnLifecycle.IsBeforeCollection(ReturnStatus.PickupScheduled));

        // Once the courier has it, there is no edge to Cancelled from any later state, and the
        // customer holds none of the edges that do exist from there.
        Assert.False(ReturnLifecycle.IsBeforeCollection(ReturnStatus.Picked));
        Assert.False(ReturnLifecycle.Exists(ReturnStatus.Picked, ReturnStatus.Cancelled));
        Assert.False(ReturnLifecycle.Exists(ReturnStatus.Received, ReturnStatus.Cancelled));
        Assert.False(ReturnLifecycle.Exists(ReturnStatus.QcPassed, ReturnStatus.Cancelled));
    }

    [Fact]
    public void An_admin_screen_is_offered_exactly_the_buttons_that_will_work()
    {
        var next = ReturnLifecycle.NextFor(ReturnStatus.Requested, ReturnActor.Platform);

        Assert.Contains(ReturnStatus.Approved, next);
        Assert.Contains(ReturnStatus.Rejected, next);
        Assert.DoesNotContain(ReturnStatus.Refunded, next);

        // A vendor grading their own goods is not offered the QC buttons at all.
        var vendorButtons = ReturnLifecycle.NextFor(ReturnStatus.Received, ReturnActor.Vendor);
        Assert.Empty(vendorButtons);
    }

    [Fact]
    public void Terminal_states_have_no_way_out()
    {
        Assert.True(ReturnLifecycle.IsTerminal(ReturnStatus.Rejected));
        Assert.True(ReturnLifecycle.IsTerminal(ReturnStatus.Closed));
        Assert.True(ReturnLifecycle.IsTerminal(ReturnStatus.Cancelled));

        Assert.Empty(ReturnLifecycle.NextFor(ReturnStatus.Closed, ReturnActor.Anyone));
    }

    [Fact]
    public void Proportional_tax_on_a_partial_return_credits_the_accepted_share_exactly()
    {
        // Five units bought for a line whose tax was charged and frozen at placement: 500 taxable,
        // 45 CGST, 45 SGST, 0 IGST, 0 cess, 590 line total.
        var line = new ReturnableLine(
            OrderLineId: Guid.NewGuid(),
            ListingId: Guid.NewGuid(),
            VariantId: Guid.NewGuid(),
            Sku: "SKU-1",
            Name: "Cushion Cover",
            ImageFileId: null,
            HsnCode: "630222",
            Quantity: 5,
            QuantityCancelled: 0,
            QuantityReturned: 0,
            UnitPrice: 118m,
            TaxableValue: 500m,
            GstRate: 18m,
            Cgst: 45m,
            Sgst: 45m,
            Igst: 0m,
            Cess: 0m,
            LineTotal: 590m,
            IsReturnable: true,
            ReturnWindowDays: 7);

        // Three of five units come back.
        var breakdown = ReturnRefundCalculator.ForUnits(line, 3);

        Assert.Equal(300m, breakdown.TaxableValue);
        Assert.Equal(27m, breakdown.Cgst);
        Assert.Equal(27m, breakdown.Sgst);
        Assert.Equal(0m, breakdown.Igst);
        Assert.Equal(354m, breakdown.GoodsTotal);
        Assert.Equal(354m, breakdown.Payable);

        // Combine adds the collection fee and any freight, and the total must equal the sum of the
        // lines plus freight less the fee — the whole point of raising the credit note this way.
        var order = new SubOrderReturnView(
            OrderId: Guid.NewGuid(),
            OrderNumber: "ORD-1",
            SubOrderId: Guid.NewGuid(),
            SubOrderNumber: "SUB-1",
            VendorId: Guid.NewGuid(),
            VendorName: "Test Seller",
            VendorGstin: null,
            CustomerId: Guid.NewGuid(),
            Status: "Delivered",
            PaymentMethod: "Prepaid",
            IsPaid: true,
            ShippingTotal: 59m,
            ShippingTax: 9m,
            Total: 649m,
            CurrencyCode: "INR",
            PlaceOfSupplyStateId: null,
            IsIntraState: true,
            DeliveredAt: DateTimeOffset.UtcNow.AddDays(-1),
            ReturnWindowEndsAt: DateTimeOffset.UtcNow.AddDays(6),
            InvoiceId: null,
            InvoiceNumber: null,
            PickupAddress: null,
            Lines: [line]);

        var combined = ReturnRefundCalculator.Combine(
            [breakdown],
            order,
            isFullReturn: false,
            refundShipping: true,
            returnShippingFee: 40m);

        // Not a full return, so no freight is credited; the fee is deducted from the goods alone.
        Assert.Equal(0m, combined.ShippingRefund);
        Assert.Equal(40m, combined.ReturnShippingFee);
        Assert.Equal(354m - 40m, combined.Payable);
        Assert.Equal(combined.GoodsTotal + combined.ShippingRefund - combined.ReturnShippingFee, combined.Payable);
    }

    [Fact]
    public void A_full_return_credits_the_original_freight_when_the_store_says_so()
    {
        var line = new ReturnableLine(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SKU-2", "Table Runner", null, "630222",
            2, 0, 0, 590m, 1000m, 18m, 90m, 90m, 0m, 0m, 1180m, true, 7);

        var breakdown = ReturnRefundCalculator.ForUnits(line, 2);

        var order = new SubOrderReturnView(
            Guid.NewGuid(), "ORD-2", Guid.NewGuid(), "SUB-2", Guid.NewGuid(), "Seller", null,
            Guid.NewGuid(), "Delivered", "Prepaid", true, 59m, 9m, 1239m, "INR", null, true,
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(6), null, null, null, [line]);

        // Every unit of the line is returned, so this is a full return of the seller's part.
        var combined = ReturnRefundCalculator.Combine([breakdown], order, isFullReturn: true, refundShipping: true, returnShippingFee: 0m);

        Assert.Equal(59m, combined.ShippingRefund);
        Assert.Equal(1180m + 59m, combined.Payable);
    }
}
