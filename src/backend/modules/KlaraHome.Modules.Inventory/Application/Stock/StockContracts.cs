using KlaraHome.Modules.Inventory.Domain;

namespace KlaraHome.Modules.Inventory.Application.Stock;

/// <summary>The stock of one offer at one location, as the API states it.</summary>
/// <param name="Id">The stock row.</param>
/// <param name="ListingId">The offer.</param>
/// <param name="WarehouseId">Where it is held.</param>
/// <param name="WarehouseCode">The location's code, so a list reads without a second call.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="Sku">The stock-keeping unit.</param>
/// <param name="QuantityOnHand">Units physically held.</param>
/// <param name="QuantityReserved">Units held for a cart or an order.</param>
/// <param name="QuantityAvailable">What may still be sold.</param>
/// <param name="ReorderLevel">The level at or below which to alert. Zero disables the alert.</param>
/// <param name="ReorderQuantity">How many the seller buys at a time.</param>
/// <param name="AllowBackorder">Whether orders beyond what is on hand are accepted.</param>
/// <param name="AllowPreorder">Whether the offer may be sold before release.</param>
/// <param name="PreorderAvailableAt">When a pre-ordered unit is expected to ship.</param>
/// <param name="TrackingMode">How closely individual units are tracked.</param>
/// <param name="IsLow">Whether it is at or below its reorder level.</param>
/// <param name="UpdatedAt">When it last moved.</param>
internal sealed record StockItemResponse(
    Guid Id,
    Guid ListingId,
    Guid WarehouseId,
    string WarehouseCode,
    Guid? VendorId,
    string Sku,
    int QuantityOnHand,
    int QuantityReserved,
    int QuantityAvailable,
    int ReorderLevel,
    int ReorderQuantity,
    bool AllowBackorder,
    bool AllowPreorder,
    DateTimeOffset? PreorderAvailableAt,
    StockTrackingMode TrackingMode,
    bool IsLow,
    DateTimeOffset? UpdatedAt);

/// <summary>One movement, as the API states it.</summary>
/// <param name="Id">The entry.</param>
/// <param name="StockItemId">The stock row that moved.</param>
/// <param name="Change">Signed change to units on hand.</param>
/// <param name="BalanceAfter">Units on hand after.</param>
/// <param name="ReservedChange">Signed change to units reserved.</param>
/// <param name="ReservedAfter">Units reserved after.</param>
/// <param name="Reason">Why it moved.</param>
/// <param name="ReferenceType">What caused it.</param>
/// <param name="ReferenceId">The id of whatever caused it.</param>
/// <param name="Note">What the operator wrote.</param>
/// <param name="ActorId">Who did it.</param>
/// <param name="OccurredAt">When.</param>
internal sealed record StockLedgerEntryResponse(
    Guid Id,
    Guid StockItemId,
    int Change,
    int BalanceAfter,
    int ReservedChange,
    int ReservedAfter,
    StockMovementReason Reason,
    string? ReferenceType,
    Guid? ReferenceId,
    string? Note,
    Guid? ActorId,
    DateTimeOffset OccurredAt);

/// <summary>One hold, as the API states it.</summary>
/// <param name="Id">The hold.</param>
/// <param name="StockItemId">The stock row.</param>
/// <param name="ListingId">The offer.</param>
/// <param name="Quantity">How many units.</param>
/// <param name="ReferenceType">What is holding them.</param>
/// <param name="ReferenceId">The cart or order.</param>
/// <param name="Status">Where the hold is in its life.</param>
/// <param name="ExpiresAt">When it lapses.</param>
/// <param name="SettledAt">When it stopped being live.</param>
/// <param name="CreatedAt">When it was taken.</param>
internal sealed record StockReservationResponse(
    Guid Id,
    Guid StockItemId,
    Guid ListingId,
    int Quantity,
    string ReferenceType,
    Guid ReferenceId,
    ReservationStatus Status,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? SettledAt,
    DateTimeOffset CreatedAt);

