using FluentValidation;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Inventory.Domain;
using KlaraHome.Modules.Inventory.Infrastructure;
using KlaraHome.Modules.Inventory.Infrastructure.Persistence;
using KlaraHome.Modules.Inventory.Infrastructure.Stock;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Inventory.Application.Purchasing;

/// <summary>One counted line of a goods receipt, as the API states it.</summary>
/// <param name="Id">The line.</param>
/// <param name="PurchaseOrderLineId">The ordered line it satisfies.</param>
/// <param name="StockItemId">The stock row the accepted units went into.</param>
/// <param name="QuantityAccepted">How many were accepted.</param>
/// <param name="QuantityRejected">How many were refused.</param>
/// <param name="RejectionReason">Why they were refused.</param>
/// <param name="BatchCode">The supplier's lot number.</param>
/// <param name="ExpiresOn">When that lot expires.</param>
internal sealed record GoodsReceiptLineResponse(
    Guid Id,
    Guid PurchaseOrderLineId,
    Guid StockItemId,
    int QuantityAccepted,
    int QuantityRejected,
    string? RejectionReason,
    string? BatchCode,
    DateOnly? ExpiresOn);

/// <summary>A goods receipt, as the API states it.</summary>
/// <param name="Id">The document.</param>
/// <param name="Number">Its number.</param>
/// <param name="PurchaseOrderId">The order the goods came against.</param>
/// <param name="WarehouseId">Where they were received.</param>
/// <param name="Status">Whether the stock has been booked in.</param>
/// <param name="ReceivedAt">When the goods arrived.</param>
/// <param name="ReceivedBy">Who took delivery.</param>
/// <param name="Notes">Anything the receiver wrote.</param>
/// <param name="Lines">What arrived.</param>
/// <param name="CreatedAt">When the receipt was opened.</param>
internal sealed record GoodsReceiptResponse(
    Guid Id,
    string Number,
    Guid PurchaseOrderId,
    Guid WarehouseId,
    GoodsReceiptStatus Status,
    DateTimeOffset ReceivedAt,
    Guid? ReceivedBy,
    string? Notes,
    IReadOnlyList<GoodsReceiptLineResponse> Lines,
    DateTimeOffset CreatedAt);

/// <summary>One counted line as the caller states it.</summary>
/// <param name="PurchaseOrderLineId">The ordered line it satisfies.</param>
/// <param name="Accepted">How many were accepted.</param>
/// <param name="Rejected">How many were refused.</param>
/// <param name="RejectionReason">Why they were refused.</param>
/// <param name="BatchCode">The supplier's lot number, for a batch-tracked item.</param>
/// <param name="ExpiresOn">When that lot expires.</param>
internal sealed record GoodsReceiptLinePayload(
    Guid PurchaseOrderLineId,
    int Accepted,
    int Rejected,
    string? RejectionReason,
    string? BatchCode,
    DateOnly? ExpiresOn);

/// <summary>Books goods in against a purchase order, producing a GRN.</summary>
/// <param name="PurchaseOrderId">The order the goods came against.</param>
/// <param name="Notes">Anything the receiver wrote.</param>
/// <param name="Lines">What arrived.</param>
internal sealed record ReceivePurchaseOrderCommand(
    Guid PurchaseOrderId,
    string? Notes,
    IReadOnlyList<GoodsReceiptLinePayload> Lines) : ICommand<GoodsReceiptResponse>;

/// <summary>Lists goods receipts.</summary>
/// <param name="PurchaseOrderId">Restrict to one order.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListGoodsReceiptsQuery(Guid? PurchaseOrderId, string? Cursor, int? Size)
    : IQuery<PagedResult<GoodsReceiptResponse>>;

/// <summary>Reads one goods receipt, lines and all.</summary>
/// <param name="GoodsReceiptId">The document.</param>
internal sealed record GetGoodsReceiptQuery(Guid GoodsReceiptId) : IQuery<GoodsReceiptResponse>;

