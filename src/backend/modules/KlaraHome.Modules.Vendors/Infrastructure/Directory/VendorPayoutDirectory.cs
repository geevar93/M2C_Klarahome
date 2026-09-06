using KlaraHome.Contracts.Vendors;
using KlaraHome.Modules.Vendors.Domain;
using KlaraHome.Modules.Vendors.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Vendors.Infrastructure.Directory;

/// <summary>
/// Answers <see cref="IVendorPayouts"/> from this module's own tables.
/// </summary>
/// <remarks>
/// <para>
/// Added at Step 18. It joins the seller to their primary <em>verified</em> bank account, and the
/// word verified is the whole check: an account somebody typed in and nobody confirmed is not a
/// destination, and a payout run that treated it as one would send money to a typo.
/// </para>
/// <para>
/// No account number leaves this module. The stored number is encrypted and stays encrypted; what
/// crosses the boundary is the last four digits, the IFSC and the gateway's own id for the account —
/// enough for a statement line and a failed-transfer investigation, and nothing anybody could pay
/// into.
/// </para>
/// <para>
/// It reads with the vendor query filter in force, like the directory beside it. In practice the
/// caller is a background settlement run with no vendor scope, so the filter is a no-op; leaving it
/// on means a future vendor-scoped caller gets their own seller rather than everybody's.
/// </para>
/// </remarks>
/// <param name="context">The Vendors data context.</param>
internal sealed class VendorPayoutDirectory(VendorsDbContext context) : IVendorPayouts
{
    /// <inheritdoc />
    public async ValueTask<VendorPayoutProfile?> FindAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default)
    {
        var profiles = await ReadAsync([vendorId], cancellationToken).ConfigureAwait(false);
        return profiles.GetValueOrDefault(vendorId);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<Guid, VendorPayoutProfile>> FindManyAsync(
        IReadOnlyCollection<Guid> vendorIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vendorIds);

        return vendorIds.Count == 0
            ? new Dictionary<Guid, VendorPayoutProfile>()
            : await ReadAsync([.. vendorIds.Distinct()], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads several sellers and their primary verified account in two queries.
    /// </summary>
    /// <remarks>
    /// Two rather than one join, because a seller may have several accounts and only one of them is
    /// primary and verified — expressing that as a correlated subquery inside a projection produces a
    /// worse plan than reading the small set separately and matching in memory.
    /// </remarks>
    private async Task<Dictionary<Guid, VendorPayoutProfile>> ReadAsync(
        Guid[] vendorIds,
        CancellationToken cancellationToken)
    {
        var vendors = await context.Vendors
            .AsNoTracking()
            .Where(vendor => vendorIds.Contains(vendor.Id))
            .Select(vendor => new
            {
                vendor.Id,
                vendor.Code,
                vendor.LegalName,
                vendor.DisplayName,
                vendor.Gstin,
                vendor.Pan,
                vendor.GatewayAccountId,
                vendor.Status,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var accounts = await context.BankAccounts
            .AsNoTracking()
            .Where(account => account.VendorId != null
                              && vendorIds.Contains(account.VendorId.Value)
                              && account.IsPrimary
                              && account.VerificationStatus == BankVerificationStatus.Verified)
            .Select(account => new
            {
                account.VendorId,
                account.AccountNumberLast4,
                account.Ifsc,
                account.AccountName,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byVendor = accounts
            .Where(account => account.VendorId is not null)
            .GroupBy(account => account.VendorId!.Value)
            .ToDictionary(group => group.Key, group => group.First());

        return vendors.ToDictionary(
            vendor => vendor.Id,
            vendor =>
            {
                var account = byVendor.GetValueOrDefault(vendor.Id);

                return new VendorPayoutProfile(
                    vendor.Id,
                    vendor.Code,
                    vendor.LegalName,
                    vendor.DisplayName,
                    vendor.Gstin,
                    vendor.Pan,
                    vendor.GatewayAccountId,
                    account is not null,
                    account?.AccountNumberLast4,
                    account?.Ifsc,
                    account?.AccountName,
                    vendor.Status == VendorStatus.Active);
            });
    }
}
