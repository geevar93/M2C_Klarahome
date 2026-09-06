using FluentValidation;
using KlaraHome.Contracts.Catalog;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Inventory.Domain;
using KlaraHome.Modules.Inventory.Infrastructure;
using KlaraHome.Modules.Inventory.Infrastructure.Persistence;
using KlaraHome.Modules.Inventory.Infrastructure.Stock;
using KlaraHome.SharedKernel.Primitives;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Inventory.Application.Stock;

/// <summary>Lists stock rows.</summary>
/// <param name="WarehouseId">Restrict to one location.</param>
/// <param name="ListingId">Restrict to one offer.</param>
/// <param name="VendorId">Restrict to one seller. Ignored for a vendor caller.</param>
/// <param name="LowStock">Only rows at or below their reorder level.</param>
/// <param name="OutOfStock">Only rows with nothing available.</param>
/// <param name="Search">A fragment of the SKU.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListStockQuery(
    Guid? WarehouseId,
    Guid? ListingId,
    Guid? VendorId,
    bool? LowStock,
    bool? OutOfStock,
    string? Search,
    string? Cursor,
    int? Size) : IQuery<PagedResult<StockItemResponse>>;

/// <summary>Reads one stock row.</summary>
/// <param name="StockItemId">The stock row.</param>
internal sealed record GetStockItemQuery(Guid StockItemId) : IQuery<StockItemResponse>;

/// <summary>Opens a stock row for an offer at a location, at zero.</summary>
/// <param name="ListingId">The offer.</param>
/// <param name="WarehouseId">Where it will be held.</param>
internal sealed record OpenStockItemCommand(Guid ListingId, Guid WarehouseId) : ICommand<StockItemResponse>;

/// <summary>Sets the replenishment and selling policy of a stock row.</summary>
/// <param name="StockItemId">The stock row.</param>
/// <param name="ReorderLevel">The level at or below which to alert. Zero disables the alert.</param>
/// <param name="ReorderQuantity">How many the seller buys at a time.</param>
/// <param name="AllowBackorder">Whether to accept orders beyond what is on hand.</param>
/// <param name="AllowPreorder">Whether the offer may be sold before release.</param>
/// <param name="PreorderAvailableAt">When a pre-ordered unit is expected to ship.</param>
/// <param name="TrackingMode">How closely individual units are tracked.</param>
internal sealed record ConfigureStockItemCommand(
    Guid StockItemId,
    int ReorderLevel,
    int ReorderQuantity,
    bool AllowBackorder,
    bool AllowPreorder,
    DateTimeOffset? PreorderAvailableAt,
    StockTrackingMode TrackingMode) : ICommand<StockItemResponse>;

/// <summary>Moves stock deliberately, with a reason.</summary>
/// <param name="StockItemId">The stock row.</param>
/// <param name="Change">Signed units.</param>
/// <param name="Reason">Why. Restricted to the reasons an operator may choose.</param>
/// <param name="Note">What they wrote.</param>
internal sealed record AdjustStockCommand(
    Guid StockItemId,
    int Change,
    StockMovementReason Reason,
    string? Note) : ICommand<StockItemResponse>;

/// <summary>Moves units of one offer between two locations.</summary>
/// <param name="ListingId">The offer.</param>
/// <param name="FromWarehouseId">Where the units leave.</param>
/// <param name="ToWarehouseId">Where they arrive.</param>
/// <param name="Quantity">How many.</param>
/// <param name="Note">Why.</param>
internal sealed record TransferStockCommand(
    Guid ListingId,
    Guid FromWarehouseId,
    Guid ToWarehouseId,
    int Quantity,
    string? Note) : ICommand<IReadOnlyList<StockItemResponse>>;

/// <summary>Reads a stock row's movements, newest first.</summary>
/// <param name="StockItemId">The stock row.</param>
/// <param name="From">Only movements on or after this instant.</param>
/// <param name="To">Only movements before this instant.</param>
/// <param name="Reason">Only movements with this reason.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListStockLedgerQuery(
    Guid StockItemId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Reason,
    string? Cursor,
    int? Size) : IQuery<PagedResult<StockLedgerEntryResponse>>;

