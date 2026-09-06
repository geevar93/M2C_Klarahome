using KlaraHome.Contracts.IntegrationEvents;

namespace KlaraHome.Contracts.Inventory;

/// <summary>
/// The stock behind an offer moved (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// <para>
/// Search rebuilds its availability facet from it, Reviews uses it to fire back-in-stock alerts,
/// and Notifications tells a shopper their item is purchasable again. It carries the aggregate
/// across every warehouse rather than one row's movement, because none of those consumers care
/// which shelf the units left.
/// </para>
/// <para>
/// Published from the outbox in the transaction that wrote the ledger entry, so a consumer
/// reacting to it is reacting to something that certainly happened.
/// </para>
/// </remarks>
/// <param name="ListingId">The offer whose stock moved.</param>
/// <param name="VendorId">The seller, so a consumer can scope without asking Catalog.</param>
/// <param name="QuantityOnHand">Units physically held, across every warehouse.</param>
/// <param name="QuantityAvailable">What may still be sold: on hand less reserved, floored at zero.</param>
/// <param name="WasAvailable">
/// Whether anything was available before this movement. The pair of flags is what makes
/// "just went out of stock" and "just came back" answerable without the consumer keeping state.
/// </param>
/// <param name="IsAvailable">Whether anything is available now.</param>
public sealed record StockLevelChanged(
    Guid ListingId,
    Guid VendorId,
    int QuantityOnHand,
    int QuantityAvailable,
    bool WasAvailable,
    bool IsAvailable) : IntegrationEvent;

/// <summary>
/// A stock item fell to or below its reorder level.
/// </summary>
/// <remarks>
/// Raised once per crossing rather than once per movement: the stock item records when it last
/// alerted and stays quiet until it has been replenished above the level again. Without that, one
/// slow-selling item below its threshold would alert its seller on every single sale, and the
/// alert that matters would be lost in the ones that do not.
/// </remarks>
/// <param name="StockItemId">The (listing, warehouse) row that is low.</param>
/// <param name="ListingId">The offer.</param>
/// <param name="VendorId">The seller to tell.</param>
/// <param name="WarehouseId">Where it is low.</param>
/// <param name="Sku">The stock-keeping unit, so the alert reads like something.</param>
/// <param name="QuantityAvailable">What is left.</param>
/// <param name="ReorderLevel">The level it fell to.</param>
/// <param name="ReorderQuantity">How many the seller said they reorder at a time.</param>
public sealed record StockRunningLow(
    Guid StockItemId,
    Guid ListingId,
    Guid VendorId,
    Guid WarehouseId,
    string Sku,
    int QuantityAvailable,
    int ReorderLevel,
    int ReorderQuantity) : IntegrationEvent;
