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
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Inventory.Application.StockTakes;

/// <summary>One counted row of a stock take, as the API states it.</summary>
/// <param name="Id">The line.</param>
/// <param name="StockItemId">The stock row being counted.</param>
/// <param name="Sku">The SKU.</param>
/// <param name="ExpectedQuantity">The book figure, frozen when counting opened.</param>
/// <param name="CountedQuantity">What the counter found, or null if not yet counted.</param>
/// <param name="Variance">Counted less expected.</param>
/// <param name="Note">What the counter wrote.</param>
internal sealed record StockTakeLineResponse(
    Guid Id,
    Guid StockItemId,
    string Sku,
    int ExpectedQuantity,
    int? CountedQuantity,
    int? Variance,
    string? Note);

/// <summary>A stock take, as the API states it.</summary>
/// <param name="Id">The sheet.</param>
/// <param name="Number">Its number.</param>
/// <param name="WarehouseId">The location being counted.</param>
/// <param name="Status">Where it is in its life.</param>
/// <param name="ScheduledFor">When the count is to be taken.</param>
/// <param name="SubmittedAt">When the variances were posted.</param>
/// <param name="SubmittedBy">Who submitted it.</param>
/// <param name="Notes">Anything the counter wrote.</param>
/// <param name="Lines">The counted rows.</param>
/// <param name="CreatedAt">When it was scheduled.</param>
internal sealed record StockTakeResponse(
    Guid Id,
    string Number,
    Guid WarehouseId,
    StockTakeStatus Status,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? SubmittedAt,
    Guid? SubmittedBy,
    string? Notes,
    IReadOnlyList<StockTakeLineResponse> Lines,
    DateTimeOffset CreatedAt);

/// <summary>One count as the caller states it.</summary>
/// <param name="StockItemId">The stock row counted.</param>
/// <param name="CountedQuantity">What was found.</param>
/// <param name="Note">What the counter wrote.</param>
internal sealed record StockTakeCountPayload(Guid StockItemId, int CountedQuantity, string? Note);

/// <summary>Lists stock takes.</summary>
/// <param name="Status">Restrict to one status.</param>
/// <param name="WarehouseId">Restrict to one location.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListStockTakesQuery(
    string? Status,
    Guid? WarehouseId,
    string? Cursor,
    int? Size) : IQuery<PagedResult<StockTakeResponse>>;

/// <summary>Reads one stock take, sheet and all.</summary>
/// <param name="StockTakeId">The sheet.</param>
internal sealed record GetStockTakeQuery(Guid StockTakeId) : IQuery<StockTakeResponse>;

/// <summary>
/// Schedules a count and opens the sheet with today's book figures frozen onto it.
/// </summary>
/// <param name="WarehouseId">The location to count.</param>
/// <param name="ScheduledFor">When the count is to be taken.</param>
/// <param name="Notes">Anything the planner wrote.</param>
/// <param name="StockItemIds">Restrict the sheet to these rows, or empty for the whole location.</param>
internal sealed record CreateStockTakeCommand(
    Guid WarehouseId,
    DateTimeOffset? ScheduledFor,
    string? Notes,
    IReadOnlyList<Guid> StockItemIds) : ICommand<StockTakeResponse>;

/// <summary>Records what the counter found.</summary>
/// <param name="StockTakeId">The sheet.</param>
/// <param name="Counts">The counts.</param>
internal sealed record RecordStockTakeCountsCommand(
    Guid StockTakeId,
    IReadOnlyList<StockTakeCountPayload> Counts) : ICommand<StockTakeResponse>;

/// <summary>Posts the sheet's variances to the ledger.</summary>
/// <param name="StockTakeId">The sheet.</param>
internal sealed record SubmitStockTakeCommand(Guid StockTakeId) : ICommand<StockTakeResponse>;

/// <summary>Abandons a count without posting anything.</summary>
/// <param name="StockTakeId">The sheet.</param>
internal sealed record CancelStockTakeCommand(Guid StockTakeId) : ICommand<StockTakeResponse>;