/// <summary>Reads the holds against a stock row.</summary>
/// <param name="StockItemId">The stock row.</param>
/// <param name="Status">Restrict to one status.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListStockReservationsQuery(
    Guid StockItemId,
    string? Status,
    string? Cursor,
    int? Size) : IQuery<PagedResult<StockReservationResponse>>;

/// <summary>Rejects a movement that could never be stored.</summary>
internal sealed class AdjustStockValidator : AbstractValidator<AdjustStockCommand>
{
    /// <summary>
    /// The reasons an operator may choose.
    /// </summary>
    /// <remarks>
    /// Deliberately not the whole enum. <c>Sale</c>, <c>Reservation</c> and <c>Release</c> are
    /// written by the reservation machinery and nothing else; letting a person post one by hand
    /// would put the reserved cache out of step with the holds that justify it, and the nightly
    /// reconciliation would then report a drift nobody could explain.
    /// </remarks>
    public static readonly IReadOnlySet<StockMovementReason> OperatorReasons =
        new HashSet<StockMovementReason>
        {
            StockMovementReason.Adjustment,
            StockMovementReason.Damage,
            StockMovementReason.Return,
            StockMovementReason.Correction,
        };

    public AdjustStockValidator()
    {
        RuleFor(command => command.StockItemId).NotEmpty();
        RuleFor(command => command.Change).NotEqual(0);
        RuleFor(command => command.Note).MaximumLength(StockLedgerEntry.MaxNoteLength);

        RuleFor(command => command.Reason)
            .Must(OperatorReasons.Contains)
            .WithMessage("That reason is written by the system and cannot be posted by hand.");
    }
}

/// <summary>Rejects a transfer that could never be stored.</summary>
internal sealed class TransferStockValidator : AbstractValidator<TransferStockCommand>
{
    public TransferStockValidator()
    {
        RuleFor(command => command.ListingId).NotEmpty();
        RuleFor(command => command.FromWarehouseId).NotEmpty();
        RuleFor(command => command.ToWarehouseId).NotEmpty();
        RuleFor(command => command.Quantity).GreaterThan(0);
        RuleFor(command => command.Note).MaximumLength(StockLedgerEntry.MaxNoteLength);
    }
}

/// <summary>Rejects a policy that could never be stored.</summary>
internal sealed class ConfigureStockItemValidator : AbstractValidator<ConfigureStockItemCommand>
{
    public ConfigureStockItemValidator()
    {
        RuleFor(command => command.StockItemId).NotEmpty();
        RuleFor(command => command.ReorderLevel).GreaterThanOrEqualTo(0);
        RuleFor(command => command.ReorderQuantity).GreaterThanOrEqualTo(0);
        RuleFor(command => command.TrackingMode).IsInEnum();
    }
}