/// <summary>One lot, as the API states it.</summary>
/// <param name="Id">The lot.</param>
/// <param name="StockItemId">The stock row.</param>
/// <param name="BatchCode">The supplier's lot number.</param>
/// <param name="Quantity">How many units of it are held.</param>
/// <param name="ManufacturedOn">When it was made.</param>
/// <param name="ExpiresOn">When it expires.</param>
/// <param name="SupplierId">Who it came from.</param>
internal sealed record StockBatchResponse(
    Guid Id,
    Guid StockItemId,
    string BatchCode,
    int Quantity,
    DateOnly? ManufacturedOn,
    DateOnly? ExpiresOn,
    Guid? SupplierId);

/// <summary>One individually identified unit, as the API states it.</summary>
/// <param name="Id">The unit.</param>
/// <param name="StockItemId">The stock row.</param>
/// <param name="SerialNumber">The manufacturer's number.</param>
/// <param name="BatchId">The lot it came in.</param>
/// <param name="Status">Where it is.</param>
/// <param name="ReferenceId">What last moved it.</param>
internal sealed record StockSerialResponse(
    Guid Id,
    Guid StockItemId,
    string SerialNumber,
    Guid? BatchId,
    SerialStatus Status,
    Guid? ReferenceId);

/// <summary>Turns stock rows and their satellites into responses.</summary>
internal static class StockProjection
{
    /// <summary>States a stock row.</summary>
    /// <param name="item">The stock row.</param>
    /// <param name="warehouseCode">Its location's code.</param>
    public static StockItemResponse ToResponse(StockItem item, string warehouseCode)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new StockItemResponse(
            item.Id,
            item.ListingId,
            item.WarehouseId,
            warehouseCode,
            item.VendorId,
            item.Sku,
            item.QuantityOnHand,
            item.QuantityReserved,
            item.QuantityAvailable,
            item.ReorderLevel,
            item.ReorderQuantity,
            item.AllowBackorder,
            item.AllowPreorder,
            item.PreorderAvailableAt,
            item.TrackingMode,
            item.IsLow,
            item.UpdatedAt);
    }

    /// <summary>States a movement.</summary>
    /// <param name="entry">The ledger entry.</param>
    public static StockLedgerEntryResponse ToResponse(StockLedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new StockLedgerEntryResponse(
            entry.Id,
            entry.StockItemId,
            entry.Change,
            entry.BalanceAfter,
            entry.ReservedChange,
            entry.ReservedAfter,
            entry.Reason,
            entry.ReferenceType,
            entry.ReferenceId,
            entry.Note,
            entry.ActorId,
            entry.OccurredAt);
    }

    /// <summary>States a hold.</summary>
    /// <param name="reservation">The hold.</param>
    public static StockReservationResponse ToResponse(StockReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        return new StockReservationResponse(
            reservation.Id,
            reservation.StockItemId,
            reservation.ListingId,
            reservation.Quantity,
            reservation.ReferenceType,
            reservation.ReferenceId,
            reservation.Status,
            reservation.ExpiresAt,
            reservation.SettledAt,
            reservation.CreatedAt);
    }

    /// <summary>States a lot.</summary>
    /// <param name="batch">The lot.</param>
    public static StockBatchResponse ToResponse(StockBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        return new StockBatchResponse(
            batch.Id,
            batch.StockItemId,
            batch.BatchCode,
            batch.Quantity,
            batch.ManufacturedOn,
            batch.ExpiresOn,
            batch.SupplierId);
    }

    /// <summary>States an individually identified unit.</summary>
    /// <param name="serial">The unit.</param>
    public static StockSerialResponse ToResponse(StockSerial serial)
    {
        ArgumentNullException.ThrowIfNull(serial);

        return new StockSerialResponse(
            serial.Id,
            serial.StockItemId,
            serial.SerialNumber,
            serial.BatchId,
            serial.Status,
            serial.ReferenceId);
    }
}
