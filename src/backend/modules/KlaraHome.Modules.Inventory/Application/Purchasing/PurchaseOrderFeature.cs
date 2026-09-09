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
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Inventory.Application.Purchasing;

/// <summary>One line of a purchase order, as the API states it.</summary>
/// <param name="Id">The line.</param>
/// <param name="ListingId">The offer being bought in.</param>
/// <param name="Sku">The SKU as it read when the order was raised.</param>
/// <param name="Description">What it is.</param>
/// <param name="QuantityOrdered">How many were ordered.</param>
/// <param name="QuantityReceived">How many have arrived.</param>
/// <param name="UnitCost">What the supplier charges per unit, before tax.</param>
/// <param name="TaxRate">The GST percentage on this line.</param>
/// <param name="LineTotal">Quantity times unit cost, before tax.</param>
internal sealed record PurchaseOrderLineResponse(
    Guid Id,
    Guid ListingId,
    string Sku,
    string? Description,
    int QuantityOrdered,
    int QuantityReceived,
    decimal UnitCost,
    decimal TaxRate,
    decimal LineTotal);

/// <summary>A purchase order, as the API states it.</summary>
/// <param name="Id">The document.</param>
/// <param name="Number">Its number.</param>
/// <param name="SupplierId">Who it is placed on.</param>
/// <param name="WarehouseId">Where the goods go.</param>
/// <param name="VendorId">Who is buying.</param>
/// <param name="Status">Where it is in its life.</param>
/// <param name="ExpectedAt">When the supplier said it would arrive.</param>
/// <param name="SubmittedAt">When it was sent.</param>
/// <param name="Subtotal">The sum of the lines before tax.</param>
/// <param name="TaxTotal">The tax on the lines.</param>
/// <param name="Total">What the buyer expects to pay.</param>
/// <param name="Notes">Anything the buyer wrote.</param>
/// <param name="Lines">The lines.</param>
/// <param name="CreatedAt">When it was raised.</param>
internal sealed record PurchaseOrderResponse(
    Guid Id,
    string Number,
    Guid SupplierId,
    Guid WarehouseId,
    Guid? VendorId,
    PurchaseOrderStatus Status,
    DateTimeOffset? ExpectedAt,
    DateTimeOffset? SubmittedAt,
    decimal Subtotal,
    decimal TaxTotal,
    decimal Total,
    string? Notes,
    IReadOnlyList<PurchaseOrderLineResponse> Lines,
    DateTimeOffset CreatedAt);

/// <summary>A line as the caller states it.</summary>
/// <param name="ListingId">The offer being bought in.</param>
/// <param name="Description">What it is. Defaults to the catalogue's name.</param>
/// <param name="QuantityOrdered">How many.</param>
/// <param name="UnitCost">What the supplier charges per unit, before tax.</param>
/// <param name="TaxRate">The GST percentage.</param>
internal sealed record PurchaseOrderLinePayload(
    Guid ListingId,
    string? Description,
    int QuantityOrdered,
    decimal UnitCost,
    decimal TaxRate);

/// <summary>Lists purchase orders.</summary>
/// <param name="Status">Restrict to one status.</param>
/// <param name="SupplierId">Restrict to one supplier.</param>
/// <param name="WarehouseId">Restrict to one destination.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListPurchaseOrdersQuery(
    string? Status,
    Guid? SupplierId,
    Guid? WarehouseId,
    string? Cursor,
    int? Size) : IQuery<PagedResult<PurchaseOrderResponse>>;

/// <summary>Reads one purchase order, lines and all.</summary>
/// <param name="PurchaseOrderId">The document.</param>
internal sealed record GetPurchaseOrderQuery(Guid PurchaseOrderId) : IQuery<PurchaseOrderResponse>;