/// <summary>Lists stock rows.</summary>
/// <param name="context">The Inventory data context.</param>
internal sealed class ListStockQueryHandler(InventoryDbContext context)
    : IQueryHandler<ListStockQuery, PagedResult<StockItemResponse>>
{
    public async Task<Result<PagedResult<StockItemResponse>>> HandleAsync(
        ListStockQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);

        // The global vendor filter has already confined a vendor caller to their own rows.
        var rows =
            from item in context.StockItems.AsNoTracking()
            join warehouse in context.Warehouses.AsNoTracking() on item.WarehouseId equals warehouse.Id
            select new { item, warehouse.Code };

        if (query.WarehouseId is { } warehouseId)
        {
            rows = rows.Where(row => row.item.WarehouseId == warehouseId);
        }

        if (query.ListingId is { } listingId)
        {
            rows = rows.Where(row => row.item.ListingId == listingId);
        }

        if (query.VendorId is { } vendorId)
        {
            rows = rows.Where(row => row.item.VendorId == vendorId);
        }

        // Restated in SQL rather than calling StockItem.IsLow, which EF cannot translate. The two
        // must say the same thing; the comment is the only thing keeping them honest, which is why
        // the expression is written once and not repeated across endpoints.
        if (query.LowStock == true)
        {
            rows = rows.Where(row =>
                row.item.ReorderLevel > 0
                && row.item.QuantityOnHand - row.item.QuantityReserved <= row.item.ReorderLevel);
        }

        if (query.OutOfStock == true)
        {
            rows = rows.Where(row => row.item.QuantityOnHand - row.item.QuantityReserved <= 0);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{InventoryQueries.EscapeLike(query.Search)}%";
            rows = rows.Where(row => EF.Functions.ILike(row.item.Sku, pattern, "\\"));
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(row => row.item.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(row => row.item.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        return Result.Success(new PagedResult<StockItemResponse>(
            [.. page.Select(row => StockProjection.ToResponse(row.item, row.Code))],
            new PageInfo(size, hasMore ? Cursor.Encode(page[^1].item.Id.ToString()) : null)));
    }
}

/// <summary>Reads one stock row.</summary>
/// <param name="context">The Inventory data context.</param>
internal sealed class GetStockItemQueryHandler(InventoryDbContext context)
    : IQueryHandler<GetStockItemQuery, StockItemResponse>
{
    public async Task<Result<StockItemResponse>> HandleAsync(
        GetStockItemQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var row = await (
            from item in context.StockItems.AsNoTracking()
            join warehouse in context.Warehouses.AsNoTracking() on item.WarehouseId equals warehouse.Id
            where item.Id == query.StockItemId
            select new { item, warehouse.Code }).FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null
            ? InventoryErrors.NotFound("stock item")
            : Result.Success(StockProjection.ToResponse(row.item, row.Code));
    }
}

/// <summary>Opens a stock row.</summary>
/// <param name="context">The Inventory data context.</param>
/// <param name="scope">Refuses a seller stocking somebody else's location.</param>
/// <param name="catalog">Checks the offer exists, and takes its SKU.</param>
/// <param name="options">Says whether an unknown offer may be stocked at all.</param>
internal sealed class OpenStockItemCommandHandler(
    InventoryDbContext context,
    InventoryScope scope,
    IProductCatalog catalog,
    IOptions<InventoryOptions> options) : ICommandHandler<OpenStockItemCommand, StockItemResponse>
{
    public async Task<Result<StockItemResponse>> HandleAsync(
        OpenStockItemCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var warehouse = await context.Warehouses
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == command.WarehouseId, cancellationToken)
            .ConfigureAwait(false);

        if (warehouse is null)
        {
            return InventoryErrors.NotFound("location");
        }

        if (!scope.CanWrite(warehouse.VendorId))
        {
            return InventoryErrors.OutOfScope;
        }

        if (!warehouse.IsActive)
        {
            return InventoryErrors.WarehouseInactive;
        }

        // Across a module boundary and therefore over the contract, never a join
        // (docs/01-architecture.md §2.1).
        var listing = await catalog
            .FindListingAsync(command.ListingId, cancellationToken)
            .ConfigureAwait(false);

        if (listing is null && options.Value.RequireKnownListing)
        {
            return InventoryErrors.UnknownListing;
        }

        if (await context.StockItems
                .AnyAsync(
                    item => item.ListingId == command.ListingId && item.WarehouseId == warehouse.Id,
                    cancellationToken)
                .ConfigureAwait(false))
        {
            return InventoryErrors.Duplicate("stock item for that offer and location");
        }

        // The row's seller is the listing's, not the location's: a platform warehouse holding a
        // seller's goods on their behalf is fulfilment-by-platform, and the stock is still theirs.
        var item = StockItem.Open(
            command.ListingId,
            warehouse.Id,
            listing?.VendorId ?? warehouse.VendorId,
            listing?.Sku ?? command.ListingId.ToString());

        context.StockItems.Add(item);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(StockProjection.ToResponse(item, warehouse.Code));
    }
}

/// <summary>Sets a stock row's replenishment and selling policy.</summary>
/// <param name="context">The Inventory data context.</param>
/// <param name="scope">Refuses a seller configuring somebody else's stock.</param>
/// <param name="audit">Records the change.</param>
internal sealed class ConfigureStockItemCommandHandler(
    InventoryDbContext context,
    InventoryScope scope,
    IAuditLogger audit) : ICommandHandler<ConfigureStockItemCommand, StockItemResponse>
{
    /// <summary>The audited action for a change to a stock row's policy.</summary>
    public const string AuditAction = "inventory.stock.configured";

    /// <summary>The entity type recorded against every stock action.</summary>
    public const string AuditEntityType = "StockItem";

    public async Task<Result<StockItemResponse>> HandleAsync(
        ConfigureStockItemCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await context.StockItems
            .FirstOrDefaultAsync(candidate => candidate.Id == command.StockItemId, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return InventoryErrors.NotFound("stock item");
        }

        if (!scope.CanWrite(item.VendorId))
        {
            return InventoryErrors.OutOfScope;
        }

        var before = new
        {
            item.ReorderLevel,
            item.ReorderQuantity,
            item.AllowBackorder,
            item.AllowPreorder,
            item.TrackingMode,
        };

        item.Configure(
            command.ReorderLevel,
            command.ReorderQuantity,
            command.AllowBackorder,
            command.AllowPreorder,
            command.PreorderAvailableAt,
            command.TrackingMode);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AuditEntityType,
                EntityId = item.Id.ToString(),
                Before = before,
                After = new
                {
                    item.ReorderLevel,
                    item.ReorderQuantity,
                    item.AllowBackorder,
                    item.AllowPreorder,
                    item.TrackingMode,
                },
            },
            cancellationToken).ConfigureAwait(false);

        var code = await StockLookups.WarehouseCodeAsync(context, item.WarehouseId, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(StockProjection.ToResponse(item, code));
    }
}

