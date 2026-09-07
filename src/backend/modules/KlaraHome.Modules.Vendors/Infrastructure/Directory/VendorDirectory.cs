using KlaraHome.Contracts.Vendors;
using KlaraHome.Modules.Vendors.Domain;
using KlaraHome.Modules.Vendors.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Vendors.Infrastructure.Directory;

/// <summary>
/// Answers <see cref="IVendorDirectory"/> from this module's own tables.
/// </summary>
/// <remarks>
/// <para>
/// Not cached. Every question this answers is asked inside a request that is already hitting the
/// database, the row is tiny and indexed by primary key, and the one thing a stale answer would get
/// wrong is whether a suspended seller may still sell — which is precisely the answer that must not
/// be a second old.
/// </para>
/// <para>
/// It reads with the vendor query filter in force. That is deliberate: a vendor user asking about
/// another seller through some future caller of this contract gets nothing, exactly as they would
/// from any other query in this module.
/// </para>
/// </remarks>
/// <param name="context">The Vendors data context.</param>
internal sealed class VendorDirectory(VendorsDbContext context) : IVendorDirectory
{
    /// <inheritdoc />
    public async ValueTask<bool> IsActiveAsync(Guid vendorId, CancellationToken cancellationToken = default)
        => await context.Vendors
            .AsNoTracking()
            .AnyAsync(
                vendor => vendor.Id == vendorId && vendor.Status == VendorStatus.Active,
                cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<VendorSummary?> FindAsync(Guid vendorId, CancellationToken cancellationToken = default)
        => await context.Vendors
            .AsNoTracking()
            .Where(vendor => vendor.Id == vendorId)
            .Select(Projection)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<VendorSummary?> FindByCodeAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var normalised = code.Trim();

        return await context.Vendors
            .AsNoTracking()
            .Where(vendor => EF.Functions.ILike(vendor.Code, normalised))
            .Select(Projection)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<Guid, VendorSummary>> FindManyAsync(
        IReadOnlyCollection<Guid> vendorIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vendorIds);

        if (vendorIds.Count == 0)
        {
            return new Dictionary<Guid, VendorSummary>();
        }

        // One query for the whole set. This contract exists largely so a listing page can name the
        // seller of each of fifty results, and doing that one id at a time is the N+1 this method
        // is here to prevent.
        var ids = vendorIds.Distinct().ToArray();

        var summaries = await context.Vendors
            .AsNoTracking()
            .Where(vendor => ids.Contains(vendor.Id))
            .Select(Projection)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return summaries.ToDictionary(summary => summary.Id);
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsServiceableAsync(
        Guid vendorId,
        Guid stateId,
        string pincode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pincode);

        var servesAllIndia = await context.Vendors
            .AsNoTracking()
            .Where(vendor => vendor.Id == vendorId)
            .Select(vendor => (bool?)vendor.ServesAllIndia)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // No such seller is not "serviceable everywhere". The caller has already asked whether the
        // seller is active; this method must not turn an unknown id into a yes.
        if (servesAllIndia is not { } servesEverywhere)
        {
            return false;
        }

        if (servesEverywhere)
        {
            return true;
        }

        // The rule set is small — a seller has a handful of regions, not thousands — so it is read
        // once and matched in memory. The alternative is a prefix comparison expressed in SQL for
        // every line of every cart render, which is a query the index cannot help with anyway.
        var regions = await context.ServiceableRegions
            .AsNoTracking()
            .Where(region => region.VendorId == vendorId)
            .Select(region => new { region.Scope, region.StateId, region.PincodePrefix, region.IsExcluded })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var trimmed = pincode.Trim();
        var matched = false;

        foreach (var region in regions)
        {
            var hit = region.Scope == ServiceableRegionScope.State
                ? region.StateId == stateId
                : region.PincodePrefix is { Length: > 0 } prefix
                  && trimmed.StartsWith(prefix, StringComparison.Ordinal);

            if (!hit)
            {
                continue;
            }

            // An exclusion is decisive wherever it matches. Returning immediately is what makes
            // "everywhere in this state except these PIN codes" mean what it reads as, whatever
            // order the rows come back in.
            if (region.IsExcluded)
            {
                return false;
            }

            matched = true;
        }

        return matched;
    }

    /// <summary>
    /// An expression rather than a method, so the provider translates it and the projection happens
    /// in SQL — the columns nobody asked for, the registered address and the return policy JSON,
    /// are never read off the disk at all.
    /// </summary>
    /// <summary>
    /// What the seller promises about goods coming back (Step 17).
    /// </summary>
    /// <remarks>
    /// Read from the seller's own <c>return_policy</c> document rather than from a settings row,
    /// because it is a commitment they made and one they can be held to. A seller who has none
    /// answers null and the caller falls back to the store's default — which is different from a
    /// seller who has one saying zero days, and that difference is why this is nullable rather than
    /// a defaulted record.
    /// </remarks>
    public async ValueTask<VendorReturnPolicy?> ReturnPolicyAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default)
    {
        var policy = await context.Vendors
            .AsNoTracking()
            .Where(vendor => vendor.Id == vendorId)
            .Select(vendor => vendor.ReturnPolicy)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return policy is null
            ? null
            : new VendorReturnPolicy(
                vendorId,
                policy.AcceptsReturns,
                policy.WindowDays,
                policy.AcceptsExchanges,
                policy.CustomerPaysReturnShipping,
                policy.Notes);
    }

    private static System.Linq.Expressions.Expression<Func<Vendor, VendorSummary>> Projection { get; } =
        vendor => new VendorSummary(
            vendor.Id,
            vendor.Code,
            vendor.DisplayName,
            vendor.Slug,
            vendor.Status == VendorStatus.Active,
            vendor.DispatchSlaHours,
            vendor.Gstin,
            vendor.Rating);
}
