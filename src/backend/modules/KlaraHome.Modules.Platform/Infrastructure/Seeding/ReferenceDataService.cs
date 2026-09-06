using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace KlaraHome.Modules.Platform.Infrastructure.Seeding;

/// <summary>
/// Serves the jurisdiction list to the modules that store a <c>state_id</c> but may not read the
/// <c>platform</c> schema.
/// </summary>
/// <remarks>
/// The whole table is loaded once and held in the process cache. There are 36 rows, they change
/// when Parliament creates a state, and the alternative is a database round trip on every address
/// a customer saves and every invoice line that resolves a place of supply.
/// </remarks>
/// <param name="context">The Platform module's context.</param>
/// <param name="cache">The process cache.</param>
internal sealed class ReferenceDataService(PlatformDbContext context, IMemoryCache cache) : IReferenceData
{
    private const string CacheKey = "platform.reference.states";

    /// <inheritdoc />
    public async ValueTask<bool> StateExistsAsync(Guid stateId, CancellationToken cancellationToken = default)
    {
        var states = await StatesAsync(cancellationToken).ConfigureAwait(false);
        return states.ContainsKey(stateId);
    }

    /// <inheritdoc />
    public async ValueTask<string?> StateCodeAsync(Guid stateId, CancellationToken cancellationToken = default)
    {
        var states = await StatesAsync(cancellationToken).ConfigureAwait(false);
        return states.GetValueOrDefault(stateId);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Queried rather than cached wholesale. The states are 36 rows; the PIN codes are roughly
    /// nineteen thousand, and holding them all in every process to answer one lookup at a time would
    /// trade a millisecond for tens of megabytes. The unique index on <c>code</c> makes this a point
    /// read, and the delivery-coverage check that calls it is not on a hot path — the storefront
    /// answer it feeds is itself cached.
    /// </remarks>
    public async ValueTask<PincodeInfo?> PincodeAsync(
        string pincode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pincode))
        {
            return null;
        }

        var code = pincode.Trim();

        var found = await context.Pincodes
            .AsNoTracking()
            .Where(entry => entry.Code == code)
            .Join(
                context.States.AsNoTracking(),
                entry => entry.StateId,
                state => state.Id,
                (entry, state) => new PincodeInfo(
                    entry.Code,
                    entry.City,
                    entry.District,
                    state.Id,
                    state.Name,
                    state.Code))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return found;
    }

    private async ValueTask<IReadOnlyDictionary<Guid, string>> StatesAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyDictionary<Guid, string>? cached) && cached is not null)
        {
            return cached;
        }

        var states = await context.States
            .AsNoTracking()
            .ToDictionaryAsync(state => state.Id, state => state.Code, cancellationToken)
            .ConfigureAwait(false);

        cache.Set(CacheKey, (IReadOnlyDictionary<Guid, string>)states, TimeSpan.FromHours(12));
        return states;
    }
}
