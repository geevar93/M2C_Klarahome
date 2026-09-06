using FluentValidation;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Payments.Application.Payments;
using KlaraHome.Modules.Payments.Domain;
using KlaraHome.Modules.Payments.Infrastructure;
using KlaraHome.Modules.Payments.Infrastructure.Events;
using KlaraHome.Modules.Payments.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Payments.Application.Cod;

/// <summary>Lists cash owed and collected at doors.</summary>
/// <param name="Status">Filter by where the cash stands. <c>Collected</c> is what is owed to us.</param>
/// <param name="VendorId">Filter to one seller.</param>
/// <param name="OrderId">Filter to one order.</param>
/// <param name="From">Only records collected on or after this instant.</param>
/// <param name="To">Only records collected strictly before this instant.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListCodCollectionsQuery(
    string? Status,
    Guid? VendorId,
    Guid? OrderId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Cursor,
    int? Size) : IQuery<PagedResult<CodCollectionResponse>>;

/// <summary>Records that the courier took the cash at the door.</summary>
/// <param name="CollectionId">The cash record.</param>
/// <param name="Amount">What was actually taken, which is not always what was owed.</param>
/// <param name="CollectedAt">When, or null for now.</param>
internal sealed record RecordCodCollectionCommand(
    Guid CollectionId,
    decimal Amount,
    DateTimeOffset? CollectedAt) : ICommand<CodCollectionResponse>;

/// <summary>
/// Records a courier's remittance against a batch of collections.
/// </summary>
/// <remarks>
/// A batch rather than one at a time, because that is how the money actually arrives: one bank
/// transfer covering a day's deliveries, with a file listing them. Matching them one by one would be
/// an afternoon's work per remittance.
/// </remarks>
/// <param name="Ids">The cash records the remittance covers.</param>
/// <param name="Reference">The courier's reference — a UTR or a batch number.</param>
/// <param name="Amount">What arrived in total, apportioned across the records.</param>
/// <param name="RemittedAt">When, or null for now.</param>
internal sealed record RecordCodRemittanceCommand(
    IReadOnlyList<Guid> Ids,
    string Reference,
    decimal? Amount,
    DateTimeOffset? RemittedAt) : ICommand<IReadOnlyList<CodCollectionResponse>>;

/// <summary>Validates a cash collection.</summary>
internal sealed class RecordCodCollectionValidator : AbstractValidator<RecordCodCollectionCommand>
{
    public RecordCodCollectionValidator()
        => RuleFor(command => command.Amount).GreaterThanOrEqualTo(0m);
}

/// <summary>Validates a remittance.</summary>
internal sealed class RecordCodRemittanceValidator : AbstractValidator<RecordCodRemittanceCommand>
{
    public RecordCodRemittanceValidator()
    {
        RuleFor(command => command.Ids).NotEmpty();
        RuleFor(command => command.Ids.Count).LessThanOrEqualTo(500);
        RuleFor(command => command.Reference).NotEmpty().MaximumLength(128);
    }
}

