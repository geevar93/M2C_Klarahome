using System.Text.Json;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Platform.Domain;
using KlaraHome.Modules.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace KlaraHome.Modules.Platform.Infrastructure.Settings;

/// <summary>
/// Reads and writes the store's configuration, with a per-tenant cache in front of the table.
/// </summary>
/// <remarks>
/// <para>
/// Settings are read on nearly every request — branding for the header, commerce rules for the
/// cart — and written a handful of times a year. The cache is therefore not an optimisation to
/// revisit later; without it the storefront would issue a settings query per page render.
/// </para>
/// <para>
/// The cache is in-process and invalidated by the writer. That is exact for the single-replica
/// deployment this product ships as, and becomes eventually-consistent-within-the-TTL the moment
/// there are two API replicas — which is why there is a TTL at all, rather than a cache that never
/// expires. Redis-backed <c>HybridCache</c> is the deferred fix (docs/01-architecture.md §4.3).
/// </para>
/// </remarks>
/// <param name="context">The Platform module's context.</param>
/// <param name="cache">The shared in-process cache.</param>
/// <param name="tenant">The ambient tenant; every cache key is scoped by it.</param>
internal sealed class StoreSettingsService(
    PlatformDbContext context,
    IMemoryCache cache,
    ITenantContext tenant) : IStoreSettings
{
    /// <summary>
    /// How long a cached section survives without being invalidated. Short enough that a second
    /// replica converges within a page refresh or two, long enough to keep the table out of the
    /// request path.
    /// </summary>
    internal static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    public async ValueTask<TSection> GetAsync<TSection>(CancellationToken cancellationToken = default)
        where TSection : class, ISettingsSection<TSection>, new()
    {
        var cacheKey = CacheKeyFor(tenant.TenantId, TSection.SectionKey);

        if (cache.TryGetValue(cacheKey, out TSection? cached) && cached is not null)
        {
            return cached;
        }

        var document = await ReadDocumentAsync(TSection.SectionKey, cancellationToken).ConfigureAwait(false);

        var value = document is null
            ? new TSection()
            : JsonSerializer.Deserialize<TSection>(document, SettingsJson.Storage) ?? new TSection();

        cache.Set(cacheKey, value, CacheDuration);
        return value;
    }

    /// <summary>
    /// The stored document for a section, or its defaults when it has never been saved. Used by the
    /// admin surface, which shows and edits the document rather than the typed object.
    /// </summary>
    /// <param name="descriptor">The section to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<string> GetDocumentAsync(
        ISettingsSectionDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var document = await ReadDocumentAsync(descriptor.Key, cancellationToken).ConfigureAwait(false);
        return document ?? descriptor.SerializeDefaults();
    }

    /// <summary>
    /// Replaces a section and returns what it held before, so the caller can write an audit entry
    /// with a real before/after pair rather than reconstructing one.
    /// </summary>
    /// <param name="descriptor">The section being replaced.</param>
    /// <param name="document">The new value, already parsed and re-serialised by the descriptor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document the section held before this call.</returns>
    public async Task<string> ReplaceAsync(
        ISettingsSectionDescriptor descriptor,
        string document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var existing = await context.StoreSettings
            .FirstOrDefaultAsync(setting => setting.Key == descriptor.Key, cancellationToken)
            .ConfigureAwait(false);

        string before;

        if (existing is null)
        {
            before = descriptor.SerializeDefaults();
            context.StoreSettings.Add(StoreSetting.Create(descriptor.Key, document, descriptor.IsPublic));
        }
        else
        {
            before = existing.Value;
            existing.Replace(document);
            existing.SetVisibility(descriptor.IsPublic);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Invalidate(descriptor.Key);
        return before;
    }

    /// <summary>Drops one section from the cache, so the next read comes from the table.</summary>
    /// <param name="sectionKey">The section key.</param>
    public void Invalidate(string sectionKey) => cache.Remove(CacheKeyFor(tenant.TenantId, sectionKey));

    private Task<string?> ReadDocumentAsync(string sectionKey, CancellationToken cancellationToken)
        => context.StoreSettings
            .AsNoTracking()
            .Where(setting => setting.Key == sectionKey)
            .Select(setting => setting.Value)
            .FirstOrDefaultAsync(cancellationToken);

    private static string CacheKeyFor(Guid tenantId, string sectionKey)
        => $"platform:settings:{tenantId}:{sectionKey}";
}
