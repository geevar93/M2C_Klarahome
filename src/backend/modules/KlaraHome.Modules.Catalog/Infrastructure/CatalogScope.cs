using System.Globalization;
using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Catalog.Infrastructure;

/// <summary>
/// Answers "may this caller write to this row", and mints SKUs.
/// </summary>
/// <remarks>
/// <para>
/// Reads are already handled: products, listings and moderation rows are
/// <see cref="KlaraHome.SharedKernel.Domain.IVendorScoped"/>, so the global query filter shows a
/// vendor caller their own rows and the platform's shared ones and nobody else's. Writes need one
/// more rule that a query filter cannot express — a seller may edit their own product but must not
/// edit a <em>platform-owned</em> one, even though they can see it — and that rule lives here,
/// once, rather than in each of the handlers that takes an id from a route.
/// </para>
/// <para>
/// Taxonomy is deliberately not scoped. Categories, brands and attributes are the platform's, and
/// the permission to change them is held only by platform staff.
/// </para>
/// </remarks>
/// <param name="context">The Catalog data context.</param>
/// <param name="caller">The signed-in caller, for their vendor scope.</param>
/// <param name="options">Supplies the SKU prefix.</param>
internal sealed class CatalogScope(
    CatalogDbContext context,
    ICallerContext caller,
    IOptions<CatalogOptions> options)
{
    /// <summary>Whether the caller is confined to one seller.</summary>
    public bool IsVendorCaller => caller.VendorId is not null;

    /// <summary>The seller the caller is confined to, or null for platform staff.</summary>
    public Guid? CallerVendorId => caller.VendorId;

    /// <summary>
    /// Whether the caller may write to a row owned by <paramref name="ownerVendorId"/>.
    /// </summary>
    /// <remarks>
    /// Platform staff may write to anything. A vendor caller may write only to their own: a
    /// platform-owned row is readable by every seller — that is what makes the catalogue shared —
    /// and writable by none of them.
    /// </remarks>
    /// <param name="ownerVendorId">The row's owning seller, or null for a platform-owned row.</param>
    public bool CanWrite(Guid? ownerVendorId)
        => caller.VendorId is not { } scoped || ownerVendorId == scoped;

    /// <summary>
    /// The seller a newly created row belongs to: the caller's own, or the one platform staff
    /// named.
    /// </summary>
    /// <param name="requested">The seller named in the request, if any.</param>
    public Guid? OwnerFor(Guid? requested) => caller.VendorId ?? requested;

    /// <summary>
    /// Takes the next SKU from the sequence — <c>SKU-000017</c>.
    /// </summary>
    /// <remarks>
    /// Only for a variant whose creator did not supply one. A merchandiser importing a real
    /// catalogue brings their own SKUs, and overwriting them would break every reference their
    /// warehouse already holds.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<string> NextSkuAsync(CancellationToken cancellationToken)
    {
        var sequence = $"{CatalogModule.SchemaName}.{CatalogDbContext.SkuSequenceName}";

        // EF1002 warns that SqlQueryRaw concatenates rather than parameterises. It is right in
        // general and does not apply here: a sequence name cannot be a parameter in any dialect,
        // and this one is built from two compile-time constants in this assembly with nothing
        // caller-supplied anywhere near it. Suppressed narrowly, at the one call site.
#pragma warning disable EF1002
        var next = await context.Database
            .SqlQueryRaw<long>($"SELECT nextval('{sequence}') AS \"Value\"")
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
#pragma warning restore EF1002

        return string.Create(CultureInfo.InvariantCulture, $"{options.Value.SkuPrefix}-{next:D6}");
    }
}