/// <summary>Rejects a receipt that could never be stored.</summary>
internal sealed class ReceivePurchaseOrderValidator : AbstractValidator<ReceivePurchaseOrderCommand>
{
    public ReceivePurchaseOrderValidator()
    {
        RuleFor(command => command.PurchaseOrderId).NotEmpty();
        RuleFor(command => command.Notes).MaximumLength(2000);
        RuleFor(command => command.Lines).NotEmpty();
        RuleForEach(command => command.Lines).SetValidator(new GoodsReceiptLineValidator());
    }
}

/// <summary>What a counted line has to carry to be bookable.</summary>
internal sealed class GoodsReceiptLineValidator : AbstractValidator<GoodsReceiptLinePayload>
{
    public GoodsReceiptLineValidator()
    {
        RuleFor(line => line.PurchaseOrderLineId).NotEmpty();
        RuleFor(line => line.Accepted).GreaterThanOrEqualTo(0);
        RuleFor(line => line.Rejected).GreaterThanOrEqualTo(0);
        RuleFor(line => line.BatchCode).MaximumLength(64);
        RuleFor(line => line.RejectionReason).MaximumLength(500);

        // A refusal has to say why. A rejected quantity with no reason is a number nobody can take
        // back to the supplier.
        RuleFor(line => line.RejectionReason)
            .NotEmpty()
            .When(line => line.Rejected > 0)
            .WithMessage("Say why the units were refused.");

        RuleFor(line => line)
            .Must(line => line.Accepted > 0 || line.Rejected > 0)
            .WithMessage("A counted line has to record something arriving.");
    }
}