/// <summary>Lists cash records, newest first.</summary>
/// <param name="context">The Payments data context.</param>
/// <param name="options">Supplies the page ceiling.</param>
internal sealed class ListCodCollectionsQueryHandler(
    PaymentsDbContext context,
    IOptions<PaymentsOptions> options)
    : IQueryHandler<ListCodCollectionsQuery, PagedResult<CodCollectionResponse>>
{
    public async Task<Result<PagedResult<CodCollectionResponse>>> HandleAsync(
        ListCodCollectionsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Math.Min(Cursor.NormalizeSize(query.Size), options.Value.MaxPageSize);
        var rows = context.CodCollections.AsNoTracking().AsQueryable();

        if (Enum.TryParse<CodCollectionStatus>(query.Status, ignoreCase: true, out var status))
        {
            rows = rows.Where(collection => collection.Status == status);
        }

        if (query.VendorId is { } vendorId)
        {
            rows = rows.Where(collection => collection.VendorId == vendorId);
        }

        if (query.OrderId is { } orderId)
        {
            rows = rows.Where(collection => collection.OrderId == orderId);
        }

        if (query.From is { } from)
        {
            rows = rows.Where(collection => collection.CollectedAt >= from);
        }

        if (query.To is { } to)
        {
            rows = rows.Where(collection => collection.CollectedAt < to);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(collection => collection.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(collection => collection.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var items = page.Take(size).Select(PaymentProjection.ToCod).ToArray();
        var next = hasMore && items.Length > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<CodCollectionResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>Records cash taken at a door.</summary>
/// <param name="context">The Payments data context.</param>
/// <param name="events">Announces the cash movement.</param>
/// <param name="scope">Who recorded it.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class RecordCodCollectionCommandHandler(
    PaymentsDbContext context,
    PaymentsEventPublisher events,
    PaymentsScope scope,
    IClock clock) : ICommandHandler<RecordCodCollectionCommand, CodCollectionResponse>
{
    public async Task<Result<CodCollectionResponse>> HandleAsync(
        RecordCodCollectionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var collection = await context.CodCollections
            .FirstOrDefaultAsync(candidate => candidate.Id == command.CollectionId, cancellationToken)
            .ConfigureAwait(false);

        if (collection is null)
        {
            return Result.Failure<CodCollectionResponse>(PaymentsErrors.NotFound("cash collection"));
        }

        if (!collection.Collect(command.Amount, command.CollectedAt ?? clock.UtcNow, scope.ActorId))
        {
            return Result.Failure<CodCollectionResponse>(PaymentsErrors.CodNotOpen);
        }

        events.CashRecorded(collection);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(PaymentProjection.ToCod(collection));
    }
}

/// <summary>
/// Records a courier's remittance across a batch.
/// </summary>
/// <remarks>
/// <para>
/// When a total is given it is apportioned across the records in proportion to what each was for, so
/// a courier who deducts their fee from the batch leaves every record short by its share rather than
/// leaving the last one in the file short by the whole fee.
/// </para>
/// <para>
/// A record that is already remitted or waived is skipped rather than refused. A remittance file
/// covering a day's deliveries will usually contain one that was handled by hand, and refusing the
/// whole batch for it would make the common case unusable.
/// </para>
/// </remarks>
/// <param name="context">The Payments data context.</param>
/// <param name="events">Announces the cash movements.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class RecordCodRemittanceCommandHandler(
    PaymentsDbContext context,
    PaymentsEventPublisher events,
    IClock clock) : ICommandHandler<RecordCodRemittanceCommand, IReadOnlyList<CodCollectionResponse>>
{
    public async Task<Result<IReadOnlyList<CodCollectionResponse>>> HandleAsync(
        RecordCodRemittanceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var ids = command.Ids.Distinct().ToArray();

        var records = await context.CodCollections
            .Where(collection => ids.Contains(collection.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (records.Count == 0)
        {
            return Result.Failure<IReadOnlyList<CodCollectionResponse>>(
                PaymentsErrors.NotFound("cash collection"));
        }

        var open = records
            .Where(collection => collection.Status is CodCollectionStatus.Pending or CodCollectionStatus.Collected)
            .ToList();

        var expected = open.Sum(collection => collection.CollectedAmount ?? collection.Amount);
        var at = command.RemittedAt ?? clock.UtcNow;

        foreach (var collection in open)
        {
            var owed = collection.CollectedAmount ?? collection.Amount;

            // Proportional where a total was given, and the record's own figure where it was not.
            var share = command.Amount is { } total && expected > 0m
                ? Math.Round(total * (owed / expected), 4, MidpointRounding.AwayFromZero)
                : owed;

            if (collection.Remit(share, command.Reference, at))
            {
                events.CashRecorded(collection);
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success<IReadOnlyList<CodCollectionResponse>>(
            [.. records.Select(PaymentProjection.ToCod)]);
    }
}
