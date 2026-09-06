using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure.Courier;
using KlaraHome.Modules.Shipping.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Shipping.Infrastructure.Serviceability;

/// <summary>What this platform can promise about a destination.</summary>
/// <param name="Pincode">The six-digit destination.</param>
/// <param name="IsServiceable">Whether anything can be delivered there.</param>
/// <param name="PrepaidOk">Whether a prepaid parcel can be.</param>
/// <param name="CodOk">Whether cash can be collected there.</param>
/// <param name="EtaDays">How long a courier says it takes, when one says.</param>
/// <param name="Courier">Whose answer this is.</param>
/// <param name="CheckedAt">When the answer was obtained. Null when nobody has ever asked.</param>
internal sealed record ServiceabilityAnswer(
    string Pincode,
    bool IsServiceable,
    bool PrepaidOk,
    bool CodOk,
    int? EtaDays,
    string? Courier,
    DateTimeOffset? CheckedAt);

/// <summary>
/// Reads and refreshes the serviceability cache (docs/08-integrations.md §2).
/// </summary>
/// <remarks>
/// <para>
/// The rule this class exists to enforce: <b>the storefront never calls an aggregator</b>. A product
/// page and a checkout both ask "can you deliver here" and both must answer in milliseconds from a
/// table. A live call on that path would make every page view depend on somebody else's uptime, and
/// the first courier outage would take the shop down with it.
/// </para>
/// <para>
/// A miss is answered optimistically and refreshed in the background. Telling a shopper "we cannot
/// deliver there" because nobody has asked the courier yet would lose the order outright; telling
/// them we can, and finding at checkout that we cannot, costs one apology. The nightly job is what
/// makes the second case rare.
/// </para>
/// <para>
/// A stale row is used and refreshed, not discarded. Yesterday's answer is very nearly always
/// today's, and the alternative — blocking a shopper's page render on an API call — is the thing
/// this cache exists to prevent.
/// </para>
/// </remarks>
/// <param name="context">The Shipping data context.</param>
/// <param name="providers">The adapters, for a refresh.</param>
/// <param name="options">Supplies the freshness window.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what a refresh learned.</param>
internal sealed partial class ServiceabilityService(
    ShippingDbContext context,
    ShippingProviderRegistry providers,
    IOptions<ShippingOptions> options,
    IClock clock,
    ILogger<ServiceabilityService> logger)
{
    /// <summary>
    /// What can be delivered to a PIN code, from the cache alone.
    /// </summary>
    /// <param name="pincode">The six-digit destination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ServiceabilityAnswer> ReadAsync(string pincode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pincode);

        var rows = await context.Serviceability
            .AsNoTracking()
            .Where(entry => entry.Pincode == pincode)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            // Nobody has ever asked. Optimistic on purpose: the alternative is refusing an order for
            // a destination that is very probably fine, and the checkout still validates against the
            // rate card and the seller's own serviceability before anybody pays.
            return new ServiceabilityAnswer(pincode, true, true, true, null, null, null);
        }

        // The best of what any courier offers, because that is what the destination can actually
        // have: one courier refusing cash does not make the PIN code prepaid-only.
        var freshest = rows.MaxBy(entry => entry.RefreshedAt)!;

        return new ServiceabilityAnswer(
            pincode,
            rows.Any(entry => entry.PrepaidOk || entry.CodOk),
            rows.Any(entry => entry.PrepaidOk),
            rows.Any(entry => entry.CodOk),
            rows.Where(entry => entry.EtaDays is > 0).Select(entry => entry.EtaDays).DefaultIfEmpty(null).Min(),
            freshest.Courier,
            freshest.RefreshedAt);
    }

    /// <summary>
    /// Asks a courier about a PIN code and records what they said.
    /// </summary>
    /// <remarks>
    /// The only place an aggregator is asked about serviceability, and it is never on a request path.
    /// It returns the answer it wrote so a caller that asked for a refresh does not have to read it
    /// back.
    /// </remarks>
    /// <param name="pincode">The six-digit destination.</param>
    /// <param name="pickupPincode">Where a parcel would leave from, when the caller knows.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ServiceabilityAnswer> RefreshAsync(
        string pincode,
        string? pickupPincode = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pincode);

        var provider = providers.Default;

        if (!providers.HasAggregator)
        {
            // Nothing to ask. The manual adapter answers yes to everything, and writing that into the
            // cache would turn "we do not know" into "we checked" — which is exactly the confusion a
            // refreshed-at column exists to prevent.
            return await ReadAsync(pincode, cancellationToken).ConfigureAwait(false);
        }

        var asked = await provider
            .CheckServiceabilityAsync(pincode, pickupPincode, weightGrams: 500, isCod: true, cancellationToken)
            .ConfigureAwait(false);

        if (asked.IsFailure)
        {
            RefreshFailed(logger, pincode, asked.Error.Message);

            return await ReadAsync(pincode, cancellationToken).ConfigureAwait(false);
        }

        var answer = asked.Value;
        var now = clock.UtcNow;

        var row = await context.Serviceability
            .FirstOrDefaultAsync(
                entry => entry.Pincode == pincode && entry.Courier == answer.Courier,
                cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            row = ServiceabilityEntry.Create(pincode, answer.Courier, now);
            context.Serviceability.Add(row);
        }

        row.Record(answer.PrepaidOk, answer.CodOk, answer.PickupOk, answer.EtaDays, answer.MaxWeightGrams, now);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new ServiceabilityAnswer(
            pincode,
            answer.PrepaidOk || answer.CodOk,
            answer.PrepaidOk,
            answer.CodOk,
            answer.EtaDays,
            answer.Courier,
            now);
    }

    /// <summary>
    /// The PIN codes whose answers are oldest, for the nightly refresh to work through.
    /// </summary>
    /// <remarks>
    /// Oldest first, so a job that is interrupted resumes where it stopped rather than starting the
    /// alphabet again. Only PIN codes somebody has already asked about are refreshed — the cache
    /// covers where this store actually delivers, not the whole of India's directory.
    /// </remarks>
    /// <param name="batchSize">How many to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<string>> StalestAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var cutoff = clock.UtcNow.AddHours(-options.Value.ServiceabilityTtlHours);

        return await context.Serviceability
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(entry => entry.RefreshedAt < cutoff)
            .OrderBy(entry => entry.RefreshedAt)
            .Select(entry => entry.Pincode)
            .Distinct()
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 1730, Level = LogLevel.Warning,
        Message = "Serviceability for {Pincode} could not be refreshed: {Detail}. The cached answer stands.")]
    private static partial void RefreshFailed(ILogger logger, string pincode, string detail);
}
