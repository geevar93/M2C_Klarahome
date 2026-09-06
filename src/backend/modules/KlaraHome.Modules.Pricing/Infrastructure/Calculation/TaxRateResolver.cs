using KlaraHome.Modules.Pricing.Domain;
using KlaraHome.Modules.Pricing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Pricing.Infrastructure.Calculation;

/// <summary>The levies in force on one HSN code on one date.</summary>
/// <param name="Rate">The GST percentage.</param>
/// <param name="CessRate">The compensation cess percentage.</param>
/// <param name="Source">The tax-rate row that decided it, or null when the product's own rate stood.</param>
internal readonly record struct ResolvedTax(decimal Rate, decimal CessRate, Guid? Source);

/// <summary>
/// Resolves a GST rate from an HSN code, as of a date (docs/02-domain-model.md §7).
/// </summary>
/// <remarks>
/// <para>
/// Always "as of" a date, never "now". An invoice reprinted next year has to show the rate that was
/// in force when the supply was made, and a resolver that reads the current row would silently
/// rewrite history every time the Council moved a rate.
/// </para>
/// <para>
/// A code with no row falls back to the rate on the product itself (docs/03-database-design.md
/// §4.4). That is deliberate: this table centralises the rates a store chooses to manage, and
/// making it a precondition for selling would mean a store could not list anything until somebody
/// had typed out the whole HSN schedule.
/// </para>
/// </remarks>
/// <param name="context">The Pricing data context.</param>
internal sealed class TaxRateResolver(PricingDbContext context)
{
    /// <summary>
    /// The levies for each of several HSN codes, keyed by code. A code with no row in force is
    /// absent, and the caller uses the product's own rate.
    /// </summary>
    /// <param name="hsnCodes">The codes to resolve.</param>
    /// <param name="asOf">The date of supply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyDictionary<string, ResolvedTax>> ResolveAsync(
        IReadOnlyCollection<string> hsnCodes,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hsnCodes);

        var codes = hsnCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(TaxRate.Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (codes.Count == 0)
        {
            return new Dictionary<string, ResolvedTax>(StringComparer.Ordinal);
        }

        var rows = await context.TaxRates
            .AsNoTracking()
            .Where(rate => codes.Contains(rate.HsnCode)
                           && rate.IsActive
                           && rate.EffectiveFrom <= asOf
                           && (rate.EffectiveTo == null || rate.EffectiveTo >= asOf))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Latest start date wins. Overlapping windows are prevented by nothing but an operator's
        // care, so the tie-break has to be deterministic rather than merely unlikely to be needed.
        return rows
            .GroupBy(rate => rate.HsnCode, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var winner = group
                        .OrderByDescending(rate => rate.EffectiveFrom)
                        .ThenBy(rate => rate.Id)
                        .First();

                    return new ResolvedTax(winner.Rate, winner.CessRate, winner.Id);
                },
                StringComparer.Ordinal);
    }

    /// <summary>The levies for one code, falling back to a product's own rate.</summary>
    /// <param name="resolved">What <see cref="ResolveAsync"/> returned.</param>
    /// <param name="hsnCode">The code on the product, or null.</param>
    /// <param name="productRate">The rate the product carries.</param>
    public static ResolvedTax For(
        IReadOnlyDictionary<string, ResolvedTax> resolved,
        string? hsnCode,
        decimal productRate)
    {
        ArgumentNullException.ThrowIfNull(resolved);

        return !string.IsNullOrWhiteSpace(hsnCode)
               && resolved.TryGetValue(TaxRate.Normalize(hsnCode), out var match)
            ? match
            : new ResolvedTax(Math.Max(0m, productRate), 0m, Source: null);
    }
}
