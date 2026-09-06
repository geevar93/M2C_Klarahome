using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Search.Infrastructure.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Search.Infrastructure.Engine;

/// <summary>
/// Chooses the engine that answers a query.
/// </summary>
/// <remarks>
/// <para>
/// The mechanism ADR-019 defines, and deliberately the same one the courier adapters use
/// (ADR-018): implementations are keyed by the engine they are, a configuration value names one,
/// and the module resolves it at the point of use rather than at registration. Moving a store from
/// PostgreSQL to a dedicated engine is then a class, a registration line, a configuration value and
/// a feature flag — and nothing outside this module changes at all.
/// </para>
/// <para>
/// Two independent switches, on purpose. <c>Search:Provider</c> is a deployment's statement about
/// what it has; the <c>search.external-engine</c> flag is an operator's statement about whether to
/// use it. A dedicated engine that starts returning stale results at four in the afternoon is
/// switched off from the admin screen in seconds and the store keeps working, which is not true of
/// anything that needs a deploy.
/// </para>
/// <para>
/// Every path that cannot be honoured falls back to PostgreSQL and says so. A store whose search
/// answers 502 because somebody typed a provider name into an environment file is a far worse
/// outcome than a store whose search is merely not as fast as it could have been — and this is the
/// same judgement the payout and courier registries made for the same reason.
/// </para>
/// </remarks>
/// <param name="engines">Every engine this build has.</param>
/// <param name="flags">Reads the switch that allows a dedicated engine at all.</param>
/// <param name="options">Supplies the configured provider key.</param>
/// <param name="logger">Reports which engine answered, and why it was not the configured one.</param>
internal sealed partial class SearchEngineRegistry(
    IEnumerable<ISearchEngine> engines,
    IFeatureFlags flags,
    IOptions<SearchOptions> options,
    ILogger<SearchEngineRegistry> logger)
{
    /// <summary>The engine to use for the next query.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ISearchEngine> ResolveAsync(CancellationToken cancellationToken)
    {
        var fallback = engines.First(engine =>
            string.Equals(engine.Key, PostgresSearchEngine.EngineKey, StringComparison.OrdinalIgnoreCase));

        var configured = options.Value.Provider;

        if (string.IsNullOrWhiteSpace(configured)
            || string.Equals(configured, PostgresSearchEngine.EngineKey, StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        var enabled = await flags
            .IsEnabledAsync(SearchFeatures.ExternalEngine, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!enabled)
        {
            ExternalEngineOff(logger, configured);
            return fallback;
        }

        var engine = engines.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, configured, StringComparison.OrdinalIgnoreCase));

        if (engine is null)
        {
            UnknownEngine(logger, configured);
            return fallback;
        }

        if (!engine.IsConfigured)
        {
            EngineUnusable(logger, configured);
            return fallback;
        }

        return engine;
    }

    [LoggerMessage(
        EventId = 7910,
        Level = LogLevel.Information,
        Message = "Search engine {Engine} is configured but the search.external-engine flag is off; "
                  + "PostgreSQL full text is answering queries")]
    private static partial void ExternalEngineOff(ILogger logger, string engine);

    [LoggerMessage(
        EventId = 7911,
        Level = LogLevel.Warning,
        Message = "Search:Provider names {Engine}, which this build has no adapter for; "
                  + "PostgreSQL full text is answering queries")]
    private static partial void UnknownEngine(ILogger logger, string engine);

    [LoggerMessage(
        EventId = 7912,
        Level = LogLevel.Warning,
        Message = "Search engine {Engine} is selected but reports itself unusable — its credentials "
                  + "are probably blank; PostgreSQL full text is answering queries")]
    private static partial void EngineUnusable(ILogger logger, string engine);
}
