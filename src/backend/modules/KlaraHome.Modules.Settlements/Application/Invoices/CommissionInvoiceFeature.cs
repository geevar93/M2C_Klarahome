using KlaraHome.Contracts.Media;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Settlements.Application;
using KlaraHome.Modules.Settlements.Domain;
using KlaraHome.Modules.Settlements.Infrastructure;
using KlaraHome.Modules.Settlements.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Settlements.Application.Invoices;

/// <summary>The platform's own tax invoice, as a seller or an operator reads it.</summary>
/// <param name="Id">The invoice.</param>
/// <param name="SettlementCycleId">The period it covers.</param>
/// <param name="VendorId">The seller it is raised on.</param>
/// <param name="InvoiceNumber">The statutory number.</param>
/// <param name="IssuedAt">When it was raised.</param>
/// <param name="PeriodStart">The first instant of the period.</param>
/// <param name="PeriodEnd">The last instant of the period.</param>
/// <param name="Commission">Commission charged, before tax.</param>
/// <param name="PlatformFee">The marketplace fee, before tax.</param>
/// <param name="PaymentFee">The gateway fee recharged, before tax.</param>
/// <param name="TaxableValue">What the tax was computed on.</param>
/// <param name="GstRate">The rate applied, as a percentage.</param>
/// <param name="Cgst">Central GST.</param>
/// <param name="Sgst">State GST.</param>
/// <param name="Igst">Integrated GST.</param>
/// <param name="TaxTotal">The tax, however it split.</param>
/// <param name="Total">What the invoice comes to.</param>
/// <param name="CurrencyCode">The currency every amount is in.</param>
/// <param name="SupplierGstin">The platform's GSTIN, frozen at issue.</param>
/// <param name="RecipientGstin">The seller's, or null if they are not registered.</param>
/// <param name="PlaceOfSupplyStateCode">The two-digit state code the supply was made to.</param>
/// <param name="HasDocument">Whether a PDF exists to download.</param>
internal sealed record CommissionInvoiceResponse(
    Guid Id,
    Guid SettlementCycleId,
    Guid VendorId,
    string InvoiceNumber,
    DateTimeOffset IssuedAt,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    decimal Commission,
    decimal PlatformFee,
    decimal PaymentFee,
    decimal TaxableValue,
    decimal GstRate,
    decimal Cgst,
    decimal Sgst,
    decimal Igst,
    decimal TaxTotal,
    decimal Total,
    string CurrencyCode,
    string? SupplierGstin,
    string? RecipientGstin,
    string? PlaceOfSupplyStateCode,
    bool HasDocument);

/// <summary>A short-lived link to a commission invoice's PDF.</summary>
/// <param name="InvoiceId">The invoice.</param>
/// <param name="InvoiceNumber">Its number, so a saved file can be named.</param>
/// <param name="Url">The signed link. Minting it is the grant.</param>
internal sealed record CommissionInvoiceDownloadResponse(Guid InvoiceId, string InvoiceNumber, string Url);

/// <summary>Lists the platform's own invoices, newest first.</summary>
/// <param name="VendorId">One seller. Ignored for a seller's own token, which is already scoped.</param>
/// <param name="Cursor">Keyset cursor from the previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListCommissionInvoicesQuery(Guid? VendorId, string? Cursor, int? Size)
    : IQuery<PagedResult<CommissionInvoiceResponse>>;

/// <summary>Reads one of the platform's invoices.</summary>
/// <param name="Id">The invoice.</param>
internal sealed record GetCommissionInvoiceQuery(Guid Id) : IQuery<CommissionInvoiceResponse>;

/// <summary>Mints a link to a commission invoice's PDF.</summary>
/// <param name="Id">The invoice.</param>
internal sealed record GetCommissionInvoiceDownloadQuery(Guid Id) : IQuery<CommissionInvoiceDownloadResponse>;

/// <summary>Projects the stored invoice onto its API shape.</summary>
internal static class CommissionInvoiceProjection
{
    /// <summary>Projects one invoice.</summary>
    /// <param name="invoice">The stored invoice.</param>
    public static CommissionInvoiceResponse ToResponse(CommissionInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        return new CommissionInvoiceResponse(
            invoice.Id,
            invoice.SettlementCycleId,
            invoice.VendorId,
            invoice.InvoiceNumber,
            invoice.IssuedAt,
            invoice.PeriodStart,
            invoice.PeriodEnd,
            invoice.Commission,
            invoice.PlatformFee,
            invoice.PaymentFee,
            invoice.TaxableValue,
            invoice.GstRate,
            invoice.Cgst,
            invoice.Sgst,
            invoice.Igst,
            invoice.TaxTotal,
            invoice.Total,
            invoice.CurrencyCode,
            invoice.SupplierGstin,
            invoice.RecipientGstin,
            invoice.PlaceOfSupplyStateCode,
            invoice.FileId is not null);
    }
}

