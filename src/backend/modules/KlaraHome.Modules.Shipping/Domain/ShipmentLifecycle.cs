namespace KlaraHome.Modules.Shipping.Domain;

/// <summary>
/// Where a consignment is (docs/02-domain-model.md §5).
/// </summary>
/// <remarks>
/// <para>
/// Two states exist before the courier knows anything about the parcel, and that is deliberate.
/// <see cref="Draft"/> is a packing job: the sub-order was confirmed, the lines are known, and
/// nothing has been weighed. <see cref="Created"/> is a booking: an AWB exists and a courier is
/// expecting it. The gap between them is the whole of the packing workflow, and collapsing them
/// would leave a platform unable to say what is waiting to be packed.
/// </para>
/// <para>
/// The numbering leaves gaps on purpose. A courier vocabulary is not a closed set, and a state that
/// has to be inserted between two others should not renumber everything after it — these values are
/// written to the database as words, but the ordering is what an admin list sorts by.
/// </para>
/// </remarks>
internal enum ShipmentStatus
{
    /// <summary>Packed or waiting to be. Nothing has been booked with a courier.</summary>
    Draft = 0,

    /// <summary>Booked. The courier has issued an air waybill.</summary>
    Created = 10,

    /// <summary>The label PDF exists and can be printed.</summary>
    LabelGenerated = 20,

    /// <summary>A collection has been asked for.</summary>
    PickupScheduled = 30,

    /// <summary>The courier has the parcel. This is dispatch.</summary>
    PickedUp = 40,

    /// <summary>Moving through the courier network.</summary>
    InTransit = 50,

    /// <summary>On a vehicle for the last mile.</summary>
    OutForDelivery = 60,

    /// <summary>With the shopper.</summary>
    Delivered = 70,

    /// <summary>An attempt failed, or the courier reported a problem. Somebody has to decide.</summary>
    Exception = 80,

    /// <summary>Coming back to the seller.</summary>
    RtoInitiated = 90,

    /// <summary>Back with the seller. Nothing further happens to it.</summary>
    RtoDelivered = 100,

    /// <summary>Called off before it moved.</summary>
    Cancelled = 110,
}

/// <summary>Why a delivery attempt failed, in this platform's vocabulary.</summary>
/// <remarks>
/// Every aggregator words these differently and most of them send free text as well. The adapter
/// maps what it can onto this list and keeps the courier's own words alongside, because an operator
/// working the queue acts on the category and quotes the text.
/// </remarks>
internal enum NdrReasonCode
{
    /// <summary>The courier gave a reason nothing here covers. The remark is what matters.</summary>
    Other = 0,

    /// <summary>Nobody was there.</summary>
    CustomerUnavailable = 1,

    /// <summary>The address could not be found, or is wrong.</summary>
    AddressIncorrect = 2,

    /// <summary>The shopper refused the parcel at the door.</summary>
    Refused = 3,

    /// <summary>The shopper asked for a later date.</summary>
    RescheduleRequested = 4,

    /// <summary>Cash on delivery, and the shopper did not have it.</summary>
    CodNotReady = 5,

    /// <summary>The courier could not reach the area — weather, unrest, a closed road.</summary>
    Unreachable = 6,
}

/// <summary>What was decided about a failed delivery.</summary>
internal enum NdrAction
{
    /// <summary>Nothing yet. This is the queue an operator works.</summary>
    Pending = 0,

    /// <summary>Try again, as it stands.</summary>
    Reattempt = 1,

    /// <summary>Try again on a date the shopper asked for.</summary>
    Rescheduled = 2,

    /// <summary>The address was corrected and the courier told.</summary>
    AddressUpdated = 3,

    /// <summary>Give up and send it back to the seller.</summary>
    ReturnToOrigin = 4,

    /// <summary>The parcel arrived anyway. The report is closed without an action.</summary>
    Resolved = 5,
}

/// <summary>
/// The one place a consignment's transition table lives.
/// </summary>
/// <remarks>
/// <para>
/// A shipment moves on a courier's word, and a courier will say anything: a scan out of order, a
/// delivery after a return, the same event three times. This table is what keeps that from becoming
/// nonsense in the database — an edge that does not exist is refused, recorded as a tracking event
/// all the same, and left for a human to look at.
/// </para>
/// <para>
/// It is deliberately forgiving in one direction and strict in the other. Forward movement may skip
/// states, because couriers routinely report "out for delivery" on a parcel they never scanned into
/// transit, and refusing that would strand the parcel. Backward movement is refused outright, except
/// the two corrections that genuinely happen: a parcel written off as an exception that then moves
/// again, and one marked for return that is delivered after all.
/// </para>
/// </remarks>
internal static class ShipmentLifecycle
{
    /// <summary>The states from which nothing further happens.</summary>
    public static readonly IReadOnlyList<ShipmentStatus> Terminal =
    [
        ShipmentStatus.Delivered,
        ShipmentStatus.RtoDelivered,
        ShipmentStatus.Cancelled,
    ];