/// <summary>Raises a draft purchase order.</summary>
/// <param name="SupplierId">Who it is placed on.</param>
/// <param name="WarehouseId">Where the goods go.</param>
/// <param name="ExpectedAt">When the supplier said it would arrive.</param>
/// <param name="Notes">Anything the buyer wrote.</param>
/// <param name="Lines">The lines.</param>
internal sealed record CreatePurchaseOrderCommand(
    Guid SupplierId,
    Guid WarehouseId,
    DateTimeOffset? ExpectedAt,
    string? Notes,
    IReadOnlyList<PurchaseOrderLinePayload> Lines) : ICommand<PurchaseOrderResponse>;

/// <summary>Rewrites a draft purchase order.</summary>
/// <param name="PurchaseOrderId">The document.</param>
/// <param name="ExpectedAt">When the supplier said it would arrive.</param>
/// <param name="Notes">Anything the buyer wrote.</param>
/// <param name="Lines">The lines.</param>
internal sealed record UpdatePurchaseOrderCommand(
    Guid PurchaseOrderId,
    DateTimeOffset? ExpectedAt,
    string? Notes,
    IReadOnlyList<PurchaseOrderLinePayload> Lines) : ICommand<PurchaseOrderResponse>;

/// <summary>Sends a purchase order to its supplier.</summary>
/// <param name="PurchaseOrderId">The document.</param>
internal sealed record SubmitPurchaseOrderCommand(Guid PurchaseOrderId) : ICommand<PurchaseOrderResponse>;

/// <summary>Calls a purchase order off.</summary>
/// <param name="PurchaseOrderId">The document.</param>
internal sealed record CancelPurchaseOrderCommand(Guid PurchaseOrderId) : ICommand<PurchaseOrderResponse>;

/// <summary>Rejects a purchase order that could never be stored.</summary>
internal sealed class CreatePurchaseOrderValidator : AbstractValidator<CreatePurchaseOrderCommand>
{
    /// <summary>The most lines one purchase order may carry.</summary>
    public const int MaxLines = 500;

    public CreatePurchaseOrderValidator()
    {
        RuleFor(command => command.SupplierId).NotEmpty();
        RuleFor(command => command.WarehouseId).NotEmpty();
        RuleFor(command => command.Notes).MaximumLength(2000);
        RuleFor(command => command.Lines).NotEmpty();
        RuleFor(command => command.Lines.Count).LessThanOrEqualTo(MaxLines);
        RuleForEach(command => command.Lines).SetValidator(new PurchaseOrderLineValidator());
    }
}

/// <summary>The same rules, for a change.</summary>
internal sealed class UpdatePurchaseOrderValidator : AbstractValidator<UpdatePurchaseOrderCommand>
{
    public UpdatePurchaseOrderValidator()
    {
        RuleFor(command => command.PurchaseOrderId).NotEmpty();
        RuleFor(command => command.Notes).MaximumLength(2000);
        RuleFor(command => command.Lines).NotEmpty();
        RuleFor(command => command.Lines.Count).LessThanOrEqualTo(CreatePurchaseOrderValidator.MaxLines);
        RuleForEach(command => command.Lines).SetValidator(new PurchaseOrderLineValidator());
    }
}

/// <summary>What a line has to carry to be orderable.</summary>
internal sealed class PurchaseOrderLineValidator : AbstractValidator<PurchaseOrderLinePayload>
{
    public PurchaseOrderLineValidator()
    {
        RuleFor(line => line.ListingId).NotEmpty();
        RuleFor(line => line.Description).MaximumLength(300);
        RuleFor(line => line.QuantityOrdered).GreaterThan(0);
        RuleFor(line => line.UnitCost).GreaterThanOrEqualTo(0m);
        RuleFor(line => line.TaxRate).InclusiveBetween(0m, 100m);
    }
}

