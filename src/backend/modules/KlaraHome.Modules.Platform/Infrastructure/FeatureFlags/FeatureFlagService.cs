using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Platform.Domain;
using KlaraHome.Modules.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace KlaraHome.Modules.Platform.Infrastructure.FeatureFlags;

/// <summary>One flag as the evaluator needs it, detached from the change tracker.</summary>
/// <param name="Key">The flag key.</param>
/// <param name="Enabled">The master switch.</param>
/// <param name="Rollout">Who the flag reaches while it is enabled.</param>
/// <param name="Description">What it controls.</param>
internal sealed record FeatureFlagSnapshot(string Key, bool Enabled, FeatureRollout Rollout, string Description);

/// <summary>
/// Evaluates feature flags against the flags table, with the whole set cached per tenant.
/// </summary>
/// <remarks>
/// <para>
/// The cache holds every flag rather than one entry per key. A page render asks about several
/// flags, the table is a few dozen rows, and one cached list answers all of them without a query
/// per question.
/// </para>
/// <para>
/// Snapshots are cached, not entities. A tracked entity handed to a process-wide cache would
/// outlive the scoped <c>DbContext</c> that loaded it, which is how a stale change tracker ends up
/// being shared between requests.
/// </para>
/// </remarks>
/// <param name="context">The Platform module's context.</param>
/// <param name="cache">The shared in-process cache.</param>
/// <param name="tenant">The ambient tenant; the cache key is scoped by it.</param>
internal sealed class FeatureFlagService(
    PlatformDbContext context,
    IMemoryCache cache,
    ITenantContext tenant) : IFeatureFlags
{
    /// <summary>
    /// How long the flag set survives without being invalidated. Deliberately shorter than the
    /// settings cache: a flag is thrown to turn something off in a hurry, and a minute is the most
    /// an operator should have to wait for a second replica to agree.
    /// </summary>
    internal static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    public async ValueTask<bool> IsEnabledAsync(
        string key,
        FeatureAudience audience = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var flags = await LoadAsync(cancellationToken).ConfigureAwait(false);

        // An unknown key is off. A typo then hides a feature rather than exposing an unfinished one.
        return flags.TryGetValue(key, out var flag)
               && flag.Enabled
               && flag.Rollout.Includes(flag.Key, audience.UserId, audience.Segment);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<string, bool>> GetAllAsync(
        FeatureAudience audience = default,
        CancellationToken cancellationToken = default)
    {
        var flags = await LoadAsync(cancellationToken).ConfigureAwait(false);

        return flags.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Enabled
                     && entry.Value.Rollout.Includes(entry.Key, audience.UserId, audience.Segment),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Every declared flag with its configuration, for the admin surface.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<FeatureFlagSnapshot>> ListAsync(CancellationToken cancellationToken)
    {
        var flags = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return [.. flags.Values.OrderBy(flag => flag.Key, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Applies a change to one flag and returns what it looked like before, so the caller can audit
    /// the change with a real before/after pair.
    /// </summary>
    /// <param name="key">The flag key. Must already be declared.</param>
    /// <param name="enabled">The new master switch value.</param>
    /// <param name="rollout">The new rollout.</param>
    /// <param name="description">The new description, or null to leave it as it is.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The flag as it was before the change, or null when no such flag is declared.</returns>
    public async Task<FeatureFlagSnapshot?> ConfigureAsync(
        string key,
        bool enabled,
        FeatureRollout rollout,
        string? description,
        CancellationToken cancellationToken)
    {
        var flag = await context.FeatureFlags
            .FirstOrDefaultAsync(candidate => candidate.Key == key, cancellationToken)
            .ConfigureAwait(false);

        if (flag is null)
        {
            return null;
        }

        var before = Snapshot(flag);

        flag.Configure(enabled, rollout);

        if (description is not null)
        {
            flag.Describe(description);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Invalidate();
        return before;
    }

    /// <summary>Drops the cached flag set, so the next evaluation comes from the table.</summary>
    public void Invalidate() => cache.Remove(CacheKeyFor(tenant.TenantId));

    private async ValueTask<IReadOnlyDictionary<string, FeatureFlagSnapshot>> LoadAsync(
        CancellationToken cancellationToken)
    {
        var cacheKey = CacheKeyFor(tenant.TenantId);

        if (cache.TryGetValue(cacheKey, out IReadOnlyDictionary<string, FeatureFlagSnapshot>? cached)
            && cached is not null)
        {
            return cached;
        }

        var flags = await context.FeatureFlags
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyDictionary<string, FeatureFlagSnapshot> map = flags
            .Select(Snapshot)
            .ToDictionary(flag => flag.Key, StringComparer.OrdinalIgnoreCase);

        cache.Set(cacheKey, map, CacheDuration);
        return map;
    }

    private static FeatureFlagSnapshot Snapshot(FeatureFlag flag)
        => new(flag.Key, flag.Enabled, flag.Rollout, flag.Description);

    private static string CacheKeyFor(Guid tenantId) => $"platform:feature-flags:{tenantId}";
}
