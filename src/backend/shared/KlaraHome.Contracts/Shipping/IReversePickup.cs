using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Contracts.Shipping;

/// <summary>One line to be collected from a shopper's door.</summary>
/// <param name="OrderLineId">The order line the units came from.</param>
/// <param name="Quantity">How many units the courier is collecting.</param>
public sealed record ReversePickupLine(Guid OrderLineId, int Quantity);

/// <summary>What a reverse pickup is booked from.</summary>
/// <remarks>
/// The seller's part is named rather than an address, because the parcel is going back to where it
/// came from: Shipping already knows the pickup point it was dispatched from, and re-stating it here
/// would be a second answer to a question the forward shipment already settled.
/// </remarks>
/// <param name="SubOrderId">The seller's part the goods came from.</param>
/// <param name="ReturnId">The RMA. It becomes the consignment's reference.</param>
/// <param name="ReturnNumber">The RMA's number, which is what goes on the label.</param>
/// <param name="Lines">What is being collected.</param>
/// <param name="ScheduledFor">When the courier should call, or null for the earliest they will.</param>
/// <param name="ManualAwb">
/// A waybill an operator obtained from a courier themselves. Present when a deployment has no
/// logistics account, which is the state a fresh one is in.
/// </param>
/// <param name="ManualCourier">Who is carrying it, for a hand-booking.</param>
public sealed record ReversePickupRequest(
    Guid SubOrderId,
    Guid ReturnId,
    string ReturnNumber,
    IReadOnlyList<ReversePickupLine> Lines,
    DateTimeOffset? ScheduledFor,
    string? ManualAwb,
    string? ManualCourier);

/// <summary>The collection a courier agreed to.</summary>
/// <param name="ShipmentId">The reverse consignment.</param>
/// <param name="Awb">The waybill it will travel back on.</param>
/// <param name="Courier">Who is carrying it.</param>
/// <param name="TrackingUrl">Where the shopper can watch it, when the courier offers one.</param>
/// <param name="ScheduledFor">When the courier will call, as they confirmed it.</param>
public sealed record ReversePickupBooking(
    Guid ShipmentId,
    string Awb,
    string? Courier,
    string? TrackingUrl,
    DateTimeOffset? ScheduledFor);

/// <summary>
/// Books a courier to collect goods from a shopper (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// The seam Returns reaches logistics through, and the last of the four this platform's post-sale
/// flow needs. It exists so that a reverse pickup is a <em>shipment</em> like any other — booked
/// through the same adapter, tracked by the same webhook receiver, and visible in the same parcel
/// list — rather than a second, thinner idea of a parcel living in the returns schema.
/// </para>
/// <para>
/// It inverts the forward journey: the shopper's frozen delivery address becomes the pickup, and the
/// seller's pickup point becomes the destination. Neither is passed in, because Shipping already
/// holds both from the parcel that went out.
/// </para>
/// <para>
/// A deployment with no logistics account still works. The manual adapter takes a waybill an
/// operator typed in, exactly as it does for a forward dispatch, and everything downstream — the
/// tracking, the timeline, the arrival at the warehouse — is unchanged.
/// </para>
/// </remarks>
public interface IReversePickup
{
    /// <summary>Books the collection, or explains why it could not be booked.</summary>
    /// <param name="request">What to collect, and from which sale.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<ReversePickupBooking>> BookAsync(
        ReversePickupRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls off a collection that has not happened yet.
    /// </summary>
    /// <remarks>
    /// What a return cancelled by the shopper after approval needs. A courier already holding the
    /// parcel cannot be called off, and saying so is more useful than a cancellation that quietly
    /// does nothing.
    /// </remarks>
    /// <param name="shipmentId">The reverse consignment.</param>
    /// <param name="reason">Why, for the parcel's own timeline.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> CancelAsync(
        Guid shipmentId,
        string? reason,
        CancellationToken cancellationToken = default);
}
