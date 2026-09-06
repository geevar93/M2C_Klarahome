using KlaraHome.Contracts.Vendors;
using KlaraHome.Modules.Vendors.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Vendors.Infrastructure.Directory;

/// <summary>
/// Answers <see cref="IVendorPickupPoints"/> from this module's own tables.
/// </summary>
/// <remarks>
/// <para>
/// The read half is the same shape as <see cref="VendorDirectory"/>: uncached, indexed, and read
/// with the vendor query filter in force, so a seller asking through some future caller sees only
/// their own addresses.
/// </para>
/// <para>
/// The write half is one field, and it is the only write another module makes into this schema. A
/// courier's id for an address is a fact only the module that talks to couriers can learn, and the
/// alternative — Shipping keeping its own table of "which of our addresses is registered with whom"
/// — would be a second answer to a question this table already asks.
/// </para>
/// <para>
/// The write bypasses the vendor filter deliberately: registration happens on a background job that
/// has no caller, and a filtered update would silently touch nothing.
/// </para>
/// </remarks>
/// <param name="context">The Vendors data context.</param>
internal sealed class VendorPickupPoints(VendorsDbContext context) : IVendorPickupPoints
{
    /// <inheritdoc />
    public async Task<VendorPickupPoint?> FindAsync(
        Guid vendorId,
        Guid? pickupLocationId = null,
        CancellationToken cancellationToken = default)
    {
        var rows = context.PickupLocations
            .AsNoTracking()
            .Where(location => location.VendorId == vendorId && location.IsActive);

        rows = pickupLocationId is { } wanted
            ? rows.Where(location => location.Id == wanted)
            : rows.Where(location => location.IsDefault);

        var found = await rows
            .Select(Projection)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // A seller who never marked one as default still has somewhere to be collected from, and
        // failing the booking over that would be refusing to ship for a data-entry omission.
        if (found is null && pickupLocationId is null)
        {
            found = await context.PickupLocations
                .AsNoTracking()
                .Where(location => location.VendorId == vendorId && location.IsActive)
                .OrderBy(location => location.CreatedAt)
                .Select(Projection)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return found;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VendorPickupPoint>> ListAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default)
        => await context.PickupLocations
            .AsNoTracking()
            .Where(location => location.VendorId == vendorId && location.IsActive)
            .OrderByDescending(location => location.IsDefault)
            .ThenBy(location => location.Label)
            .Select(Projection)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<Result> RecordCourierLocationCodeAsync(
        Guid pickupLocationId,
        string courierLocationCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(courierLocationCode);

        var location = await context.PickupLocations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate => candidate.Id == pickupLocationId, cancellationToken)
            .ConfigureAwait(false);

        if (location is null)
        {
            return Result.Failure(VendorErrors.ChildNotFound("pickup location"));
        }

        location.LinkCourierLocation(courierLocationCode);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <summary>
    /// An expression rather than a method, so the projection happens in SQL and the columns nobody
    /// asked for are never read off the disk.
    /// </summary>
    private static System.Linq.Expressions.Expression<Func<Domain.VendorPickupLocation, VendorPickupPoint>>
        Projection
    { get; } =
        location => new VendorPickupPoint(
            location.Id,
            location.VendorId!.Value,
            location.Label,
            location.ContactName,
            location.ContactPhone,
            location.Line1,
            location.Line2,
            location.Landmark,
            location.City,
            location.StateId,
            location.Pincode,
            location.IsDefault,
            location.CourierLocationCode);
}