/// <summary>Lists purchase orders.</summary>
/// <param name="context">The Inventory data context.</param>
internal sealed class ListPurchaseOrdersQueryHandler(InventoryDbContext context)
    : IQueryHandler<ListPurchaseOrdersQuery, PagedResult<PurchaseOrderResponse>>
{
    public async Task<Result<PagedResult<PurchaseOrderResponse>>> HandleAsync(
        ListPurchaseOrdersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);

        // Without the lines: a list screen shows totals and status, and loading every line of every
        // document to render a table of twenty-five would be a page load nobody forgives.
        var rows = context.PurchaseOrders.AsNoTracking();

        if (Enum.TryParse<PurchaseOrderStatus>(query.Status, ignoreCase: true, out var status))
        {
            rows = rows.Where(order => order.Status == status);
        }

        if (query.SupplierId is { } supplierId)
        {
            rows = rows.Where(order => order.SupplierId == supplierId);
        }

        if (query.WarehouseId is { } warehouseId)
        {
            rows = rows.Where(order => order.WarehouseId == warehouseId);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(order => order.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(order => order.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        return Result.Success(new PagedResult<PurchaseOrderResponse>(
            [.. page.Select(PurchaseOrderProjection.ToResponse)],
            new PageInfo(size, hasMore ? Cursor.Encode(page[^1].Id.ToString()) : null)));
    }
}

/// <summary>Reads one purchase order.</summary>
/// <param name="context">The Inventory data context.</param>
internal sealed class GetPurchaseOrderQueryHandler(InventoryDbContext context)
    : IQueryHandler<GetPurchaseOrderQuery, PurchaseOrderResponse>
{
    public async Task<Result<PurchaseOrderResponse>> HandleAsync(
        GetPurchaseOrderQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var order = await context.PurchaseOrders
            .AsNoTracking()
            .Include(candidate => candidate.Lines)
            .FirstOrDefaultAsync(candidate => candidate.Id == query.PurchaseOrderId, cancellationToken)
            .ConfigureAwait(false);

        return order is null
            ? InventoryErrors.NotFound("purchase order")
            : Result.Success(PurchaseOrderProjection.ToResponse(order));
    }
}

/// <summary>Raises a draft purchase order.</summary>
/// <param name="context">The Inventory data context.</param>
/// <param name="scope">Decides who is buying, and mints the number.</param>
/// <param name="catalog">Resolves each line's SKU and name from the catalogue.</param>
/// <param name="audit">Records the document.</param>
internal sealed class CreatePurchaseOrderCommandHandler(
    InventoryDbContext context,
    InventoryScope scope,
    IProductCatalog catalog,
    IAuditLogger audit) : ICommandHandler<CreatePurchaseOrderCommand, PurchaseOrderResponse>
{
    /// <summary>The audited action for a new purchase order.</summary>
    public const string AuditAction = "inventory.purchase-order.created";

    /// <summary>The entity type recorded against every purchase-order action.</summary>
    public const string AuditEntityType = "PurchaseOrder";

    public async Task<Result<PurchaseOrderResponse>> HandleAsync(
        CreatePurchaseOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var supplier = await context.Suppliers
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == command.SupplierId, cancellationToken)
            .ConfigureAwait(false);

        if (supplier is null)
        {
            return InventoryErrors.NotFound("supplier");
        }

        if (!supplier.IsActive)
        {
            return InventoryErrors.SupplierInactive;
        }

        var warehouse = await context.Warehouses
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == command.WarehouseId, cancellationToken)
            .ConfigureAwait(false);

        if (warehouse is null)
        {
            return InventoryErrors.NotFound("location");
        }

        if (!scope.CanWrite(warehouse.VendorId) || !scope.CanWrite(supplier.VendorId))
        {
            return InventoryErrors.PlatformOnly;
        }

        var number = await scope.NextPurchaseOrderNumberAsync(cancellationToken).ConfigureAwait(false);
        var order = PurchaseOrder.Raise(number, supplier.Id, warehouse.Id, scope.OwnerFor(warehouse.VendorId));

        var lines = await BuildLinesAsync(catalog, order.Id, command.Lines, cancellationToken)
            .ConfigureAwait(false);

        if (lines.IsFailure)
        {
            return lines.Error;
        }

        order.SetLines(lines.Value);
        order.Describe(command.ExpectedAt, command.Notes);

        context.PurchaseOrders.Add(order);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AuditEntityType,
                EntityId = order.Id.ToString(),
                After = new { order.Number, order.SupplierId, order.WarehouseId, Total = order.Total.Amount },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(PurchaseOrderProjection.ToResponse(order));
    }

    /// <summary>
    /// Turns the caller's lines into document lines, taking each SKU and name from the catalogue.
    /// </summary>
    /// <remarks>
    /// One batched contract call rather than one per line: a fifty-line order would otherwise be
    /// fifty round trips, and <c>IProductCatalog</c> exposes the batched form for exactly this.
    /// </remarks>
    internal static async Task<Result<List<PurchaseOrderLine>>> BuildLinesAsync(
        IProductCatalog catalog,
        Guid orderId,
        IReadOnlyList<PurchaseOrderLinePayload> payloads,
        CancellationToken cancellationToken)
    {
        var listings = await catalog
            .FindListingsAsync([.. payloads.Select(line => line.ListingId).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        var lines = new List<PurchaseOrderLine>(payloads.Count);

        foreach (var payload in payloads)
        {
            if (!listings.TryGetValue(payload.ListingId, out var listing))
            {
                return InventoryErrors.UnknownListing;
            }

            lines.Add(PurchaseOrderLine.Add(
                orderId,
                payload.ListingId,
                listing.Sku,
                payload.Description ?? listing.Name,
                payload.QuantityOrdered,
                Money.Rupees(payload.UnitCost),
                payload.TaxRate));
        }

        return lines;
    }
}

/// <summary>Rewrites a draft purchase order.</summary>
/// <param name="context">The Inventory data context.</param>
/// <param name="scope">Refuses a seller editing somebody else's document.</param>
/// <param name="catalog">Resolves each line's SKU and name.</param>
internal sealed class UpdatePurchaseOrderCommandHandler(
    InventoryDbContext context,
    InventoryScope scope,
    IProductCatalog catalog) : ICommandHandler<UpdatePurchaseOrderCommand, PurchaseOrderResponse>
{
    public async Task<Result<PurchaseOrderResponse>> HandleAsync(
        UpdatePurchaseOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var order = await context.PurchaseOrders
            .Include(candidate => candidate.Lines)
            .FirstOrDefaultAsync(candidate => candidate.Id == command.PurchaseOrderId, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return InventoryErrors.NotFound("purchase order");
        }

        if (!scope.CanWrite(order.VendorId))
        {
            return InventoryErrors.PlatformOnly;
        }

        // The document has left the building. A supplier who has been sent an order and then finds
        // the quantities changed underneath them is a dispute nobody wins.
        if (!order.IsEditable)
        {
            return InventoryErrors.DocumentFrozen;
        }

        var lines = await CreatePurchaseOrderCommandHandler
            .BuildLinesAsync(catalog, order.Id, command.Lines, cancellationToken)
            .ConfigureAwait(false);

        if (lines.IsFailure)
        {
            return lines.Error;
        }

        // The old lines are removed explicitly. Clearing the collection alone would leave EF with
        // orphans whose foreign key it would try to null, and the column is not nullable.
        context.PurchaseOrderLines.RemoveRange(order.Lines);

        order.SetLines(lines.Value);
        order.Describe(command.ExpectedAt, command.Notes);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(PurchaseOrderProjection.ToResponse(order));
    }
}

/// <summary>Sends a purchase order to its supplier.</summary>
/// <param name="context">The Inventory data context.</param>
/// <param name="scope">Refuses a seller acting on somebody else's document.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="audit">Records the submission.</param>
internal sealed class SubmitPurchaseOrderCommandHandler(
    InventoryDbContext context,
    InventoryScope scope,
    IClock clock,
    IAuditLogger audit) : ICommandHandler<SubmitPurchaseOrderCommand, PurchaseOrderResponse>
{
    /// <summary>The audited action for a submitted purchase order.</summary>
    public const string AuditAction = "inventory.purchase-order.submitted";

    public async Task<Result<PurchaseOrderResponse>> HandleAsync(
        SubmitPurchaseOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var order = await context.PurchaseOrders
            .Include(candidate => candidate.Lines)
            .FirstOrDefaultAsync(candidate => candidate.Id == command.PurchaseOrderId, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return InventoryErrors.NotFound("purchase order");
        }

        if (!scope.CanWrite(order.VendorId))
        {
            return InventoryErrors.PlatformOnly;
        }

        if (!order.Submit(clock.UtcNow))
        {
            return InventoryErrors.InvalidTransition(order.Status, PurchaseOrderStatus.Submitted);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreatePurchaseOrderCommandHandler.AuditEntityType,
                EntityId = order.Id.ToString(),
                After = new { order.Number, Total = order.Total.Amount, order.SubmittedAt },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(PurchaseOrderProjection.ToResponse(order));
    }
}

/// <summary>Calls a purchase order off.</summary>
/// <param name="context">The Inventory data context.</param>
/// <param name="scope">Refuses a seller acting on somebody else's document.</param>
/// <param name="audit">Records the cancellation.</param>
internal sealed class CancelPurchaseOrderCommandHandler(
    InventoryDbContext context,
    InventoryScope scope,
    IAuditLogger audit) : ICommandHandler<CancelPurchaseOrderCommand, PurchaseOrderResponse>
{
    /// <summary>The audited action for a cancelled purchase order.</summary>
    public const string AuditAction = "inventory.purchase-order.cancelled";

    public async Task<Result<PurchaseOrderResponse>> HandleAsync(
        CancelPurchaseOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var order = await context.PurchaseOrders
            .Include(candidate => candidate.Lines)
            .FirstOrDefaultAsync(candidate => candidate.Id == command.PurchaseOrderId, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return InventoryErrors.NotFound("purchase order");
        }

        if (!scope.CanWrite(order.VendorId))
        {
            return InventoryErrors.PlatformOnly;
        }

        // A partially received order cannot be cancelled: the units already on the shelf are real,
        // and this document is the only record of where they came from.
        if (!order.Cancel())
        {
            return InventoryErrors.InvalidTransition(order.Status, PurchaseOrderStatus.Cancelled);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreatePurchaseOrderCommandHandler.AuditEntityType,
                EntityId = order.Id.ToString(),
                After = new { order.Number },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(PurchaseOrderProjection.ToResponse(order));
    }
}

/// <summary>Turns purchase orders into responses.</summary>
internal static class PurchaseOrderProjection
{
    /// <summary>States a purchase order.</summary>
    /// <param name="order">The document.</param>
    public static PurchaseOrderResponse ToResponse(PurchaseOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);

        return new PurchaseOrderResponse(
            order.Id,
            order.Number,
            order.SupplierId,
            order.WarehouseId,
            order.VendorId,
            order.Status,
            order.ExpectedAt,
            order.SubmittedAt,
            order.Subtotal.Amount,
            order.TaxTotal.Amount,
            order.Total.Amount,
            order.Notes,
            [.. order.Lines.Select(ToResponse)],
            order.CreatedAt);
    }

    /// <summary>States one line.</summary>
    /// <param name="line">The line.</param>
    public static PurchaseOrderLineResponse ToResponse(PurchaseOrderLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return new PurchaseOrderLineResponse(
            line.Id,
            line.ListingId,
            line.Sku,
            line.Description,
            line.QuantityOrdered,
            line.QuantityReceived,
            line.UnitCost.Amount,
            line.TaxRate,
            line.LineTotal.Amount);
    }
}
