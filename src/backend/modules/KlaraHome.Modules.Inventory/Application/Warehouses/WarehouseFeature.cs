using FluentValidation;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Inventory.Domain;
using KlaraHome.Modules.Inventory.Infrastructure;
using KlaraHome.Modules.Inventory.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Inventory.Application.Warehouses;

/// <summary>An address, as the API states it.</summary>
/// <param name="Line1">Building, unit and street.</param>
/// <param name="Line2">Area or locality.</param>
/// <param name="Landmark">A nearby landmark.</param>
/// <param name="City">City or town.</param>
/// <param name="StateId">The <c>platform.states</c> row.</param>
/// <param name="ContactName">Who the courier asks for.</param>
/// <param name="ContactPhone">The number they ring.</param>
internal sealed record AddressPayload(
    string Line1,
    string? Line2,
    string? Landmark,
    string City,
    Guid? StateId,
    string? ContactName,
    string? ContactPhone);

/// <summary>A stock location, as the API states it.</summary>
/// <param name="Id">The location.</param>
/// <param name="VendorId">The seller who owns it, or null for a platform warehouse.</param>
/// <param name="Code">The short code an operator quotes.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Pincode">Six-digit PIN code.</param>
/// <param name="Address">Where it is.</param>
/// <param name="IsActive">Whether stock may still move through it.</param>
/// <param name="Priority">Allocation order. Lower wins.</param>
/// <param name="StockItemCount">How many offers are stocked here.</param>
/// <param name="CreatedAt">When it was opened.</param>
internal sealed record WarehouseResponse(
    Guid Id,
    Guid? VendorId,
    string Code,
    string Name,
    string Pincode,
    AddressPayload? Address,
    bool IsActive,
    int Priority,
    int StockItemCount,
    DateTimeOffset CreatedAt);

/// <summary>Lists stock locations.</summary>
/// <param name="VendorId">Restrict to one seller. Ignored for a vendor caller.</param>
/// <param name="ActiveOnly">Hide closed locations.</param>
/// <param name="Search">A fragment of the code or the name.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListWarehousesQuery(
    Guid? VendorId,
    bool? ActiveOnly,
    string? Search,
    string? Cursor,
    int? Size) : IQuery<PagedResult<WarehouseResponse>>;

/// <summary>Reads one location.</summary>
/// <param name="WarehouseId">The location.</param>
internal sealed record GetWarehouseQuery(Guid WarehouseId) : IQuery<WarehouseResponse>;

/// <summary>Opens a location.</summary>
/// <param name="VendorId">The seller. Ignored for a vendor caller, who opens their own.</param>
/// <param name="Code">The short code an operator quotes.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Pincode">Six-digit PIN code.</param>
/// <param name="Address">Where it is.</param>
/// <param name="Priority">Allocation order.</param>
internal sealed record CreateWarehouseCommand(
    Guid? VendorId,
    string Code,
    string Name,
    string Pincode,
    AddressPayload? Address,
    int Priority) : ICommand<WarehouseResponse>;

/// <summary>Renames a location and restates where it is.</summary>
/// <param name="WarehouseId">The location.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Pincode">Six-digit PIN code.</param>
/// <param name="Address">Where it is.</param>
/// <param name="Priority">Allocation order.</param>
/// <param name="IsActive">Whether stock may still move through it.</param>
internal sealed record UpdateWarehouseCommand(
    Guid WarehouseId,
    string Name,
    string Pincode,
    AddressPayload? Address,
    int Priority,
    bool IsActive) : ICommand<WarehouseResponse>;

/// <summary>Closes a location for good.</summary>
/// <param name="WarehouseId">The location.</param>
internal sealed record DeleteWarehouseCommand(Guid WarehouseId) : ICommand;

