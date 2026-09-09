using System.Globalization;
using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Inventory.Infrastructure;

/// <summary>
/// Answers "may this caller write to this row", and mints the document numbers.
/// </summary>
/// <remarks>
/// <para>
/// Reads are already handled: warehouses, stock items, suppliers and the three document types are
/// <see cref="KlaraHome.SharedKernel.Domain.IVendorScoped"/>, so the global query filter shows a
/// vendor caller their own rows and nobody else's. <see cref="Domain.Warehouse"/> alone also
/// declares <see cref="KlaraHome.SharedKernel.Domain.IPlatformShared"/>, which widens that read to
/// the platform's own locations — fulfilment by platform puts a seller's units on a platform shelf,
/// and a seller who could not see the shelf could not see the units. Everything else in this schema
/// stays private to its owner; the platform's supplier list in particular is nobody else's business.
/// </para>
/// <para>
/// Which leaves exactly one rule a query filter cannot express, and it lives here: a seller may open
/// stock, count, rename or buy into <em>their own</em> location and must not do any of it to a
/// platform one, even though they can see it. Once, rather than in each handler that takes an id
/// from a route.
/// </para>
/// <para>
/// The same arrangement as <c>CatalogScope</c>, and deliberately so: two modules answering the same
/// question two different ways is how a seller ends up able to do in one what they cannot do in the
/// other.
/// </para>
/// </remarks>
/// <param name="context">The Inventory data context.</param>
/// <param name="caller">The signed-in caller, for their vendor scope.</param>
/// <param name="options">Supplies the document-number prefixes.</param>
internal sealed class InventoryScope(
    InventoryDbContext context,
    ICallerContext caller,
    IOptions<InventoryOptions> options)
{
    /// <summary>Whether the caller is confined to one seller.</summary>
    public bool IsVendorCaller => caller.VendorId is not null;

    /// <summary>The seller the caller is confined to, or null for platform staff.</summary>
    public Guid? CallerVendorId => caller.VendorId;

    /// <summary>Whether the caller may write to a row owned by <paramref name="ownerVendorId"/>.</summary>
    /// <param name="ownerVendorId">The row's owning seller, or null for a platform-owned row.</param>
    public bool CanWrite(Guid? ownerVendorId)
        => caller.VendorId is not { } scoped || ownerVendorId == scoped;

    /// <summary>The seller a newly created row belongs to: the caller's own, or the one staff named.</summary>
    /// <param name="requested">The seller named in the request, if any.</param>
    public Guid? OwnerFor(Guid? requested) => caller.VendorId ?? requested;

    /// <summary>Takes the next purchase-order number — <c>PO-000017</c>.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<string> NextPurchaseOrderNumberAsync(CancellationToken cancellationToken)
        => NextAsync(
            InventoryDbContext.PurchaseOrderSequenceName,
            options.Value.PurchaseOrderPrefix,
            cancellationToken);

    /// <summary>Takes the next goods-receipt number — <c>GRN-000017</c>.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<string> NextGoodsReceiptNumberAsync(CancellationToken cancellationToken)
        => NextAsync(
            InventoryDbContext.GoodsReceiptSequenceName,
            options.Value.GoodsReceiptPrefix,
            cancellationToken);

    /// <summary>Takes the next stock-take number — <c>STK-000017</c>.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<string> NextStockTakeNumberAsync(CancellationToken cancellationToken)
        => NextAsync(
            InventoryDbContext.StockTakeSequenceName,
            options.Value.StockTakePrefix,
            cancellationToken);

    private async Task<string> NextAsync(string sequenceName, string prefix, CancellationToken cancellationToken)
    {
        var sequence = $"{InventoryModule.SchemaName}.{sequenceName}";

        // EF1002 warns that SqlQueryRaw concatenates rather than parameterises. It is right in
        // general and does not apply here: a sequence name cannot be a parameter in any dialect, and
        // this one is built from two compile-time constants in this assembly with nothing
        // caller-supplied anywhere near it. Suppressed narrowly, at the one call site.
#pragma warning disable EF1002
        var next = await context.Database
            .SqlQueryRaw<long>($"SELECT nextval('{sequence}') AS \"Value\"")
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
#pragma warning restore EF1002

        return string.Create(CultureInfo.InvariantCulture, $"{prefix}-{next:D6}");
    }
}