/// <summary>
/// Lists the platform's own invoices.
/// </summary>
/// <remarks>
/// Vendor-scoped by the caller's token rather than by an id in the query string, exactly as every
/// other settlement read is: a seller reading their own invoices and a manager reading everybody's
/// run the same query, and the only difference is whether the caller carries a vendor id.
/// </remarks>
/// <param name="context">The Settlements data context.</param>
/// <param name="scope">Who is asking.</param>
internal sealed class ListCommissionInvoicesQueryHandler(SettlementsDbContext context, SettlementsScope scope)
    : IQueryHandler<ListCommissionInvoicesQuery, PagedResult<CommissionInvoiceResponse>>
{
    public async Task<Result<PagedResult<CommissionInvoiceResponse>>> HandleAsync(
        ListCommissionInvoicesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);
        var rows = context.CommissionInvoices.AsNoTracking().AsQueryable();

        if (scope.VendorFilter(query.VendorId) is { } vendorId)
        {
            rows = rows.Where(invoice => invoice.VendorId == vendorId);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(invoice => invoice.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(invoice => invoice.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var claimed = page.Take(size).ToList();

        var next = hasMore && claimed.Count > 0 ? Cursor.Encode(claimed[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<CommissionInvoiceResponse>(
            [.. claimed.Select(CommissionInvoiceProjection.ToResponse)],
            new PageInfo(size, next)));
    }
}

/// <summary>Reads one invoice, within the caller's scope.</summary>
/// <param name="context">The Settlements data context.</param>
internal sealed class GetCommissionInvoiceQueryHandler(SettlementsDbContext context)
    : IQueryHandler<GetCommissionInvoiceQuery, CommissionInvoiceResponse>
{
    public async Task<Result<CommissionInvoiceResponse>> HandleAsync(
        GetCommissionInvoiceQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // The vendor query filter is what confines a seller here: an invoice raised on somebody else
        // does not exist for them, and they get the same 404 an invented id gets.
        var invoice = await context.CommissionInvoices
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == query.Id, cancellationToken)
            .ConfigureAwait(false);

        return invoice is null
            ? Result.Failure<CommissionInvoiceResponse>(SettlementsErrors.NotFound("commission invoice"))
            : Result.Success(CommissionInvoiceProjection.ToResponse(invoice));
    }
}

/// <summary>
/// Mints a short-lived link to the invoice PDF.
/// </summary>
/// <remarks>
/// A link rather than the bytes, as every other document on this platform is served: minting it is
/// the grant, and the URL carries no authorisation of its own beyond its expiry.
/// </remarks>
/// <param name="context">The Settlements data context.</param>
/// <param name="media">Signs the link.</param>
internal sealed class GetCommissionInvoiceDownloadQueryHandler(SettlementsDbContext context, IMediaLibrary media)
    : IQueryHandler<GetCommissionInvoiceDownloadQuery, CommissionInvoiceDownloadResponse>
{
    public async Task<Result<CommissionInvoiceDownloadResponse>> HandleAsync(
        GetCommissionInvoiceDownloadQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var invoice = await context.CommissionInvoices
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == query.Id, cancellationToken)
            .ConfigureAwait(false);

        if (invoice is null)
        {
            return Result.Failure<CommissionInvoiceDownloadResponse>(
                SettlementsErrors.NotFound("commission invoice"));
        }

        if (invoice.FileId is not { } fileId)
        {
            return Result.Failure<CommissionInvoiceDownloadResponse>(SettlementsErrors.InvoiceDocumentMissing);
        }

        var url = await media.GetSignedUrlAsync(fileId, cancellationToken).ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(url)
            ? Result.Failure<CommissionInvoiceDownloadResponse>(SettlementsErrors.InvoiceDocumentMissing)
            : Result.Success(new CommissionInvoiceDownloadResponse(invoice.Id, invoice.InvoiceNumber, url));
    }
}