/// <summary>Rejects a sheet that could never be stored.</summary>
internal sealed class CreateStockTakeValidator : AbstractValidator<CreateStockTakeCommand>
{
    public CreateStockTakeValidator()
    {
        RuleFor(command => command.WarehouseId).NotEmpty();
        RuleFor(command => command.Notes).MaximumLength(2000);
    }
}

/// <summary>Rejects counts that could never be stored.</summary>
internal sealed class RecordStockTakeCountsValidator : AbstractValidator<RecordStockTakeCountsCommand>
{
    public RecordStockTakeCountsValidator()
    {
        RuleFor(command => command.StockTakeId).NotEmpty();
        RuleFor(command => command.Counts).NotEmpty();
        RuleForEach(command => command.Counts).ChildRules(count =>
        {
            count.RuleFor(payload => payload.StockItemId).NotEmpty();
            count.RuleFor(payload => payload.CountedQuantity).GreaterThanOrEqualTo(0);
            count.RuleFor(payload => payload.Note).MaximumLength(500);
        });
    }
}

/// <summary>Lists stock takes.</summary>
/// <param name="context">The Inventory data context.</param>
internal sealed class ListStockTakesQueryHandler(InventoryDbContext context)
    : IQueryHandler<ListStockTakesQuery, PagedResult<StockTakeResponse>>
{
    public async Task<Result<PagedResult<StockTakeResponse>>> HandleAsync(
        ListStockTakesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);

        // Without the lines: a sheet can carry thousands, and a list screen shows status and dates.
        var rows = context.StockTakes.AsNoTracking();

        if (Enum.TryParse<StockTakeStatus>(query.Status, ignoreCase: true, out var status))
        {
            rows = rows.Where(take => take.Status == status);
        }

        if (query.WarehouseId is { } warehouseId)
        {
            rows = rows.Where(take => take.WarehouseId == warehouseId);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(take => take.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(take => take.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        return Result.Success(new PagedResult<StockTakeResponse>(
            [.. page.Select(StockTakeProjection.ToResponse)],
            new PageInfo(size, hasMore ? Cursor.Encode(page[^1].Id.ToString()) : null)));
    }
}

/// <summary>Reads one stock take.</summary>
/// <param name="context">The Inventory data context.</param>
internal sealed class GetStockTakeQueryHandler(InventoryDbContext context)
    : IQueryHandler<GetStockTakeQuery, StockTakeResponse>
{
    public async Task<Result<StockTakeResponse>> HandleAsync(
        GetStockTakeQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var take = await context.StockTakes
            .AsNoTracking()
            .Include(candidate => candidate.Lines)
            .FirstOrDefaultAsync(candidate => candidate.Id == query.StockTakeId, cancellationToken)
            .ConfigureAwait(false);

        return take is null
            ? InventoryErrors.NotFound("stock take")
            : Result.Success(StockTakeProjection.ToResponse(take));
    }
}

/// <summary>
/// Schedules a count and freezes the book figures onto the sheet.
/// </summary>
/// <remarks>
/// Frozen now rather than read at submission, and that is the whole design. A count taken on
/// Tuesday and submitted on Thursday must be compared against Tuesday's book figure, or every sale
/// in between becomes a phantom variance and the recount "corrects" a discrepancy that never
/// existed.
/// </remarks>
/// <param name="context">The Inventory data context.</param>
/// <param name="scope">Refuses a seller counting somebody else's location, and mints the number.</param>
/// <param name="options">The sheet-size limit.</param>
internal sealed class CreateStockTakeCommandHandler(
    InventoryDbContext context,
    InventoryScope scope,
    IOptions<InventoryOptions> options) : ICommandHandler<CreateStockTakeCommand, StockTakeResponse>
{
    public async Task<Result<StockTakeResponse>> HandleAsync(
        CreateStockTakeCommand command,
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
            return InventoryErrors.PlatformOnly;
        }

        var rows = context.StockItems
            .AsNoTracking()
            .Where(item => item.WarehouseId == warehouse.Id);

        if (command.StockItemIds.Count > 0)
        {
            var ids = command.StockItemIds.Distinct().ToArray();
            rows = rows.Where(item => ids.Contains(item.Id));
        }

        var items = await rows
            .OrderBy(item => item.Sku)
            .Take(options.Value.MaxStockTakeLines + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (items.Count > options.Value.MaxStockTakeLines)
        {
            return InventoryErrors.StockTakeTooLarge(options.Value.MaxStockTakeLines);
        }

        var number = await scope.NextStockTakeNumberAsync(cancellationToken).ConfigureAwait(false);

        var take = StockTake.Schedule(
            number,
            warehouse.Id,
            scope.OwnerFor(warehouse.VendorId),
            command.ScheduledFor,
            command.Notes);

        take.BeginCounting(items.Select(item =>
            StockTakeLine.Expect(take.Id, item.Id, item.Sku, item.QuantityOnHand)));

        context.StockTakes.Add(take);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(StockTakeProjection.ToResponse(take));
    }
}

/// <summary>Records what the counter found.</summary>
/// <param name="context">The Inventory data context.</param>
/// <param name="scope">Refuses a seller counting somebody else's location.</param>
internal sealed class RecordStockTakeCountsCommandHandler(InventoryDbContext context, InventoryScope scope)
    : ICommandHandler<RecordStockTakeCountsCommand, StockTakeResponse>
{
    public async Task<Result<StockTakeResponse>> HandleAsync(
        RecordStockTakeCountsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var take = await context.StockTakes
            .Include(candidate => candidate.Lines)
            .FirstOrDefaultAsync(candidate => candidate.Id == command.StockTakeId, cancellationToken)
            .ConfigureAwait(false);

        if (take is null)
        {
            return InventoryErrors.NotFound("stock take");
        }

        if (!scope.CanWrite(take.VendorId))
        {
            return InventoryErrors.PlatformOnly;
        }

        if (!take.IsOpen)
        {
            return InventoryErrors.DocumentFrozen;
        }

        // Partial counts are the normal case: a counter works a section at a time and sends what
        // they have, so a payload naming rows that are not on this sheet is skipped rather than
        // rejecting the whole submission.
        var byItem = take.Lines.ToDictionary(line => line.StockItemId);

        foreach (var count in command.Counts)
        {
            if (byItem.TryGetValue(count.StockItemId, out var line))
            {
                line.Count(count.CountedQuantity, count.Note);
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(StockTakeProjection.ToResponse(take));
    }
}

/// <summary>
/// Posts the sheet's variances to the ledger.
/// </summary>
/// <remarks>
/// One <see cref="StockMovementReason.Correction"/> entry per non-zero variance, in one
/// transaction. Uncounted rows are left alone: "nobody counted this shelf" and "this shelf is
/// empty" are different facts, and posting the first as the second would write off stock that is
/// simply still on it.
/// </remarks>
/// <param name="context">The Inventory data context.</param>
/// <param name="scope">Refuses a seller submitting somebody else's sheet.</param>
/// <param name="ledger">Applies each correction atomically.</param>
/// <param name="user">Who submitted it.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="audit">Records the submission.</param>
internal sealed class SubmitStockTakeCommandHandler(
    InventoryDbContext context,
    InventoryScope scope,
    StockLedgerService ledger,
    IUserContext user,
    IClock clock,
    IAuditLogger audit) : ICommandHandler<SubmitStockTakeCommand, StockTakeResponse>
{
    /// <summary>The audited action for a submitted stock take.</summary>
    public const string AuditAction = "inventory.stock-take.submitted";

    /// <summary>The entity type recorded against every stock-take action.</summary>
    public const string AuditEntityType = "StockTake";

    /// <summary>The reference type every correction carries.</summary>
    public const string ReferenceType = "stock_take";

    public async Task<Result<StockTakeResponse>> HandleAsync(
        SubmitStockTakeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var take = await context.StockTakes
            .Include(candidate => candidate.Lines)
            .FirstOrDefaultAsync(candidate => candidate.Id == command.StockTakeId, cancellationToken)
            .ConfigureAwait(false);

        if (take is null)
        {
            return InventoryErrors.NotFound("stock take");
        }

        if (!scope.CanWrite(take.VendorId))
        {
            return InventoryErrors.PlatformOnly;
        }

        if (take.Status != StockTakeStatus.Counting)
        {
            return InventoryErrors.InvalidTransition(take.Status, StockTakeStatus.Submitted);
        }

        var now = clock.UtcNow;
        var posted = 0;
        var refused = new List<string>();

        await context.ExecuteInTransactionAsync(
            async (_, token) =>
            {
                foreach (var line in take.Lines)
                {
                    if (line.Variance is not { } variance || variance == 0)
                    {
                        continue;
                    }

                    var item = await context.StockItems
                        .FirstOrDefaultAsync(
                            candidate => candidate.Id == line.StockItemId,
                            token)
                        .ConfigureAwait(false);

                    if (item is null)
                    {
                        continue;
                    }

                    var movement = await ledger
                        .MoveAsync(
                            item,
                            variance,
                            StockMovementReason.Correction,
                            ReferenceType,
                            take.Id,
                            $"Stock take {take.Number}: counted {line.CountedQuantity}, "
                            + $"expected {line.ExpectedQuantity}",
                            user.UserId,
                            token)
                        .ConfigureAwait(false);

                    // A downward correction can be refused when sales since the freeze have already
                    // taken the shelf below the variance. The row is reported rather than posted:
                    // forcing it would drive on hand negative, and the recount is stale anyway.
                    if (movement.Applied)
                    {
                        posted++;
                    }
                    else
                    {
                        refused.Add(line.Sku);
                    }
                }

                take.Submit(now, user.UserId);

                await context.SaveChangesAsync(token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AuditEntityType,
                EntityId = take.Id.ToString(),
                After = new
                {
                    take.Number,
                    take.WarehouseId,
                    CorrectionsPosted = posted,
                    RefusedSkus = refused,
                },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(StockTakeProjection.ToResponse(take));
    }
}

/// <summary>Abandons a count.</summary>
/// <param name="context">The Inventory data context.</param>
/// <param name="scope">Refuses a seller cancelling somebody else's sheet.</param>
internal sealed class CancelStockTakeCommandHandler(InventoryDbContext context, InventoryScope scope)
    : ICommandHandler<CancelStockTakeCommand, StockTakeResponse>
{
    public async Task<Result<StockTakeResponse>> HandleAsync(
        CancelStockTakeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var take = await context.StockTakes
            .Include(candidate => candidate.Lines)
            .FirstOrDefaultAsync(candidate => candidate.Id == command.StockTakeId, cancellationToken)
            .ConfigureAwait(false);

        if (take is null)
        {
            return InventoryErrors.NotFound("stock take");
        }

        if (!scope.CanWrite(take.VendorId))
        {
            return InventoryErrors.PlatformOnly;
        }

        if (!take.Cancel())
        {
            return InventoryErrors.InvalidTransition(take.Status, StockTakeStatus.Cancelled);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(StockTakeProjection.ToResponse(take));
    }
}

/// <summary>Turns stock takes into responses.</summary>
internal static class StockTakeProjection
{
    /// <summary>States a stock take.</summary>
    /// <param name="take">The sheet.</param>
    public static StockTakeResponse ToResponse(StockTake take)
    {
        ArgumentNullException.ThrowIfNull(take);

        return new StockTakeResponse(
            take.Id,
            take.Number,
            take.WarehouseId,
            take.Status,
            take.ScheduledFor,
            take.SubmittedAt,
            take.SubmittedBy,
            take.Notes,
            [.. take.Lines.Select(ToResponse)],
            take.CreatedAt);
    }

    /// <summary>States one counted row.</summary>
    /// <param name="line">The line.</param>
    public static StockTakeLineResponse ToResponse(StockTakeLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return new StockTakeLineResponse(
            line.Id,
            line.StockItemId,
            line.Sku,
            line.ExpectedQuantity,
            line.CountedQuantity,
            line.Variance,
            line.Note);
    }
}
