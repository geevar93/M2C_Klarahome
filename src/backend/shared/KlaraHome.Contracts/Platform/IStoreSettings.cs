namespace KlaraHome.Contracts.Platform;

/// <summary>
/// Reads the store's configuration. The Platform module owns the storage; every other module
/// reads through this contract and never touches the <c>platform</c> schema
/// (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// Reads are served from a per-tenant cache and are cheap enough to call inside a request path.
/// A section is never null: an unconfigured deployment returns the section's own defaults.
/// </remarks>
public interface IStoreSettings
{
    /// <summary>The current value of one section, or its defaults if it has never been saved.</summary>
    /// <typeparam name="TSection">The section to read.</typeparam>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<TSection> GetAsync<TSection>(CancellationToken cancellationToken = default)
        where TSection : class, ISettingsSection<TSection>, new();
}