/// <summary>
/// Books goods in: writes the GRN, moves the stock, and advances the order.
/// </summary>
/// <remarks>
/// <para>
/// All three in one transaction. A receipt whose stock movement failed would show units nobody has,
/// and a movement whose receipt failed would show units nobody ordered; neither is recoverable from
/// the other end.
/// </para>
/// <para>
/// Rejected units are recorded and deliberately not booked in. They are the supplier's problem,
/// they never reach the shelf, and the count is the evidence a buyer quotes back to them.
/// </para>
/// </remarks>
/// <param name="context">The Inventory data context.</param>
/// <param name="scope">Refuses a seller receiving somebody else's order, and mints the number.</param>
/// <param name="ledger">Applies each line's movement atomically.</param>
/// <param name="user">Who took delivery.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="audit">Records the receipt.</param>
internal sealed class ReceivePurchaseOrderCommandHandler(
    InventoryDbContext context,
    InventoryScope scope,
    StockLedgerService ledger,
    IUserContext user,
    IClock clock,
    IAuditLogger audit) : ICommandHandler<ReceivePurchaseOrderCommand, GoodsReceiptResponse>
{
    /// <summary>The audited action for a posted goods receipt.</summary>
    public const string AuditAction = "inventory.goods-receipt.posted";

    /// <summary>The entity type recorded against every goods-receipt action.</summary>
    public const string AuditEntityType = "GoodsReceipt";

    /// <summary>The reference type every purchase movement carries.</summary>
    public const string ReferenceType = "goods_receipt";

    public async Task<Result<GoodsReceiptResponse>> HandleAsync(
        ReceivePurchaseOrderCommand command,
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
            return InventoryErrors.OutOfScope;
        }

        if (!order.IsReceivable)
        {
            return InventoryErrors.InvalidTransition(order.Status, PurchaseOrderStatus.PartiallyReceived);
        }

        var number = await scope.NextGoodsReceiptNumberAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;

        var receipt = GoodsReceipt.Open(
            number,
            order.Id,
            order.WarehouseId,
            order.VendorId,
            now,
            user.UserId);

        Result<GoodsReceiptResponse> outcome = InventoryErrors.NotFound("purchase order line");

        await context.ExecuteInTransactionAsync(
            async (_, token) =>
            {
                var lines = new List<GoodsReceiptLine>(command.Lines.Count);

                foreach (var payload in command.Lines)
                {
                    var orderLine = order.Lines
                        .FirstOrDefault(line => line.Id == payload.PurchaseOrderLineId);

                    if (orderLine is null)
                    {
                        outcome = InventoryErrors.NotFound("purchase order line");
                        return;
                    }

                    if (payload.Accepted > 0 && orderLine.QuantityOutstanding == 0)
                    {
                        outcome = InventoryErrors.NothingOutstanding(orderLine.Sku);
                        return;
                    }

                    // Clamped by the line itself rather than trusted: booking in more than was
                    // ordered is the check constraint's job to make impossible and the domain's job
                    // to make unnecessary.
                    var accepted = orderLine.Receive(payload.Accepted);

                    var item = await ResolveStockItemAsync(
                            order.WarehouseId,
                            orderLine.ListingId,
                            orderLine.Sku,
                            order.VendorId,
                            token)
                        .ConfigureAwait(false);

                    if (accepted > 0)
                    {
                        var movement = await ledger
                            .MoveAsync(
                                item,
                                accepted,
                                StockMovementReason.Purchase,
                                ReferenceType,
                                receipt.Id,
                                $"GRN {number} against {order.Number}",
                                user.UserId,
                                token)
                            .ConfigureAwait(false);

                        if (!movement.Applied)
                        {
                            // Unreachable: an inbound movement's only guard is that on hand stays
                            // non-negative. Throwing rolls the whole receipt back rather than
                            // leaving a GRN whose units never arrived.
                            throw new InvalidOperationException(
                                $"The inbound movement for {orderLine.Sku} was refused, which cannot happen.");
                        }

                        await RecordBatchAsync(item, payload, accepted, token).ConfigureAwait(false);
                    }

                    lines.Add(GoodsReceiptLine.Record(
                        receipt.Id,
                        orderLine.Id,
                        item.Id,
                        accepted,
                        payload.Rejected,
                        payload.RejectionReason,
                        payload.BatchCode,
                        payload.ExpiresOn));
                }

                receipt.Record(lines, command.Notes);
                receipt.Post(now);
                order.RefreshReceiptStatus();

                context.GoodsReceipts.Add(receipt);
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                outcome = Result.Success(GoodsReceiptProjection.ToResponse(receipt));
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
                EntityType = AuditEntityType,
                EntityId = receipt.Id.ToString(),
                After = new
                {
                    ReceiptNumber = receipt.Number,
                    OrderNumber = order.Number,
                    Accepted = receipt.Lines.Sum(line => line.QuantityAccepted),
                    Rejected = receipt.Lines.Sum(line => line.QuantityRejected),
                    OrderStatus = order.Status.ToString(),
                },
            },
            cancellationToken).ConfigureAwait(false);

        return outcome;
    }

    /// <summary>
    /// Finds the stock row the units go into, opening one if the destination has never stocked this
    /// offer before.
    /// </summary>
    /// <remarks>
    /// Opening it here is right: a buyer who ordered goods into a location has said where they
    /// belong, and refusing the receipt because nobody pressed "open stock item" first would leave
    /// a pallet on a loading bay with nowhere to be recorded.
    /// </remarks>
    private async Task<StockItem> ResolveStockItemAsync(
        Guid warehouseId,
        Guid listingId,
        string sku,
        Guid? vendorId,
        CancellationToken cancellationToken)
    {
        var item = await context.StockItems
            .FirstOrDefaultAsync(
                candidate => candidate.ListingId == listingId && candidate.WarehouseId == warehouseId,
                cancellationToken)
            .ConfigureAwait(false);

        if (item is not null)
        {
            return item;
        }

        var opened = StockItem.Open(listingId, warehouseId, vendorId, sku);
        context.StockItems.Add(opened);

        return opened;
    }

    /// <summary>Records the lot the units arrived in, when the item is batch-tracked.</summary>
    private async Task RecordBatchAsync(
        StockItem item,
        GoodsReceiptLinePayload payload,
        int accepted,
        CancellationToken cancellationToken)
    {
        if (item.TrackingMode != StockTrackingMode.Batch || string.IsNullOrWhiteSpace(payload.BatchCode))
        {
            return;
        }

        var code = payload.BatchCode.Trim().ToUpperInvariant();

        var batch = await context.Batches
            .FirstOrDefaultAsync(
                candidate => candidate.StockItemId == item.Id && candidate.BatchCode == code,
                cancellationToken)
            .ConfigureAwait(false);

        // A second delivery of the same lot adds to it. Splitting one lot across two rows would
        // make the expiry sweep count it twice.
        if (batch is null)
        {
            context.Batches.Add(StockBatch.Record(
                item.Id,
                code,
                accepted,
                manufacturedOn: null,
                payload.ExpiresOn,
                supplierId: null));

            return;
        }

        batch.Add(accepted);
        batch.Redate(batch.ManufacturedOn, payload.ExpiresOn ?? batch.ExpiresOn);
    }
}