    /// <summary>Whether nothing further can happen to a consignment in this state.</summary>
    /// <param name="status">The status.</param>
    public static bool IsTerminal(ShipmentStatus status) => Terminal.Contains(status);

    /// <summary>Whether the machine has this edge.</summary>
    /// <param name="from">Where it is.</param>
    /// <param name="to">Where it is being moved to.</param>
    public static bool IsTransitionAllowed(ShipmentStatus from, ShipmentStatus to)
    {
        if (from == to || IsTerminal(from))
        {
            return false;
        }

        return (from, to) switch
        {
            // Cancelling is possible right up to the moment the courier physically has it. After
            // that it is a return, not a cancellation, and it costs money either way.
            (_, ShipmentStatus.Cancelled) => from
                is ShipmentStatus.Draft
                or ShipmentStatus.Created
                or ShipmentStatus.LabelGenerated
                or ShipmentStatus.PickupScheduled,

            // A courier can report trouble at any point once it is booked.
            (_, ShipmentStatus.Exception) => from != ShipmentStatus.Draft,

            // The correction that matters: a parcel that was in exception moves again. Every
            // forward state is reachable from it, because the courier is telling us where it now is.
            (ShipmentStatus.Exception, _) => to
                is ShipmentStatus.InTransit
                or ShipmentStatus.OutForDelivery
                or ShipmentStatus.Delivered
                or ShipmentStatus.RtoInitiated,

            // A return can still end in a delivery. It happens more often than it should, and
            // refusing it would leave the shopper holding goods the platform says are coming back.
            (ShipmentStatus.RtoInitiated, _) => to
                is ShipmentStatus.RtoDelivered
                or ShipmentStatus.Delivered,

            // Everything else is forward movement, and skipping is allowed: couriers report
            // "out for delivery" on parcels they never scanned into transit, and a machine that
            // refused that would strand the parcel rather than the scan.
            _ => to > from,
        };
    }

    /// <summary>
    /// The ordering module's word for what a consignment's state means for the sale, or null when it
    /// means nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The translation between two vocabularies that must not be merged. A shipment has a label and
    /// a pickup slot; a sub-order has neither, and there is nothing useful to tell a shopper about
    /// either. Only the states a customer would recognise map across.
    /// </para>
    /// <para>
    /// The names are the ordering module's, spelled as strings because no module may reference
    /// another. <c>IOrderFulfilment</c> parses them, and a name that does not parse is refused there
    /// rather than silently ignored.
    /// </para>
    /// </remarks>
    /// <param name="status">The consignment's state.</param>
    public static string? OrderStatusFor(ShipmentStatus status)
        => status switch
        {
            // Dispatch. The parcel is with the courier, which is the fact the shopper is waiting for.
            ShipmentStatus.PickedUp => "Shipped",
            ShipmentStatus.InTransit => "Shipped",
            ShipmentStatus.OutForDelivery => "OutForDelivery",
            ShipmentStatus.Delivered => "Delivered",
            ShipmentStatus.Exception => "DeliveryFailed",
            ShipmentStatus.RtoInitiated => "RtoInitiated",
            ShipmentStatus.RtoDelivered => "RtoDelivered",
            _ => null,
        };

    /// <summary>A sentence a shopper can read, for the timeline entry that carries no transition.</summary>
    /// <param name="status">The consignment's state.</param>
    public static string Narrate(ShipmentStatus status)
        => status switch
        {
            ShipmentStatus.Draft => "Your parcel is being packed.",
            ShipmentStatus.Created => "A courier has been booked.",
            ShipmentStatus.LabelGenerated => "The shipping label has been printed.",
            ShipmentStatus.PickupScheduled => "A collection has been arranged with the courier.",
            ShipmentStatus.PickedUp => "The courier has collected your parcel.",
            ShipmentStatus.InTransit => "Your parcel is on its way.",
            ShipmentStatus.OutForDelivery => "Your parcel is out for delivery today.",
            ShipmentStatus.Delivered => "Your parcel has been delivered.",
            ShipmentStatus.Exception => "The courier reported a problem with your parcel.",
            ShipmentStatus.RtoInitiated => "Your parcel is being returned to the seller.",
            ShipmentStatus.RtoDelivered => "Your parcel has been returned to the seller.",
            ShipmentStatus.Cancelled => "The shipment was cancelled.",
            _ => "Your parcel was updated.",
        };
}
