using KlaraHome.Contracts.Media;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Orders.Application.Orders;
using KlaraHome.Modules.Orders.Infrastructure;
using KlaraHome.Modules.Orders.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Orders.Application.Invoices;

/// <summary>A short-lived link to an invoice document.</summary>
/// <param name="InvoiceId">The invoice.</param>
/// <param name="InvoiceNumber">Its number, for the download name.</param>
/// <param name="Url">The signed URL. Minting it is the grant, so it is issued only after the check.</param>
internal sealed record InvoiceDownloadResponse(Guid InvoiceId, string InvoiceNumber, string Url);

/// <summary>Lists the invoices raised against one order.</summary>
/// <param name="OrderId">The order.</param>
internal sealed record ListOrderInvoicesQuery(Guid OrderId) : IQuery<IReadOnlyList<InvoiceResponse>>;

/// <summary>Mints a download link for one invoice.</summary>
/// <param name="InvoiceId">The invoice.</param>
internal sealed record GetInvoiceDownloadQuery(Guid InvoiceId) : IQuery<InvoiceDownloadResponse>;

/// <summary>
/// Lists an order's invoices.
/// </summary>
/// <remarks>
/// The scope is the order's, not the invoice's: a shopper sees the invoices on their own order, a
/// seller sees their own — the vendor query filter on <c>invoices</c> takes care of the second —
/// and platform staff see all of them.
/// </remarks>
/// <param name="context">The Ordering data context.</param>
/// <param name="scope">Who is asking.</param>
internal sealed class ListOrderInvoicesQueryHandler(OrdersDbContext context, OrdersScope scope)
    : IQueryHandler<ListOrderInvoicesQuery, IReadOnlyList<InvoiceResponse>>
{
    public async Task<Result<IReadOnlyList<InvoiceResponse>>> HandleAsync(
        ListOrderInvoicesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var visible = await IsVisibleAsync(context, scope, query.OrderId, cancellationToken).ConfigureAwait(false);

        if (!visible)
        {
            return OrdersErrors.NotFound("order");
        }

        var rows = await context.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.OrderId == query.OrderId)
            .OrderBy(invoice => invoice.InvoiceNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<InvoiceResponse> invoices = [.. rows.Select(OrderProjection.ToResponse)];

        return Result.Success(invoices);
    }

    /// <summary>Whether this caller may see an order's documents at all.</summary>
    internal static async Task<bool> IsVisibleAsync(
        OrdersDbContext context,
        OrdersScope scope,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        if (scope.IsVendor)
        {
            // The vendor filter answers it: a seller with no sub-order on this order sees none.
            return await context.SubOrders
                .AsNoTracking()
                .AnyAsync(subOrder => subOrder.OrderId == orderId, cancellationToken)
                .ConfigureAwait(false);
        }

        var customerId = await context.Orders
            .AsNoTracking()
            .Where(order => order.Id == orderId)
            .Select(order => (Guid?)order.CustomerId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (customerId is null)
        {
            return false;
        }

        // Platform staff reach this through a permissioned route, so the only remaining question is
        // whether a shopper is asking about somebody else's order.
        return scope.Actor != Domain.OrderActor.Customer || customerId == scope.CustomerId;
    }
}

/// <summary>
/// Mints a short-lived link to an invoice PDF.
/// </summary>
/// <remarks>
/// The URL carries no authorisation of its own beyond its expiry, so minting one <em>is</em> the
/// grant — which is why the visibility check happens here, against the invoice's order, rather than
/// being left to the storage layer.
/// </remarks>
/// <param name="context">The Ordering data context.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="media">Signs the URL.</param>
internal sealed class GetInvoiceDownloadQueryHandler(
    OrdersDbContext context,
    OrdersScope scope,
    IMediaLibrary media) : IQueryHandler<GetInvoiceDownloadQuery, InvoiceDownloadResponse>
{
    public async Task<Result<InvoiceDownloadResponse>> HandleAsync(
        GetInvoiceDownloadQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var invoice = await context.Invoices
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == query.InvoiceId, cancellationToken)
            .ConfigureAwait(false);

        if (invoice is null)
        {
            return OrdersErrors.NotFound("invoice");
        }

        var visible = await ListOrderInvoicesQueryHandler
            .IsVisibleAsync(context, scope, invoice.OrderId, cancellationToken)
            .ConfigureAwait(false);

        if (!visible)
        {
            return OrdersErrors.NotFound("invoice");
        }

        if (invoice.FileId is not { } fileId)
        {
            return OrdersErrors.InvoiceFileMissing;
        }

        var url = await media.GetSignedUrlAsync(fileId, cancellationToken).ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(url)
            ? OrdersErrors.InvoiceFileMissing
            : Result.Success(new InvoiceDownloadResponse(invoice.Id, invoice.InvoiceNumber, url));
    }
}
