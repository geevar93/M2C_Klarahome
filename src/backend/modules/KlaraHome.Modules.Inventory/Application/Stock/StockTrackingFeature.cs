using FluentValidation;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Inventory.Domain;
using KlaraHome.Modules.Inventory.Infrastructure;
using KlaraHome.Modules.Inventory.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Inventory.Application.Stock;

/// <summary>Lists the lots held against a stock row, soonest to expire first.</summary>
/// <param name="StockItemId">The stock row.</param>
internal sealed record ListStockBatchesQuery(Guid StockItemId) : IQuery<IReadOnlyList<StockBatchResponse>>;

/// <summary>Records a lot against a stock row.</summary>
/// <param name="StockItemId">The stock row.</param>
/// <param name="BatchCode">The supplier's lot number.</param>
/// <param name="Quantity">How many units of it are held.</param>
/// <param name="ManufacturedOn">When it was made.</param>
/// <param name="ExpiresOn">When it expires.</param>
/// <param name="SupplierId">Who it came from.</param>
internal sealed record RecordStockBatchCommand(
    Guid StockItemId,
    string BatchCode,
    int Quantity,
    DateOnly? ManufacturedOn,
    DateOnly? ExpiresOn,
    Guid? SupplierId) : ICommand<StockBatchResponse>;

/// <summary>Lists the individually identified units held against a stock row.</summary>
/// <param name="StockItemId">The stock row.</param>
/// <param name="Status">Restrict to one status.</param>
internal sealed record ListStockSerialsQuery(Guid StockItemId, string? Status)
    : IQuery<IReadOnlyList<StockSerialResponse>>;

/// <summary>Books individually identified units in against a stock row.</summary>
/// <param name="StockItemId">The stock row.</param>
/// <param name="SerialNumbers">The manufacturers' numbers.</param>
/// <param name="BatchId">The lot they came in.</param>
internal sealed record RecordStockSerialsCommand(
    Guid StockItemId,
    IReadOnlyList<string> SerialNumbers,
    Guid? BatchId) : ICommand<IReadOnlyList<StockSerialResponse>>;

/// <summary>Rejects a lot that could never be stored.</summary>
internal sealed class RecordStockBatchValidator : AbstractValidator<RecordStockBatchCommand>
{
    public RecordStockBatchValidator()
    {
        RuleFor(command => command.StockItemId).NotEmpty();
        RuleFor(command => command.BatchCode).NotEmpty().MaximumLength(64);
        RuleFor(command => command.Quantity).GreaterThanOrEqualTo(0);

        RuleFor(command => command.ExpiresOn)
            .GreaterThanOrEqualTo(command => command.ManufacturedOn)
            .When(command => command.ManufacturedOn is not null && command.ExpiresOn is not null)
            .WithMessage("A lot cannot expire before it was made.");
    }
}

/// <summary>Rejects serials that could never be stored.</summary>
internal sealed class RecordStockSerialsValidator : AbstractValidator<RecordStockSerialsCommand>
{
    /// <summary>The most units one call may book in. A pallet, not a container.</summary>
    public const int MaxSerialsPerCall = 500;

    public RecordStockSerialsValidator()
    {
        RuleFor(command => command.StockItemId).NotEmpty();
        RuleFor(command => command.SerialNumbers).NotEmpty();
        RuleFor(command => command.SerialNumbers.Count).LessThanOrEqualTo(MaxSerialsPerCall);
        RuleForEach(command => command.SerialNumbers).NotEmpty().MaximumLength(96);
    }
}

/// <summary>Lists the lots held against a stock row.</summary>
/// <param name="context">The Inventory data context.</param>
internal sealed class ListStockBatchesQueryHandler(InventoryDbContext context)
    : IQueryHandler<ListStockBatchesQuery, IReadOnlyList<StockBatchResponse>>
{
    public async Task<Result<IReadOnlyList<StockBatchResponse>>> HandleAsync(
        ListStockBatchesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // The vendor filter is on the stock item, so reading the batches is gated by the right to
        // read the row they hang off.
        if (!await context.StockItems
                .AnyAsync(item => item.Id == query.StockItemId, cancellationToken)
                .ConfigureAwait(false))
        {
            return InventoryErrors.NotFound("stock item");
        }

        // Nulls last: a lot with no expiry never becomes urgent, and putting it at the top of a
        // list ordered by urgency is the opposite of useful.
        var batches = await context.Batches
            .AsNoTracking()
            .Where(batch => batch.StockItemId == query.StockItemId)
            .OrderBy(batch => batch.ExpiresOn == null)
            .ThenBy(batch => batch.ExpiresOn)
            .ThenBy(batch => batch.BatchCode)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<StockBatchResponse>>(
            [.. batches.Select(StockProjection.ToResponse)]);
    }
}

