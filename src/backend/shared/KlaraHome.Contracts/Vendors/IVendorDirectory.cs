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
    /// The published facts about one seller, found by their code rather than their id.
    /// </summary>
    /// <remarks>
    /// The code is the stable, human-quotable handle — it is what appears on an invoice, in a
    /// support call and in the vendor column of an uploaded spreadsheet. Everything that arrives
    /// from outside this system names a seller that way and not by a GUID, so a lookup by code is
    /// the seam those callers need; without it each of them ends up either carrying an id it has no
    /// way to have learned, or querying across the schema boundary this contract exists to prevent.
    /// </remarks>
    /// <param name="code">The seller's code. Matched case-insensitively.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<VendorSummary?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// What a seller promises about goods coming back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Added at Step 17. The seller's promise is the middle term of a three-way resolution: the
    /// product's own window is frozen on the order line and wins, the store's default is the
    /// backstop, and this sits between them — a seller who says thirty days has said so on every
    /// product page, and a return refused at seven would be refusing what the shopper was shown.
    /// </para>
    /// <para>
    /// It is a separate call from <see cref="FindAsync"/> rather than a field on
    /// <see cref="VendorSummary"/>, because the summary is read on the buy-box path for every
    /// listing on a page and this is read once, when somebody asks to send something back.
    /// </para>
    /// </remarks>
    /// <param name="vendorId">The seller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<VendorReturnPolicy?> ReturnPolicyAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default);
}

/// <summary>What a seller promises about goods coming back.</summary>
/// <remarks>
/// The seller's half of the return policy, as it is shown on their product pages. It is a promise
/// rather than a rule the platform enforces alone: what actually happens to a particular return also
/// depends on the reason code and on the store's own settings, and the strictest of the three is not
/// always the one that applies.
/// </remarks>
/// <param name="VendorId">The seller.</param>
/// <param name="AcceptsReturns">Whether they take goods back at all.</param>
/// <param name="WindowDays">How many days after delivery a return may be raised, or zero for none.</param>
/// <param name="AcceptsExchanges">Whether a replacement is offered as well as a refund.</param>
/// <param name="CustomerPaysReturnShipping">
/// Whether the shopper pays the reverse freight when the reason is not a fault. It is what the
/// seller agreed to; a reason code marked as the seller's fault still overrides it.
/// </param>
/// <param name="Notes">The seller's own wording, shown on the product page.</param>
public sealed record VendorReturnPolicy(
    Guid VendorId,
    bool AcceptsReturns,
    int WindowDays,
    bool AcceptsExchanges,
    bool CustomerPaysReturnShipping,
    string? Notes);
