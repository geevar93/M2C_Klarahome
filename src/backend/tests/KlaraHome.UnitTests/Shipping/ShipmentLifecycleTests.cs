using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure.Courier.Shiprocket;

namespace KlaraHome.UnitTests.Shipping;

/// <summary>
/// What a consignment does when a courier says things twice, late, or in the wrong order.
/// </summary>
/// <remarks>
/// Tested while writing it under the build sprint's rule 1. A courier will report a delivery on a
/// parcel it already returned, an "out for delivery" on one it never scanned into transit, and the
/// same scan three times in an afternoon — and every one of those failures is silent. The machine is
/// what stops them becoming nonsense in a column somebody eventually shows a customer.
/// </remarks>
public sealed class ShipmentLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Forward_movement_may_skip_states()
    {
        // Couriers routinely report "out for delivery" on a parcel they never scanned into transit.
        // A machine that refused that would strand the parcel rather than the scan.
        Assert.True(ShipmentLifecycle.IsTransitionAllowed(ShipmentStatus.Created, ShipmentStatus.OutForDelivery));
        Assert.True(ShipmentLifecycle.IsTransitionAllowed(ShipmentStatus.PickedUp, ShipmentStatus.Delivered));
    }

    [Fact]
    public void Backward_movement_is_refused()
    {
        Assert.False(ShipmentLifecycle.IsTransitionAllowed(ShipmentStatus.OutForDelivery, ShipmentStatus.InTransit));
        Assert.False(ShipmentLifecycle.IsTransitionAllowed(ShipmentStatus.PickedUp, ShipmentStatus.Created));
    }

    [Fact]
    public void Nothing_moves_out_of_a_terminal_state()
    {
        foreach (var terminal in ShipmentLifecycle.Terminal)
        {
            foreach (var status in Enum.GetValues<ShipmentStatus>())
            {
                Assert.False(ShipmentLifecycle.IsTransitionAllowed(terminal, status));
            }
        }
    }

    [Fact]
    public void A_parcel_in_exception_can_move_again()
    {
        // The correction that matters: a failed attempt is not the end of the parcel's life, and the
        // next scan is the courier telling us where it now is.
        Assert.True(ShipmentLifecycle.IsTransitionAllowed(ShipmentStatus.Exception, ShipmentStatus.OutForDelivery));
        Assert.True(ShipmentLifecycle.IsTransitionAllowed(ShipmentStatus.Exception, ShipmentStatus.Delivered));
        Assert.True(ShipmentLifecycle.IsTransitionAllowed(ShipmentStatus.Exception, ShipmentStatus.RtoInitiated));
    }

    [Fact]
    public void A_parcel_marked_for_return_can_still_be_delivered()
    {
        // It happens more often than it should, and refusing it would leave the shopper holding
        // goods the platform says are coming back.
        Assert.True(ShipmentLifecycle.IsTransitionAllowed(ShipmentStatus.RtoInitiated, ShipmentStatus.Delivered));
        Assert.True(ShipmentLifecycle.IsTransitionAllowed(ShipmentStatus.RtoInitiated, ShipmentStatus.RtoDelivered));
    }

    [Fact]
    public void Cancelling_is_possible_only_until_the_courier_has_it()
    {
        Assert.True(ShipmentLifecycle.IsTransitionAllowed(ShipmentStatus.Draft, ShipmentStatus.Cancelled));
        Assert.True(ShipmentLifecycle.IsTransitionAllowed(ShipmentStatus.PickupScheduled, ShipmentStatus.Cancelled));

        // After collection it is a return, not a cancellation, and it costs money either way.
        Assert.False(ShipmentLifecycle.IsTransitionAllowed(ShipmentStatus.PickedUp, ShipmentStatus.Cancelled));
        Assert.False(ShipmentLifecycle.IsTransitionAllowed(ShipmentStatus.InTransit, ShipmentStatus.Cancelled));
    }

    [Fact]
    public void A_repeated_delivery_scan_does_not_move_the_delivery_time()
    {
        var shipment = Booked();

        Assert.True(shipment.Advance(ShipmentStatus.Delivered, Now, reason: null));
        Assert.Equal(Now, shipment.DeliveredAt);

        // The return window is measured from this instant, so a courier repeating itself an hour
        // later must not move it. The second call is refused outright — the state is terminal.
        Assert.False(shipment.Advance(ShipmentStatus.Delivered, Now.AddHours(1), reason: null));
        Assert.Equal(Now, shipment.DeliveredAt);
    }

    [Fact]
    public void Each_failed_attempt_counts()
    {
        var shipment = Booked();

        shipment.Advance(ShipmentStatus.OutForDelivery, Now, reason: null);
        shipment.Advance(ShipmentStatus.Exception, Now.AddHours(2), "Customer not available");
        shipment.Advance(ShipmentStatus.OutForDelivery, Now.AddDays(1), reason: null);
        shipment.Advance(ShipmentStatus.Exception, Now.AddDays(1).AddHours(2), "Customer not available");

        // The attempt counter is what a failed-delivery report is numbered by, and the unique index
        // on (shipment, attempt) is what stops a repeated scan filling the queue.
        Assert.Equal(2, shipment.DeliveryAttempts);
    }

    [Fact]
    public void A_booked_parcel_cannot_be_booked_again()
    {
        var shipment = Booked();

        // A second waybill for one box would leave two consignments in the world and only one of
        // them tracked.
        Assert.False(shipment.Book(
            "aggregator",
            "Other Courier",
            serviceName: null,
            "OTHER-1",
            providerShipmentId: null,
            trackingUrl: null,
            expectedDeliveryAt: null,
            freightCost: null,
            Now));

        Assert.Equal("AWB-1", shipment.Awb);
    }

    [Fact]
    public void A_booked_parcel_cannot_be_repacked_or_reweighed()
    {
        var shipment = Booked();

        // The courier priced the consignment on the figures it was given; changing them afterwards
        // would leave this platform's record disagreeing with the waybill a dispute is argued from.
        Assert.False(shipment.Unpack());
        Assert.False(shipment.CaptureWeight(9_000, 10m, 10m, 10m));
    }

    [Fact]
    public void The_chargeable_weight_is_the_greater_of_dead_and_volumetric()
    {
        var shipment = Drafted();

        shipment.Pack(ShipmentLine.Pack(shipment.Id, Guid.NewGuid(), "SKU-1", "Cushion", 2, 300, 800m));
        shipment.CaptureWeight(600, lengthCm: 40m, widthCm: 30m, heightCm: 20m);

        // 600 g on the scale, 4.8 kg by volume. A courier charges the second, and so does the rate
        // card — which is the whole reason both are kept.
        Assert.Equal(4_800, shipment.ChargeableWeightGrams(5_000));
    }

    [Fact]
    public void An_unweighed_parcel_falls_back_to_what_its_contents_weigh()
    {
        var shipment = Drafted();

        shipment.Pack(ShipmentLine.Pack(shipment.Id, Guid.NewGuid(), "SKU-1", "Cushion", 3, 250, 900m));

        // A quote can be given before anything is packed. The estimate is never written to the
        // measured weight: an estimate recorded as a measurement is how a weight dispute is lost.
        Assert.Equal(750, shipment.ChargeableWeightGrams(5_000));
        Assert.Equal(0, shipment.WeightGrams);
    }

    [Fact]
    public void Only_the_states_a_customer_would_recognise_reach_the_order()
    {
        Assert.Equal("Shipped", ShipmentLifecycle.OrderStatusFor(ShipmentStatus.PickedUp));
        Assert.Equal("OutForDelivery", ShipmentLifecycle.OrderStatusFor(ShipmentStatus.OutForDelivery));
        Assert.Equal("Delivered", ShipmentLifecycle.OrderStatusFor(ShipmentStatus.Delivered));
        Assert.Equal("DeliveryFailed", ShipmentLifecycle.OrderStatusFor(ShipmentStatus.Exception));

        // A label and a pickup slot mean nothing to a shopper and nothing to the order machine.
        Assert.Null(ShipmentLifecycle.OrderStatusFor(ShipmentStatus.LabelGenerated));
        Assert.Null(ShipmentLifecycle.OrderStatusFor(ShipmentStatus.PickupScheduled));
        Assert.Null(ShipmentLifecycle.OrderStatusFor(ShipmentStatus.Draft));
    }

    [Fact]
    public void A_couriers_own_words_are_translated_and_a_return_beats_a_delivery()
    {
        Assert.Equal(ShipmentStatus.Delivered, ShiprocketStatusMap.ToShipmentStatus("DELIVERED"));
        Assert.Equal(ShipmentStatus.OutForDelivery, ShiprocketStatusMap.ToShipmentStatus("Out For Delivery"));
        Assert.Equal(ShipmentStatus.Exception, ShiprocketStatusMap.ToShipmentStatus("UNDELIVERED"));

        // "RTO Delivered" contains "delivered", and reading it as a delivery would tell a shopper
        // their parcel arrived when it is back with the seller.
        Assert.Equal(ShipmentStatus.RtoDelivered, ShiprocketStatusMap.ToShipmentStatus("RTO DELIVERED"));
        Assert.Equal(ShipmentStatus.RtoInitiated, ShiprocketStatusMap.ToShipmentStatus("RTO Initiated"));

        // Anything unrecognised is still evidence the parcel is moving, and InTransit is the state
        // nothing irreversible hangs off.
        Assert.Equal(ShipmentStatus.InTransit, ShiprocketStatusMap.ToShipmentStatus("Reached Hub XYZ"));
        Assert.Equal(ShipmentStatus.InTransit, ShiprocketStatusMap.ToShipmentStatus(null));
    }

    [Fact]
    public void A_failure_reason_is_categorised_so_the_queue_can_be_worked()
    {
        Assert.Equal(NdrReasonCode.CustomerUnavailable, ShiprocketStatusMap.ToNdrReason("Customer not available"));
        Assert.Equal(NdrReasonCode.AddressIncorrect, ShiprocketStatusMap.ToNdrReason("Incomplete address"));
        Assert.Equal(NdrReasonCode.CodNotReady, ShiprocketStatusMap.ToNdrReason("COD not ready"));
        Assert.Equal(NdrReasonCode.Refused, ShiprocketStatusMap.ToNdrReason("Rejected by customer"));
        Assert.Equal(NdrReasonCode.Other, ShiprocketStatusMap.ToNdrReason("Something nobody has seen before"));
    }

    [Fact]
    public void The_same_scan_reported_twice_produces_the_same_deduplication_id()
    {
        // A webhook and the polling fallback routinely carry the same scan. The synthetic id is what
        // makes the second one free rather than a second timeline entry.
        var first = TrackingEvent.SyntheticId("Out For Delivery", Now);
        var second = TrackingEvent.SyntheticId("out for delivery", Now);

        Assert.Equal(first, second);
        Assert.NotEqual(first, TrackingEvent.SyntheticId("Delivered", Now));
        Assert.NotEqual(first, TrackingEvent.SyntheticId("Out For Delivery", Now.AddSeconds(1)));
    }

    [Fact]
    public void Only_a_booked_unfinished_parcel_is_worth_asking_a_courier_about()
    {
        var silence = TimeSpan.FromHours(24);
        var draft = Drafted();

        // Nothing to ask: a draft has no courier.
        Assert.False(draft.IsStale(Now.AddDays(3), silence));

        var booked = Booked();

        Assert.False(booked.IsStale(Now.AddHours(1), silence));
        Assert.True(booked.IsStale(Now.AddDays(2), silence));

        booked.Advance(ShipmentStatus.Delivered, Now.AddDays(1), reason: null);

        // Nothing left to say.
        Assert.False(booked.IsStale(Now.AddDays(5), silence));
    }

    private static Shipment Drafted()
    {
        var shipment = Shipment.Draft(
            Guid.NewGuid(),
            "KH-2026-000123",
            Guid.NewGuid(),
            "KH-2026-000123-1",
            Guid.NewGuid(),
            Guid.NewGuid());

        shipment.Address("560001", Guid.NewGuid(), codAmount: null, 1_200m, 69m, "INR");

        return shipment;
    }

    private static Shipment Booked()
    {
        var shipment = Drafted();

        shipment.Pack(ShipmentLine.Pack(shipment.Id, Guid.NewGuid(), "SKU-1", "Cushion", 1, 400, 1_200m));
        shipment.CaptureWeight(450, 20m, 15m, 10m);

        shipment.Book(
            "aggregator",
            "Courier One",
            "Surface",
            "AWB-1",
            "1001",
            trackingUrl: null,
            expectedDeliveryAt: null,
            freightCost: 55m,
            Now);

        return shipment;
    }
}