/// <summary>Records a lot.</summary>
/// <param name="context">The Inventory data context.</param>
/// <param name="scope">Refuses a seller recording against somebody else's stock.</param>
internal sealed class RecordStockBatchCommandHandler(InventoryDbContext context, InventoryScope scope)
    : ICommandHandler<RecordStockBatchCommand, StockBatchResponse>
{
    public async Task<Result<StockBatchResponse>> HandleAsync(
        RecordStockBatchCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await context.StockItems
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == command.StockItemId, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return InventoryErrors.NotFound("stock item");
        }

        if (!scope.CanWrite(item.VendorId))
        {
            return InventoryErrors.PlatformOnly;
        }

        var code = command.BatchCode.Trim().ToUpperInvariant();

        var existing = await context.Batches
            .FirstOrDefaultAsync(
                batch => batch.StockItemId == item.Id && batch.BatchCode == code,
                cancellationToken)
            .ConfigureAwait(false);

        // A second delivery of the same lot adds to it rather than colliding with it. Refusing
        // would make a legitimate split delivery look like a data-entry mistake.
        if (existing is not null)
        {
            existing.Add(command.Quantity);
            existing.Redate(command.ManufacturedOn, command.ExpiresOn);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(StockProjection.ToResponse(existing));
        }

        var batch = StockBatch.Record(
            item.Id,
            code,
            command.Quantity,
            command.ManufacturedOn,
            command.ExpiresOn,
            command.SupplierId);

        context.Batches.Add(batch);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(StockProjection.ToResponse(batch));
    }
}

/// <summary>Lists the individually identified units held against a stock row.</summary>
/// <param name="context">The Inventory data context.</param>
internal sealed class ListStockSerialsQueryHandler(InventoryDbContext context)
    : IQueryHandler<ListStockSerialsQuery, IReadOnlyList<StockSerialResponse>>
{
    public async Task<Result<IReadOnlyList<StockSerialResponse>>> HandleAsync(
        ListStockSerialsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await context.StockItems
                .AnyAsync(item => item.Id == query.StockItemId, cancellationToken)
                .ConfigureAwait(false))
        {
            return InventoryErrors.NotFound("stock item");
        }

        var rows = context.Serials
            .AsNoTracking()
            .Where(serial => serial.StockItemId == query.StockItemId);

        if (Enum.TryParse<SerialStatus>(query.Status, ignoreCase: true, out var status))
        {
            rows = rows.Where(serial => serial.Status == status);
        }

        var serials = await rows
            .OrderBy(serial => serial.SerialNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<StockSerialResponse>>(
            [.. serials.Select(StockProjection.ToResponse)]);
    }
}

/// <summary>Books individually identified units in.</summary>
/// <param name="context">The Inventory data context.</param>
/// <param name="scope">Refuses a seller recording against somebody else's stock.</param>
internal sealed class RecordStockSerialsCommandHandler(InventoryDbContext context, InventoryScope scope)
    : ICommandHandler<RecordStockSerialsCommand, IReadOnlyList<StockSerialResponse>>
{
    public async Task<Result<IReadOnlyList<StockSerialResponse>>> HandleAsync(
        RecordStockSerialsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await context.StockItems
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == command.StockItemId, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return InventoryErrors.NotFound("stock item");
        }

        if (!scope.CanWrite(item.VendorId))
        {
            return InventoryErrors.PlatformOnly;
        }

        var numbers = command.SerialNumbers
            .Select(number => number.Trim().ToUpperInvariant())
            .Where(number => number.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Already-known numbers are skipped rather than refused. Booking in a pallet is a scan, and
        // a scanner that stutters on one unit should not make the operator start the pallet again.
        var known = await context.Serials
            .Where(serial => serial.StockItemId == item.Id && numbers.Contains(serial.SerialNumber))
            .Select(serial => serial.SerialNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var added = numbers
            .Where(number => !known.Contains(number, StringComparer.Ordinal))
            .Select(number => StockSerial.Record(item.Id, number, command.BatchId))
            .ToList();

        if (added.Count > 0)
        {
            context.Serials.AddRange(added);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result.Success<IReadOnlyList<StockSerialResponse>>(
            [.. added.Select(StockProjection.ToResponse)]);
    }
}
