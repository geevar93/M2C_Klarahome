using KlaraHome.Contracts.Identity;
using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Identity.Infrastructure.Directory;

/// <summary>
/// Answers <see cref="ICustomerDirectory"/> from this module's own tables.
/// </summary>
/// <remarks>
/// <para>
/// Not cached, for the same reason <c>VendorDirectory</c> is not: every question is asked inside a
/// request that is already hitting the database, and the one thing a stale answer would get wrong
/// is whether a disabled account may still check out.
/// </para>
/// <para>
/// The address reads are keyed on <em>(customer, address)</em> rather than on the address alone.
/// The caller passes both, so an id belonging to somebody else simply does not resolve — the
/// authorisation is in the shape of the query rather than in a check a future caller could forget.
/// </para>
/// </remarks>
/// <param name="context">The Identity data context.</param>
internal sealed class CustomerDirectory(IdentityDbContext context) : ICustomerDirectory
{
    /// <inheritdoc />
    public async ValueTask<CustomerSummary?> FindAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var user = await context.Users
            .AsNoTracking()
            .Where(candidate => candidate.Id == customerId && candidate.UserType == UserType.Customer)
            .Select(candidate => new { candidate.Id, candidate.Mobile, candidate.Email, candidate.Status })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return null;
        }

        // The profile is a separate row and may not exist yet — an account created by an OTP has a
        // number and nothing else. A missing profile is a customer with no name, not a missing
        // customer, so the two reads are separate and the second one is allowed to come back empty.
        var profile = await context.CustomerProfiles
            .AsNoTracking()
            .Where(candidate => candidate.UserId == customerId)
            .Select(candidate => new { candidate.FirstName, candidate.LastName, candidate.Gstin })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var name = string.Join(' ', new[] { profile?.FirstName, profile?.LastName }
                .Where(part => !string.IsNullOrWhiteSpace(part)))
            .Trim();

        return new CustomerSummary(
            user.Id,
            string.IsNullOrWhiteSpace(name) ? user.Mobile ?? user.Email ?? "Customer" : name,
            user.Email,
            user.Mobile,
            profile?.Gstin,
            user.Status == UserStatus.Active);
    }

    /// <inheritdoc />
    public async ValueTask<CustomerAddress?> FindAddressAsync(
        Guid customerId,
        Guid addressId,
        CancellationToken cancellationToken = default)
        => await context.Addresses
            .AsNoTracking()
            .Where(address => address.Id == addressId && address.UserId == customerId)
            .Select(Projection)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CustomerAddress>> ListAddressesAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
        => await context.Addresses
            .AsNoTracking()
            .Where(address => address.UserId == customerId)
            .OrderByDescending(address => address.IsDefaultShipping)
            .ThenByDescending(address => address.IsDefaultBilling)
            .ThenByDescending(address => address.Id)
            .Select(Projection)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// An expression rather than a method, so the provider translates it and the projection happens
    /// in SQL.
    /// </summary>
    private static System.Linq.Expressions.Expression<Func<Address, CustomerAddress>> Projection { get; } =
        address => new CustomerAddress(
            address.Id,
            address.Label,
            address.RecipientName,
            address.Mobile,
            address.Line1,
            address.Line2,
            address.Landmark,
            address.City,
            address.StateId,
            address.Pincode,
            address.Gstin,
            address.Type == AddressType.Office);
}
