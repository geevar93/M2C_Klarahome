using KlaraHome.Contracts.IntegrationEvents;

namespace KlaraHome.Contracts.Vendors;

/// <summary>
/// A seller began trading (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// Catalog allows their listings to go live on it, Settlements opens their ledger, and
/// Notifications welcomes them. Published from the outbox in the transaction that activated them,
/// so a consumer that acts on it is acting on something that certainly happened.
/// </remarks>
/// <param name="VendorId">The seller.</param>
/// <param name="Code">Their short code.</param>
/// <param name="DisplayName">The name shoppers see.</param>
public sealed record VendorActivated(Guid VendorId, string Code, string DisplayName) : IntegrationEvent;

/// <summary>
/// A seller was stopped from trading.
/// </summary>
/// <remarks>
/// Catalog takes their listings out of the storefront and Search drops them from the index. Their
/// open orders are deliberately unaffected: a suspension stops new sales, it does not abandon the
/// customers who already bought.
/// </remarks>
/// <param name="VendorId">The seller.</param>
/// <param name="Reason">Why, in the operator's words. Shown to the seller.</param>
public sealed record VendorSuspended(Guid VendorId, string? Reason) : IntegrationEvent;

/// <summary>
/// A seller left the marketplace for good.
/// </summary>
/// <remarks>
/// Terminal. Settlements still owes them whatever the ledger says, which is why this is announced
/// rather than the row being deleted.
/// </remarks>
/// <param name="VendorId">The seller.</param>
/// <param name="Reason">Why they left.</param>
public sealed record VendorOffboarded(Guid VendorId, string? Reason) : IntegrationEvent;
