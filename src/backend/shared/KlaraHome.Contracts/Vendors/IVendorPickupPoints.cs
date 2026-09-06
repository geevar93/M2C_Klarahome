using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Contracts.Vendors;

/// <summary>A place a courier collects from, as the module that books the collection needs it.</summary>
/// <param name="Id">The pickup location.</param>
/// <param name="VendorId">The seller it belongs to.</param>
/// <param name="Label">What the seller calls it.</param>
/// <param name="ContactName">Who the courier asks for on arrival.</param>
/// <param name="ContactPhone">The number the courier rings, in E.164.</param>
/// <param name="Line1">Building and unit.</param>
/// <param name="Line2">Street, area or locality.</param>
/// <param name="Landmark">A nearby landmark.</param>
/// <param name="City">City or town.</param>
/// <param name="StateId">The <c>platform.states</c> row.</param>
/// <param name="Pincode">Six-digit PIN code. The first leg is priced from this.</param>
/// <param name="IsDefault">Whether shipments are booked against it unless another is named.</param>
/// <param name="CourierLocationCode">
/// The aggregator's own id for this address, or null while it is unregistered. A courier will not
/// accept a pickup against an address it has never been told about, so this is the evidence that
/// registration happened.
/// </param>
public sealed record VendorPickupPoint(
    Guid Id,
    Guid VendorId,
    string Label,
    string ContactName,
    string ContactPhone,
    string Line1,
    string? Line2,
    string? Landmark,
    string City,
    Guid StateId,
    string Pincode,
    bool IsDefault,
    string? CourierLocationCode);

/// <summary>
/// Reads and registers sellers' pickup addresses from outside the Vendors module
/// (docs/03-database-design.md §4.3).
/// </summary>
/// <remarks>
/// <para>
/// A separate interface from <see cref="IVendorDirectory"/> rather than four more methods on it,
/// because that one is deliberately the small set of facts <em>every</em> module needs about a
/// seller. A pickup address is needed by exactly one module, and putting it on the shared contract
/// would make every consumer carry it.
/// </para>
/// <para>
/// It is the only contract in the platform through which another module <em>writes</em> to the
/// Vendors schema, and the write is a single field: the courier's id for an address, which only the
/// module that talks to couriers can ever learn. Everything else here is a read.
/// </para>
/// </remarks>
public interface IVendorPickupPoints
{
    /// <summary>
    /// The address a seller dispatches from — the one named, or their default.
    /// </summary>
    /// <param name="vendorId">The seller.</param>
    /// <param name="pickupLocationId">A specific location, or null for the seller's default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<VendorPickupPoint?> FindAsync(
        Guid vendorId,
        Guid? pickupLocationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Every address a seller still collects from.</summary>
    /// <param name="vendorId">The seller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<VendorPickupPoint>> ListAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the courier's own id for an address, once it has been registered with them.
    /// </summary>
    /// <remarks>
    /// Idempotent, and deliberately last-write-wins: re-registering an address with a second
    /// aggregator overwrites the code, which is correct, because only one aggregator is booked
    /// against at a time and a stale code is worse than none.
    /// </remarks>
    /// <param name="pickupLocationId">The address.</param>
    /// <param name="courierLocationCode">The aggregator's id for it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> RecordCourierLocationCodeAsync(
        Guid pickupLocationId,
        string courierLocationCode,
        CancellationToken cancellationToken = default);
}
