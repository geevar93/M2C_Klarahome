using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Shipping.Infrastructure.Rating;

/// <summary>What a parcel would cost on one service.</summary>
/// <param name="Zone">The zone that matched.</param>
/// <param name="Rate">The rule that priced it.</param>
/// <param name="Amount">What the customer pays, inclusive of tax.</param>
/// <param name="TaxAmount">The tax inside that figure.</param>
/// <param name="ChargeableWeightGrams">The weight it was priced on.</param>
internal sealed record RatedParcel(
    ShippingZone Zone,
    ShippingRate Rate,
    decimal Amount,
    decimal TaxAmount,
    int ChargeableWeightGrams);

/// <summary>
/// Chooses the zone and the rate rule for a parcel, and prices it.
/// </summary>
/// <remarks>
/// <para>
/// The one place a delivery charge is decided, and it is deliberately not the same place a courier's
/// price is read. What the customer pays comes from this rate card; what the platform pays comes from
/// the aggregator; and keeping them apart is what makes the margin on delivery a fact rather than an
/// assumption (docs/08-integrations.md §2).
/// </para>
/// <para>
/// Selection is three decisions in order, and each one is a tie-break for the last. The zone is the
/// active one with the lowest priority number that covers the destination. The rule is the seller's
/// own where they have one, and the platform's otherwise. Where two rules still match, the narrower
/// weight band wins — a specific rule beats a general one, which is the only tie-break that lets a
/// tariff be edited without fear of what else it silently changes.
/// </para>
/// <para>
/// The whole rate card is read and matched in memory. That is not laziness: a store has a handful of
/// zones and a few dozen rules, the match is a range test the index cannot help with, and the
/// alternative is a query per basket render on the hottest path the storefront has.
/// </para>
/// </remarks>
/// <param name="context">The Shipping data context.</param>
/// <param name="options">Supplies the volumetric divisor and the freight tax rate.</param>
internal sealed class RateResolver(ShippingDbContext context, IOptions<ShippingOptions> options)
{
    /// <summary>
    /// Prices a parcel on every service that can carry it, cheapest first.
    /// </summary>
    /// <remarks>
    /// An empty list means the rate card has no rule for this destination and weight — which is a
    /// hole in the tariff, not a courier refusing. The two are different problems and the caller
    /// reports them differently.
    /// </remarks>
    /// <param name="vendorId">The seller dispatching, whose override wins where they have one.</param>
    /// <param name="stateId">The destination's state.</param>
    /// <param name="pincode">The destination's six-digit PIN code.</param>
    /// <param name="chargeableWeightGrams">The weight the courier prices on.</param>
    /// <param name="orderValue">What the seller's part of the basket comes to.</param>
    /// <param name="isCod">Whether cash will be collected at the door.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<RatedParcel>> RateAsync(
        Guid vendorId,
        Guid? stateId,
        string pincode,
        int chargeableWeightGrams,
        decimal orderValue,
        bool isCod,
        CancellationToken cancellationToken = default)
    {
        var zone = await ZoneForAsync(stateId, pincode, cancellationToken).ConfigureAwait(false);

        if (zone is null)
        {
            return [];
        }

        var rules = await context.Rates
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(rate => rate.ZoneId == zone.Id
                           && rate.IsActive
                           && (rate.VendorId == null || rate.VendorId == vendorId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rated = new List<RatedParcel>();

        foreach (var method in Enum.GetValues<ShippingMethod>())
        {
            var chosen = Choose(rules, method, vendorId, chargeableWeightGrams, orderValue, isCod);

            if (chosen is null)
            {
                continue;
            }

            var amount = chosen.AmountFor(chargeableWeightGrams, orderValue, isCod);

            rated.Add(new RatedParcel(zone, chosen, amount, TaxInside(amount), chargeableWeightGrams));
        }

        return [.. rated.OrderBy(parcel => parcel.Amount).ThenBy(parcel => parcel.Rate.EtaMaxDays)];
    }

    /// <summary>The zone a destination falls in, or null when the map does not cover it.</summary>
    /// <param name="stateId">The destination's state.</param>
    /// <param name="pincode">The destination's six-digit PIN code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ShippingZone?> ZoneForAsync(
        Guid? stateId,
        string pincode,
        CancellationToken cancellationToken = default)
    {
        var zones = await context.Zones
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(zone => zone.IsActive)
            .OrderBy(zone => zone.Priority)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // The first match in priority order, and a catch-all only if nothing specific matched — so a
        // rest-of-India zone with a high priority number cannot swallow a metro one.
        return zones.FirstOrDefault(zone => !zone.IsCatchAll && zone.Covers(stateId, pincode))
               ?? zones.FirstOrDefault(zone => zone.IsCatchAll);
    }

    /// <summary>The weight a courier would price a parcel on, from its contents and its box.</summary>
    /// <param name="deadWeightGrams">What the contents weigh.</param>
    /// <param name="dimensions">The box, when it has been measured.</param>
    public int ChargeableWeight(int deadWeightGrams, ShipmentDimensions? dimensions)
        => Math.Max(
            Math.Max(0, deadWeightGrams),
            dimensions?.VolumetricGrams(options.Value.VolumetricDivisor) ?? 0);

    /// <summary>
    /// The tax inside a tax-inclusive delivery charge.
    /// </summary>
    /// <remarks>
    /// Back-calculated from the inclusive figure at the configured freight rate, which is what makes
    /// it consistent with every other price in this platform: the shopper is shown one number and the
    /// tax is taken out of it, never added to it. The split that reaches an invoice is the pricing
    /// engine's, computed from this same inclusive amount, so the two cannot disagree about a total.
    /// </remarks>
    /// <param name="amountInclusive">The delivery charge, inclusive of tax.</param>
    public decimal TaxInside(decimal amountInclusive)
    {
        var rate = options.Value.FreightGstRate;

        return rate <= 0m
            ? 0m
            : Math.Round(amountInclusive - (amountInclusive * 100m / (100m + rate)), 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The rule that prices this parcel on one service, or null when none does.
    /// </summary>
    /// <remarks>
    /// A seller's own rule beats the platform's outright, even when the platform's is cheaper — the
    /// override exists precisely because the seller negotiated something different, and letting price
    /// decide would make the override apply only when it did not matter.
    /// </remarks>
    private static ShippingRate? Choose(
        IReadOnlyList<ShippingRate> rules,
        ShippingMethod method,
        Guid vendorId,
        int chargeableWeightGrams,
        decimal orderValue,
        bool isCod)
    {
        var candidates = rules
            .Where(rate => rate.Method == method && rate.Applies(chargeableWeightGrams, orderValue))
            .Where(rate => !isCod || rate.IsCodAllowed)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var own = candidates.Where(rate => rate.VendorId == vendorId).ToList();
        var pool = own.Count > 0 ? own : candidates.Where(rate => rate.VendorId is null).ToList();

        return pool
            .OrderBy(rate => rate.WeightBandWidth)
            .ThenBy(rate => rate.BaseRate)
            .FirstOrDefault();
    }
}
