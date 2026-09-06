using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Returns.Infrastructure;
using KlaraHome.Modules.Returns.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Returns.Application.CreditNotes;

/// <summary>The credit notes raised, for the GST return and for finance.</summary>
/// <param name="VendorId">Filter to one seller. Ignored for a seller caller, who has only their own.</param>
/// <param name="FinancialYear">Filter to one financial year, as <c>2026-27</c>.</param>
/// <param name="From">Only notes raised on or after this instant.</param>
/// <param name="To">Only notes raised strictly before this instant.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListCreditNotesQuery(
    Guid? VendorId,
    string? FinancialYear,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Cursor,
    int? Size) : IQuery<PagedResult<CreditNoteResponse>>;

/// <summary>One credit note.</summary>
/// <param name="CreditNoteId">The note.</param>
internal sealed record GetCreditNoteQuery(Guid CreditNoteId) : IQuery<CreditNoteResponse>;

/// <summary>The credit note raised against one return, when one was.</summary>
/// <param name="ReturnId">The RMA.</param>
internal sealed record GetReturnCreditNoteQuery(Guid ReturnId) : IQuery<CreditNoteResponse>;

/// <summary>
/// Lists the credit notes, newest first.
/// </summary>
/// <remarks>
/// The list a GST return is prepared from, which is why the financial year is a filter rather than a
/// date range somebody has to remember runs April to March. A seller sees only their own; the vendor
/// query filter does the confining, exactly as it does for their invoices.
/// </remarks>
/// <param name="context">The Returns data context.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="options">Supplies the page ceiling.</param>
internal sealed class ListCreditNotesQueryHandler(
    ReturnsDbContext context,
    ReturnsScope scope,
    IOptions<ReturnsOptions> options)
    : IQueryHandler<ListCreditNotesQuery, PagedResult<CreditNoteResponse>>
{
    public async Task<Result<PagedResult<CreditNoteResponse>>> HandleAsync(
        ListCreditNotesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Math.Min(Cursor.NormalizeSize(query.Size), options.Value.MaxPageSize);
        var rows = context.CreditNotes.AsNoTracking().AsQueryable();

        if (!scope.IsVendor && query.VendorId is { } vendorId)
        {
            rows = rows.Where(note => note.VendorId == vendorId);
        }

        if (query.FinancialYear is { Length: > 0 } year)
        {
            rows = rows.Where(note => note.FinancialYear == year);
        }

        if (query.From is { } from)
        {
            rows = rows.Where(note => note.IssuedAt >= from);
        }

        if (query.To is { } to)
        {
            rows = rows.Where(note => note.IssuedAt < to);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(note => note.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(note => note.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var items = page.Take(size).Select(ReturnProjection.ToCreditNote).ToArray();
        var next = hasMore && items.Length > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<CreditNoteResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>Reads one credit note.</summary>
/// <param name="context">The Returns data context.</param>
internal sealed class GetCreditNoteQueryHandler(ReturnsDbContext context)
    : IQueryHandler<GetCreditNoteQuery, CreditNoteResponse>
{
    public async Task<Result<CreditNoteResponse>> HandleAsync(
        GetCreditNoteQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var note = await context.CreditNotes
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == query.CreditNoteId, cancellationToken)
            .ConfigureAwait(false);

        return note is null
            ? Result.Failure<CreditNoteResponse>(ReturnsErrors.NotFound("credit note"))
            : Result.Success(ReturnProjection.ToCreditNote(note));
    }
}

/// <summary>
/// Reads the credit note raised against one return.
/// </summary>
/// <remarks>
/// The shopper's route to their own copy, and the reason it is keyed on the return rather than on
/// the note: a shopper knows their RMA number and has no reason ever to have seen a credit-note id.
/// It is confined to their own returns by the caller's token, never by a parameter.
/// </remarks>
/// <param name="context">The Returns data context.</param>
/// <param name="scope">Who is asking.</param>
internal sealed class GetReturnCreditNoteQueryHandler(ReturnsDbContext context, ReturnsScope scope)
    : IQueryHandler<GetReturnCreditNoteQuery, CreditNoteResponse>
{
    public async Task<Result<CreditNoteResponse>> HandleAsync(
        GetReturnCreditNoteQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var owns = await context.Returns
            .AsNoTracking()
            .AnyAsync(
                request => request.Id == query.ReturnId && request.CustomerId == scope.CustomerId,
                cancellationToken)
            .ConfigureAwait(false);

        if (!owns)
        {
            return Result.Failure<CreditNoteResponse>(ReturnsErrors.NotFound("return"));
        }

        var note = await context.CreditNotes
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                candidate => candidate.ReturnId == query.ReturnId
                             && candidate.TenantId == context.TenantId,
                cancellationToken)
            .ConfigureAwait(false);

        // The vendor filter is bypassed deliberately and the tenant is asserted by hand in its
        // place. A credit note belongs to a seller, and a shopper is not one — without this, a
        // customer reading their own note would be filtered out of their own document.
        return note is null
            ? Result.Failure<CreditNoteResponse>(ReturnsErrors.NotFound("credit note"))
            : Result.Success(ReturnProjection.ToCreditNote(note));
    }
}
