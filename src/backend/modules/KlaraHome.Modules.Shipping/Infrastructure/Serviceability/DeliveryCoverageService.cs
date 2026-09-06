using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Shipping;
using KlaraHome.Modules.Shipping.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Shipping.Infrastructure.Serviceability;

/// <summary>Where a PIN code turned out to be, and who said so.</summary>
/// <param name="City">The city, or null when nobody knows.</param>
/// <param name="State">The state, likewise.</param>
internal readonly record struct ResolvedPlace(string? City, string? State);

/// <summary>
/// Whether this store will deliver to a destination at all (ADR-018).
/// </summary>
/// <remarks>
/// <para>
/// The second of the two questions an address has to pass, and the one that belongs to the
/// shopkeeper rather than to a courier. <see cref="ServiceabilityService"/> answers "can a parcel
/// get there"; this answers "have we decided to sell there", and the two are kept apart because one
/// is a fact about India's logistics and the other is a decision an operator reverses in a settings
/// screen.
/// </para>
/// <para>
/// <b>There is no cache and no optimistic miss here.</b> That rule belongs to serviceability, where
/// guessing yes costs one apology; a coverage rule that guessed yes would sell to an address the
/// store has explicitly decided not to serve. The inputs are a settings row and a PIN code, both
/// always available, so there is nothing to be optimistic about.
/// </para>
/// <para>
/// A PIN code's city comes from <c>platform.pincodes</c> first — seeded, authoritative, and
/// unaffected by anybody's API having a bad day — and from what the courier last said about the PIN
/// code second. A destination whose city nobody knows can still be covered by a prefix or by an
/// explicit PIN code, which is what keeps an unseeded deployment trading.
/// </para>
/// </remarks>
/// <param name="settings">Reads the operator's delivery-coverage policy.</param>
/// <param name="reference">The platform's own PIN-code reference data.</param>
/// <param name="context">The Shipping data context, for the courier's own answer about a place.</param>
internal sealed class DeliveryCoverageService(
    IStoreSettings settings,
    IReferenceData reference,
    ShippingDbContext context)
{
    /// <summary>
    /// Whether the store delivers to a PIN code, and what is known about the place.
    /// </summary>
    /// <param name="pincode">The six-digit destination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<(bool Covered, ResolvedPlace Place, string? Message)> EvaluateAsync(
        string pincode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pincode);

        var policy = await settings
            .GetAsync<DeliveryCoverageSettings>(cancellationToken)
            .ConfigureAwait(false);

        var place = await PlaceAsync(pincode, cancellationToken).ConfigureAwait(false);

        if (!policy.Enabled)
        {
            return (true, place, null);
        }

        return (Covers(policy, pincode, place.City), place, policy.Message);
    }

    /// <summary>
    /// Whether a policy admits a PIN code.
    /// </summary>
    /// <remarks>
    /// Pure, and separated from the reading of settings and reference data so the rule itself can be
    /// tested without either. The order is the rule: <b>a block beats everything</b>, and after that
    /// any one allow rule is enough — an explicit PIN code, a prefix, or the city's name.
    /// </remarks>
    /// <param name="policy">The operator's policy.</param>
    /// <param name="pincode">The six-digit destination.</param>
    /// <param name="city">The destination's city, where it is known.</param>
    public static bool Covers(DeliveryCoverageSettings policy, string pincode, string? city)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (!policy.Enabled)
        {
            return true;
        }

        var code = (pincode ?? string.Empty).Trim();

        if (policy.BlockedPincodes.Any(blocked => Same(blocked, code)))
        {
            return false;
        }

        if (policy.AllowedPincodes.Any(allowed => Same(allowed, code)))
        {
            return true;
        }

        if (policy.AllowedPincodePrefixes.Any(prefix =>
                !string.IsNullOrWhiteSpace(prefix)
                && code.StartsWith(prefix.Trim(), StringComparison.Ordinal)))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(city)
               && policy.AllowedCities.Any(allowed => Same(allowed, city));
    }

    /// <summary>The refusal a coverage answer and a serviceability answer add up to.</summary>
    /// <remarks>
    /// Coverage is reported first when both fail. It is the one the operator can do something about,
    /// and telling a shopper "no courier goes there" about an address the store had already decided
    /// not to serve would be true and misleading.
    /// </remarks>
    /// <param name="covered">Whether the store delivers there.</param>
    /// <param name="serviceable">Whether a courier will carry a parcel there.</param>
    /// <param name="codWanted">Whether the shopper wants to pay cash.</param>
    /// <param name="codAvailable">Whether cash can be collected there.</param>
    public static DeliveryRefusal RefusalFor(bool covered, bool serviceable, bool codWanted, bool codAvailable)
    {
        if (!covered)
        {
            return DeliveryRefusal.NotCovered;
        }

        if (!serviceable)
        {
            return DeliveryRefusal.NotServiceable;
        }

        return codWanted && !codAvailable ? DeliveryRefusal.CodUnavailable : DeliveryRefusal.None;
    }

    /// <summary>Where a PIN code is: the platform's own answer first, the courier's second.</summary>
    private async Task<ResolvedPlace> PlaceAsync(string pincode, CancellationToken cancellationToken)
    {
        var known = await reference.PincodeAsync(pincode, cancellationToken).ConfigureAwait(false);

        if (known is not null)
        {
            return new ResolvedPlace(known.City, known.StateName);
        }

        // What a courier last told us about the PIN code, from the serviceability cache. Only a
        // fallback: it is one carrier's opinion, recorded to make an unseeded deployment usable
        // rather than to compete with reference data.
        var cached = await context.Serviceability
            .AsNoTracking()
            .Where(entry => entry.Pincode == pincode && entry.City != null)
            .OrderByDescending(entry => entry.RefreshedAt)
            .Select(entry => new { entry.City, entry.State })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return cached is null ? default : new ResolvedPlace(cached.City, cached.State);
    }

    private static bool Same(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left)
           && !string.IsNullOrWhiteSpace(right)
           && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}