/// <summary>Rejects a location that could never be stored.</summary>
internal sealed class CreateWarehouseValidator : AbstractValidator<CreateWarehouseCommand>
{
    public CreateWarehouseValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(32).Matches("^[A-Za-z0-9][A-Za-z0-9-]*$");
        RuleFor(command => command.Name).NotEmpty().MaximumLength(160);
        RuleFor(command => command.Pincode).NotEmpty().Matches("^[1-9][0-9]{5}$");
        RuleFor(command => command.Priority).InclusiveBetween(0, Warehouse.MaxPriority);
        RuleFor(command => command.Address!).SetValidator(new AddressPayloadValidator())
            .When(command => command.Address is not null);
    }
}

/// <summary>The same rules, for a change.</summary>
internal sealed class UpdateWarehouseValidator : AbstractValidator<UpdateWarehouseCommand>
{
    public UpdateWarehouseValidator()
    {
        RuleFor(command => command.WarehouseId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(160);
        RuleFor(command => command.Pincode).NotEmpty().Matches("^[1-9][0-9]{5}$");
        RuleFor(command => command.Priority).InclusiveBetween(0, Warehouse.MaxPriority);
        RuleFor(command => command.Address!).SetValidator(new AddressPayloadValidator())
            .When(command => command.Address is not null);
    }
}

/// <summary>What an address has to carry to be deliverable.</summary>
internal sealed class AddressPayloadValidator : AbstractValidator<AddressPayload>
{
    public AddressPayloadValidator()
    {
        RuleFor(address => address.Line1).NotEmpty().MaximumLength(200);
        RuleFor(address => address.Line2).MaximumLength(200);
        RuleFor(address => address.Landmark).MaximumLength(160);
        RuleFor(address => address.City).NotEmpty().MaximumLength(120);
        RuleFor(address => address.ContactName).MaximumLength(160);
        RuleFor(address => address.ContactPhone).MaximumLength(20);
    }
}

/// <summary>Lists stock locations.</summary>
/// <param name="context">The Inventory data context.</param>
internal sealed class ListWarehousesQueryHandler(InventoryDbContext context)
    : IQueryHandler<ListWarehousesQuery, PagedResult<WarehouseResponse>>
{
    public async Task<Result<PagedResult<WarehouseResponse>>> HandleAsync(
        ListWarehousesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);

        // The global vendor filter has already confined a vendor caller to their own locations and
        // the platform's.
        var rows = context.Warehouses.AsNoTracking();

        if (query.VendorId is { } vendorId)
        {
            rows = rows.Where(warehouse => warehouse.VendorId == vendorId);
        }

        if (query.ActiveOnly == true)
        {
            rows = rows.Where(warehouse => warehouse.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{InventoryQueries.EscapeLike(query.Search)}%";

            rows = rows.Where(warehouse =>
                EF.Functions.ILike(warehouse.Code, pattern, "\\")
                || EF.Functions.ILike(warehouse.Name, pattern, "\\"));
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(warehouse => warehouse.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(warehouse => warehouse.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        // One grouped count for the page rather than one per row: the list is the screen an
        // operator opens first, and N+1 on it is the kind of slowness nobody attributes correctly.
        var ids = page.ConvertAll(warehouse => warehouse.Id);

        var counts = await context.StockItems
            .AsNoTracking()
            .Where(item => ids.Contains(item.WarehouseId))
            .GroupBy(item => item.WarehouseId)
            .Select(group => new { WarehouseId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.WarehouseId, row => row.Count, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new PagedResult<WarehouseResponse>(
            [.. page.Select(warehouse => WarehouseProjection.ToResponse(
                warehouse,
                counts.GetValueOrDefault(warehouse.Id)))],
            new PageInfo(size, hasMore ? Cursor.Encode(page[^1].Id.ToString()) : null)));
    }
}

/// <summary>Reads one location.</summary>
/// <param name="context">The Inventory data context.</param>
internal sealed class GetWarehouseQueryHandler(InventoryDbContext context)
    : IQueryHandler<GetWarehouseQuery, WarehouseResponse>
{
    public async Task<Result<WarehouseResponse>> HandleAsync(
        GetWarehouseQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var warehouse = await context.Warehouses
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == query.WarehouseId, cancellationToken)
            .ConfigureAwait(false);

        if (warehouse is null)
        {
            return InventoryErrors.NotFound("location");
        }

        var count = await context.StockItems
            .AsNoTracking()
            .CountAsync(item => item.WarehouseId == warehouse.Id, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(WarehouseProjection.ToResponse(warehouse, count));
    }
}

/// <summary>Opens a location.</summary>
/// <param name="context">The Inventory data context.</param>
/// <param name="scope">Decides which seller the location belongs to.</param>
/// <param name="vendors">Checks that a named seller exists.</param>
/// <param name="audit">Records the change.</param>
internal sealed class CreateWarehouseCommandHandler(
    InventoryDbContext context,
    InventoryScope scope,
    IVendorDirectory vendors,
    IAuditLogger audit) : ICommandHandler<CreateWarehouseCommand, WarehouseResponse>
{
    /// <summary>The audited action for a new location.</summary>
    public const string AuditAction = "inventory.warehouse.created";

    /// <summary>The entity type recorded against every warehouse action.</summary>
    public const string AuditEntityType = "Warehouse";

    public async Task<Result<WarehouseResponse>> HandleAsync(
        CreateWarehouseCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var ownerId = scope.OwnerFor(command.VendorId);

        // Across a module boundary and therefore over the contract, never a join
        // (docs/01-architecture.md §2.1).
        if (ownerId is { } vendorId
            && await vendors.FindAsync(vendorId, cancellationToken).ConfigureAwait(false) is null)
        {
            return InventoryErrors.NotFound("seller");
        }

        var code = command.Code.Trim().ToUpperInvariant();

        if (await context.Warehouses
                .AnyAsync(candidate => candidate.Code == code, cancellationToken)
                .ConfigureAwait(false))
        {
            return InventoryErrors.Duplicate("location code");
        }

        var warehouse = Warehouse.Open(ownerId, code, command.Name, command.Pincode);
        warehouse.Update(command.Name, command.Pincode, WarehouseProjection.ToAddress(command.Address), command.Priority);

        context.Warehouses.Add(warehouse);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AuditEntityType,
                EntityId = warehouse.Id.ToString(),
                After = new { warehouse.Code, warehouse.Name, warehouse.Pincode, warehouse.VendorId },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(WarehouseProjection.ToResponse(warehouse, 0));
    }
}

/// <summary>Renames a location.</summary>
/// <param name="context">The Inventory data context.</param>
/// <param name="scope">Refuses a seller editing somebody else's location.</param>
/// <param name="audit">Records the change.</param>
internal sealed class UpdateWarehouseCommandHandler(
    InventoryDbContext context,
    InventoryScope scope,
    IAuditLogger audit) : ICommandHandler<UpdateWarehouseCommand, WarehouseResponse>
{
    /// <summary>The audited action for a change to a location.</summary>
    public const string AuditAction = "inventory.warehouse.updated";

    public async Task<Result<WarehouseResponse>> HandleAsync(
        UpdateWarehouseCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var warehouse = await context.Warehouses
            .FirstOrDefaultAsync(candidate => candidate.Id == command.WarehouseId, cancellationToken)
            .ConfigureAwait(false);

        if (warehouse is null)
        {
            return InventoryErrors.NotFound("location");
        }

        if (!scope.CanWrite(warehouse.VendorId))
        {
            return InventoryErrors.PlatformOnly;
        }

        var before = new { warehouse.Name, warehouse.Pincode, warehouse.Priority, warehouse.IsActive };

        warehouse.Update(
            command.Name,
            command.Pincode,
            WarehouseProjection.ToAddress(command.Address),
            command.Priority);

        warehouse.SetActive(command.IsActive);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreateWarehouseCommandHandler.AuditEntityType,
                EntityId = warehouse.Id.ToString(),
                Before = before,
                After = new { warehouse.Name, warehouse.Pincode, warehouse.Priority, warehouse.IsActive },
            },
            cancellationToken).ConfigureAwait(false);

        var count = await context.StockItems
            .AsNoTracking()
            .CountAsync(item => item.WarehouseId == warehouse.Id, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(WarehouseProjection.ToResponse(warehouse, count));
    }
}

/// <summary>Removes a location.</summary>
/// <param name="context">The Inventory data context.</param>
/// <param name="scope">Refuses a seller removing somebody else's location.</param>
/// <param name="audit">Records the removal.</param>
internal sealed class DeleteWarehouseCommandHandler(
    InventoryDbContext context,
    InventoryScope scope,
    IAuditLogger audit) : ICommandHandler<DeleteWarehouseCommand>
{
    /// <summary>The audited action for a removed location.</summary>
    public const string AuditAction = "inventory.warehouse.deleted";

    public async Task<Result> HandleAsync(DeleteWarehouseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var warehouse = await context.Warehouses
            .FirstOrDefaultAsync(candidate => candidate.Id == command.WarehouseId, cancellationToken)
            .ConfigureAwait(false);

        if (warehouse is null)
        {
            return Result.Failure(InventoryErrors.NotFound("location"));
        }

        if (!scope.CanWrite(warehouse.VendorId))
        {
            return Result.Failure(InventoryErrors.PlatformOnly);
        }

        // A location with stock rows against it cannot go: the ledger entries behind them point at
        // it, and removing it would orphan every movement ever recorded there. Closing it is the
        // operation the operator actually wants, and it is one field away.
        if (await context.StockItems
                .AnyAsync(item => item.WarehouseId == warehouse.Id, cancellationToken)
                .ConfigureAwait(false))
        {
            return Result.Failure(InventoryErrors.StillInUse("stock items"));
        }

        context.Warehouses.Remove(warehouse);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreateWarehouseCommandHandler.AuditEntityType,
                EntityId = warehouse.Id.ToString(),
                Before = new { warehouse.Code, warehouse.Name },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Turns locations into responses, and payloads into addresses.</summary>
internal static class WarehouseProjection
{
    /// <summary>States a location.</summary>
    /// <param name="warehouse">The location.</param>
    /// <param name="stockItemCount">How many offers are stocked there.</param>
    public static WarehouseResponse ToResponse(Warehouse warehouse, int stockItemCount)
    {
        ArgumentNullException.ThrowIfNull(warehouse);

        return new WarehouseResponse(
            warehouse.Id,
            warehouse.VendorId,
            warehouse.Code,
            warehouse.Name,
            warehouse.Pincode,
            ToPayload(warehouse.Address),
            warehouse.IsActive,
            warehouse.Priority,
            stockItemCount,
            warehouse.CreatedAt);
    }

    /// <summary>Turns the API's address into the stored one.</summary>
    /// <param name="payload">What the caller sent, if anything.</param>
    public static WarehouseAddress ToAddress(AddressPayload? payload)
        => payload is null
            ? new WarehouseAddress()
            : new WarehouseAddress
            {
                Line1 = payload.Line1.Trim(),
                Line2 = Trim(payload.Line2),
                Landmark = Trim(payload.Landmark),
                City = payload.City.Trim(),
                StateId = payload.StateId,
                ContactName = Trim(payload.ContactName),
                ContactPhone = Trim(payload.ContactPhone),
            };

    /// <summary>Turns a stored address into the API's, or null when it was never filled in.</summary>
    /// <param name="address">The stored address.</param>
    public static AddressPayload? ToPayload(WarehouseAddress? address)
        => address is null || string.IsNullOrWhiteSpace(address.Line1)
            ? null
            : new AddressPayload(
                address.Line1,
                address.Line2,
                address.Landmark,
                address.City,
                address.StateId,
                address.ContactName,
                address.ContactPhone);

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