/// <summary>Moves stock deliberately.</summary>
/// <param name="context">The Inventory data context.</param>
/// <param name="scope">Refuses a seller adjusting somebody else's stock.</param>
/// <param name="ledger">Applies the movement atomically.</param>
/// <param name="user">Who is doing it, recorded on the ledger entry.</param>
/// <param name="audit">Records the change.</param>
internal sealed class AdjustStockCommandHandler(
    InventoryDbContext context,
    InventoryScope scope,
    StockLedgerService ledger,
    IUserContext user,
    IAuditLogger audit) : ICommandHandler<AdjustStockCommand, StockItemResponse>
{
    /// <summary>The audited action for a manual movement.</summary>
    public const string AuditAction = "inventory.stock.adjusted";

    public async Task<Result<StockItemResponse>> HandleAsync(
        AdjustStockCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await context.StockItems
            .FirstOrDefaultAsync(candidate => candidate.Id == command.StockItemId, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return InventoryErrors.NotFound("stock item");
        }

        if (!scope.CanWrite(item.VendorId))
        {
            return InventoryErrors.OutOfScope;
        }

        var result = await ledger
            .MoveAsync(
                item,
                command.Change,
                command.Reason,
                referenceType: null,
                referenceId: null,
                command.Note,
                user.UserId,
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Applied)
        {
            return InventoryErrors.InsufficientStock;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = ConfigureStockItemCommandHandler.AuditEntityType,
                EntityId = item.Id.ToString(),
                After = new
                {
                    command.Change,
                    Reason = command.Reason.ToString(),
                    command.Note,
                    result.QuantityOnHand,
                },
            },
            cancellationToken).ConfigureAwait(false);

        var code = await StockLookups.WarehouseCodeAsync(context, item.WarehouseId, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(StockProjection.ToResponse(item, code));
    }
}