/// <summary>Lists goods receipts.</summary>
/// <param name="context">The Inventory data context.</param>
internal sealed class ListGoodsReceiptsQueryHandler(InventoryDbContext context)
    : IQueryHandler<ListGoodsReceiptsQuery, PagedResult<GoodsReceiptResponse>>
{
    public async Task<Result<PagedResult<GoodsReceiptResponse>>> HandleAsync(
        ListGoodsReceiptsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);
        var rows = context.GoodsReceipts.AsNoTracking();

        if (query.PurchaseOrderId is { } purchaseOrderId)
        {
            rows = rows.Where(receipt => receipt.PurchaseOrderId == purchaseOrderId);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(receipt => receipt.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(receipt => receipt.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        return Result.Success(new PagedResult<GoodsReceiptResponse>(
            [.. page.Select(GoodsReceiptProjection.ToResponse)],
            new PageInfo(size, hasMore ? Cursor.Encode(page[^1].Id.ToString()) : null)));
    }
}

/// <summary>Reads one goods receipt.</summary>
/// <param name="context">The Inventory data context.</param>
internal sealed class GetGoodsReceiptQueryHandler(InventoryDbContext context)
    : IQueryHandler<GetGoodsReceiptQuery, GoodsReceiptResponse>
{
    public async Task<Result<GoodsReceiptResponse>> HandleAsync(
        GetGoodsReceiptQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var receipt = await context.GoodsReceipts
            .AsNoTracking()
            .Include(candidate => candidate.Lines)
            .FirstOrDefaultAsync(candidate => candidate.Id == query.GoodsReceiptId, cancellationToken)
            .ConfigureAwait(false);

        return receipt is null
            ? InventoryErrors.NotFound("goods receipt")
            : Result.Success(GoodsReceiptProjection.ToResponse(receipt));
    }
}

/// <summary>Turns goods receipts into responses.</summary>
internal static class GoodsReceiptProjection
{
    /// <summary>States a goods receipt.</summary>
    /// <param name="receipt">The document.</param>
    public static GoodsReceiptResponse ToResponse(GoodsReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        return new GoodsReceiptResponse(
            receipt.Id,
            receipt.Number,
            receipt.PurchaseOrderId,
            receipt.WarehouseId,
            receipt.Status,
            receipt.ReceivedAt,
            receipt.ReceivedBy,
            receipt.Notes,
            [.. receipt.Lines.Select(ToResponse)],
            receipt.CreatedAt);
    }

    /// <summary>States one counted line.</summary>
    /// <param name="line">The line.</param>
    public static GoodsReceiptLineResponse ToResponse(GoodsReceiptLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return new GoodsReceiptLineResponse(
            line.Id,
            line.PurchaseOrderLineId,
            line.StockItemId,
            line.QuantityAccepted,
            line.QuantityRejected,
            line.RejectionReason,
            line.BatchCode,
            line.ExpiresOn);
    }
}
