namespace KlaraHome.Contracts.Vendors;

/// <summary>What every other module is allowed to know about a seller.</summary>
/// <param name="Id">The seller's id, which is the <c>vendor_id</c> other schemas store.</param>
/// <param name="Code">The short, stable, human-quotable code — it appears on invoices and in support calls.</param>
/// <param name="DisplayName">The name a shopper sees.</param>
/// <param name="Slug">The storefront path segment.</param>
/// <param name="IsActive">Whether the seller may currently trade.</param>
/// <param name="DispatchSlaHours">How long this seller has to hand a parcel to a courier.</param>
/// <param name="Gstin">The seller's GST registration, which decides the place of supply on their invoice.</param>
/// <param name="Rating">
/// Their average review score out of five, or null if nobody has rated them yet. Added for the buy
/// box (docs/03-database-design.md §4.4), which ranks competing offers partly on it and cannot
/// read the sellers' table to find out.
/// </param>
public sealed record VendorSummary(
    Guid Id,
    string Code,
    string DisplayName,
    string Slug,
    bool IsActive,
    int DispatchSlaHours,
    string? Gstin,
    decimal? Rating);

/// <summary>
/// Reads sellers from outside the Vendors module (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// No foreign key may cross a schema boundary, so Catalog, Inventory, Orders and Settlements all
/// hold a plain <c>vendor_id</c> and validate it here. Without this contract, "is this a real,
/// trading seller" is either an unchecked assumption or a query across a boundary — and the first
/// of those ends with a listing nobody can fulfil.
/// </para>
/// <para>
/// Deliberately read-only and deliberately small. A module that needs a seller's bank account or
/// its KYC state is asking a question the Vendors module answers through its own endpoints, not
/// one it exports.
/// </para>
/// </remarks>
public interface IVendorDirectory
{
    /// <summary>Whether a seller exists and may currently trade.</summary>
    /// <param name="vendorId">The seller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<bool> IsActiveAsync(Guid vendorId, CancellationToken cancellationToken = default);

    /// <summary>The published facts about one seller, or null when there is no such seller.</summary>
    /// <param name="vendorId">The seller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<VendorSummary?> FindAsync(Guid vendorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The published facts about several sellers at once, keyed by id. Absent ids are simply not
    /// in the result — a listing whose seller has been offboarded is a case the caller handles.
    /// </summary>
    /// <param name="vendorIds">The sellers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IReadOnlyDictionary<Guid, VendorSummary>> FindManyAsync(
        IReadOnlyCollection<Guid> vendorIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a seller is willing to deliver to a destination.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The seller's own commercial choice, not the courier's reach: Shipping asks an aggregator
    /// whether a parcel <em>can</em> get somewhere, this answers whether this seller wants it to
    /// (docs/03-database-design.md §4.3). Added for the cart, which has to tell a shopper that a
    /// line cannot be delivered to their address before they reach the payment screen.
    /// </para>
    /// <para>
    /// A seller who serves all of India says yes to everything. Otherwise the rules are an
    /// allow-list with exclusions, and an exclusion wins over an inclusion — which is what makes
    /// "all of Karnataka except one district" expressible in two rows.
    /// </para>
    /// </remarks>
    /// <param name="vendorId">The seller.</param>
    /// <param name="stateId">The destination's <c>platform.states</c> row.</param>
    /// <param name="pincode">The destination's six-digit PIN code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<bool> IsServiceableAsync(
        Guid vendorId,
        Guid stateId,
        string pincode,
        CancellationToken cancellationToken = default);
}