/// <summary>
/// Moves units of one offer between two locations.
/// </summary>
/// <remarks>
/// Two ledger entries sharing a reference id, not one: the units leave one shelf and arrive on
/// another, and a single entry could not say both. The pair is written inside one transaction, so a
/// transfer that fails halfway leaves neither leg — stock in flight that exists nowhere is the worst
/// outcome available here.
/// </remarks>
/// <param name="context">The Inventory data context.</param>
/// <param name="scope">Refuses a seller transferring somebody else's stock.</param>
/// <param name="ledger">Applies each leg atomically.</param>
/// <param name="user">Who is doing it.</param>
/// <param name="audit">Records the movement.</param>
internal sealed class TransferStockCommandHandler(
    InventoryDbContext context,
    InventoryScope scope,
    StockLedgerService ledger,
    IUserContext user,
    IAuditLogger audit) : ICommandHandler<TransferStockCommand, IReadOnlyList<StockItemResponse>>
{
    /// <summary>The audited action for a transfer.</summary>
    public const string AuditAction = "inventory.stock.transferred";

    /// <summary>The reference type both legs of a transfer carry.</summary>
    public const string ReferenceType = "transfer";

    public async Task<Result<IReadOnlyList<StockItemResponse>>> HandleAsync(
        TransferStockCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.FromWarehouseId == command.ToWarehouseId)
        {
            return InventoryErrors.SameWarehouse;
        }

        var source = await FindAsync(command.ListingId, command.FromWarehouseId, cancellationToken)
            .ConfigureAwait(false);

        var destination = await FindAsync(command.ListingId, command.ToWarehouseId, cancellationToken)
            .ConfigureAwait(false);

        if (source is null || destination is null)
        {
            return InventoryErrors.NotFound(
                "stock item at both locations. Open one at the destination before transferring");
        }

        if (!scope.CanWrite(source.VendorId) || !scope.CanWrite(destination.VendorId))
        {
            return InventoryErrors.OutOfScope;
        }

        // A shared id, so the two legs are recognisably one movement in a ledger that lists them
        // separately.
        var reference = UuidV7.New();

        var fromCode = await StockLookups
            .WarehouseCodeAsync(context, command.FromWarehouseId, cancellationToken)
            .ConfigureAwait(false);

        var toCode = await StockLookups
            .WarehouseCodeAsync(context, command.ToWarehouseId, cancellationToken)
            .ConfigureAwait(false);

        Result<IReadOnlyList<StockItemResponse>> outcome = InventoryErrors.InsufficientStock;

        await context.ExecuteInTransactionAsync(
            async (_, token) =>
            {
                var out_ = await ledger
                    .MoveAsync(
                        source,
                        -command.Quantity,
                        StockMovementReason.TransferOut,
                        ReferenceType,
                        reference,
                        command.Note,
                        user.UserId,
                        token)
                    .ConfigureAwait(false);

                if (!out_.Applied)
                {
                    return;
                }

                var in_ = await ledger
                    .MoveAsync(
                        destination,
                        command.Quantity,
                        StockMovementReason.TransferIn,
                        ReferenceType,
                        reference,
                        command.Note,
                        user.UserId,
                        token)
                    .ConfigureAwait(false);

                if (!in_.Applied)
                {
                    // Nothing can refuse an inbound movement — the guard is only that on hand stays
                    // non-negative — so this is unreachable rather than merely unlikely. Throwing
                    // rolls the outbound leg back; returning would commit stock into thin air.
                    throw new InvalidOperationException(
                        $"The inbound leg of transfer {reference} was refused, which cannot happen.");
                }

                await context.SaveChangesAsync(token).ConfigureAwait(false);

                outcome = Result.Success<IReadOnlyList<StockItemResponse>>(
                [
                    StockProjection.ToResponse(source, fromCode),
                    StockProjection.ToResponse(destination, toCode),
                ]);
            },
            cancellationToken).ConfigureAwait(false);

        if (outcome.IsFailure)
        {
            return outcome;
        }

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = ConfigureStockItemCommandHandler.AuditEntityType,
                EntityId = reference.ToString(),
                After = new
                {
                    command.ListingId,
                    command.FromWarehouseId,
                    command.ToWarehouseId,
                    command.Quantity,
                    command.Note,
                },
            },
            cancellationToken).ConfigureAwait(false);

        return outcome;
    }

    private Task<StockItem?> FindAsync(Guid listingId, Guid warehouseId, CancellationToken cancellationToken)
        => context.StockItems.FirstOrDefaultAsync(
            item => item.ListingId == listingId && item.WarehouseId == warehouseId,
            cancellationToken);
}

/// <summary>Reads a stock row's movements.</summary>
/// <param name="context">The Inventory data context.</param>
internal sealed class ListStockLedgerQueryHandler(InventoryDbContext context)
    : IQueryHandler<ListStockLedgerQuery, PagedResult<StockLedgerEntryResponse>>
{
    public async Task<Result<PagedResult<StockLedgerEntryResponse>>> HandleAsync(
        ListStockLedgerQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // The vendor filter is on the stock item, not on the ledger, so the caller's right to read
        // the movements is established by their right to read the row they belong to.
        if (!await context.StockItems
                .AnyAsync(item => item.Id == query.StockItemId, cancellationToken)
                .ConfigureAwait(false))
        {
            return InventoryErrors.NotFound("stock item");
        }

        var size = Cursor.NormalizeSize(query.Size);

        var rows = context.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.StockItemId == query.StockItemId);

        if (query.From is { } from)
        {
            rows = rows.Where(entry => entry.OccurredAt >= from);
        }

        if (query.To is { } to)
        {
            rows = rows.Where(entry => entry.OccurredAt < to);
        }

        if (Enum.TryParse<StockMovementReason>(query.Reason, ignoreCase: true, out var reason))
        {
            rows = rows.Where(entry => entry.Reason == reason);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(entry => entry.Id.CompareTo(after) < 0);
        }

        // Ordered on the id, which is a UUIDv7 and therefore time-ordered. Ordering on occurred_at
        // would need a tiebreak anyway, and two movements in one transaction share an instant.
        var page = await rows
            .OrderByDescending(entry => entry.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        return Result.Success(new PagedResult<StockLedgerEntryResponse>(
            [.. page.Select(StockProjection.ToResponse)],
            new PageInfo(size, hasMore ? Cursor.Encode(page[^1].Id.ToString()) : null)));
    }
}

/// <summary>Reads the holds against a stock row.</summary>
/// <param name="context">The Inventory data context.</param>
internal sealed class ListStockReservationsQueryHandler(InventoryDbContext context)
    : IQueryHandler<ListStockReservationsQuery, PagedResult<StockReservationResponse>>
{
    public async Task<Result<PagedResult<StockReservationResponse>>> HandleAsync(
        ListStockReservationsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await context.StockItems
                .AnyAsync(item => item.Id == query.StockItemId, cancellationToken)
                .ConfigureAwait(false))
        {
            return InventoryErrors.NotFound("stock item");
        }

        var size = Cursor.NormalizeSize(query.Size);

        var rows = context.Reservations
            .AsNoTracking()
            .Where(reservation => reservation.StockItemId == query.StockItemId);

        if (Enum.TryParse<ReservationStatus>(query.Status, ignoreCase: true, out var status))
        {
            rows = rows.Where(reservation => reservation.Status == status);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(reservation => reservation.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(reservation => reservation.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        return Result.Success(new PagedResult<StockReservationResponse>(
            [.. page.Select(StockProjection.ToResponse)],
            new PageInfo(size, hasMore ? Cursor.Encode(page[^1].Id.ToString()) : null)));
    }
}

/// <summary>The one-line lookups several handlers need after they have changed something.</summary>
internal static class StockLookups
{
    /// <summary>The code of a location, for a response that has to name it.</summary>
    /// <param name="context">The Inventory data context.</param>
    /// <param name="warehouseId">The location.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<string> WarehouseCodeAsync(
        InventoryDbContext context,
        Guid warehouseId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var code = await context.Warehouses
            .AsNoTracking()
            .Where(warehouse => warehouse.Id == warehouseId)
            .Select(warehouse => warehouse.Code)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return code ?? string.Empty;
    }
}
