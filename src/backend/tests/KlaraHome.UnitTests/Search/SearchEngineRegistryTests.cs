using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Search.Application;
using KlaraHome.Modules.Search.Infrastructure;
using KlaraHome.Modules.Search.Infrastructure.Engine;
using KlaraHome.Modules.Search.Infrastructure.Query;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KlaraHome.UnitTests.Search;

/// <summary>
/// Which engine answers a query (ADR-019), and which of ADR-019's four fall-back reasons the
/// registry chose, each proved without a database — <c>SearchEngineRegistry</c> takes its engines,
/// its flag reader and its options by substitution, and never opens a connection to decide.
/// </summary>
public sealed class SearchEngineRegistryTests
{
    [Fact]
    public async Task Blank_provider_falls_back_to_postgres_with_no_flag_check_at_all()
    {
        var flags = new StubFlags(enabled: true); // even a flag that would allow it changes nothing
        var registry = Registry(Configured(string.Empty), flags, new StubEngine("meilisearch", configured: true));

        var engine = await registry.ResolveAsync(CancellationToken.None);

        Assert.Equal(PostgresSearchEngine.EngineKey, engine.Key);
        Assert.Equal(0, flags.CallCount);
    }

    [Fact]
    public async Task An_unknown_provider_key_falls_back_to_postgres()
    {
        var flags = new StubFlags(enabled: true);
        var registry = Registry(Configured("some-engine-this-build-has-no-adapter-for"), flags, new StubEngine("meilisearch", configured: true));

        var engine = await registry.ResolveAsync(CancellationToken.None);

        Assert.Equal(PostgresSearchEngine.EngineKey, engine.Key);
    }

    [Fact]
    public async Task A_configured_engine_with_the_flag_off_falls_back_to_postgres()
    {
        var flags = new StubFlags(enabled: false);
        var registry = Registry(Configured("meilisearch"), flags, new StubEngine("meilisearch", configured: true));

        var engine = await registry.ResolveAsync(CancellationToken.None);

        Assert.Equal(PostgresSearchEngine.EngineKey, engine.Key);
        Assert.Equal(1, flags.CallCount);
    }

    [Fact]
    public async Task A_named_engine_that_reports_itself_unconfigured_falls_back_to_postgres()
    {
        var flags = new StubFlags(enabled: true);
        var registry = Registry(Configured("meilisearch"), flags, new StubEngine("meilisearch", configured: false));

        var engine = await registry.ResolveAsync(CancellationToken.None);

        Assert.Equal(PostgresSearchEngine.EngineKey, engine.Key);
    }

    [Fact]
    public async Task A_named_configured_engine_with_the_flag_on_is_selected()
    {
        var flags = new StubFlags(enabled: true);
        var registry = Registry(Configured("meilisearch"), flags, new StubEngine("meilisearch", configured: true));

        var engine = await registry.ResolveAsync(CancellationToken.None);

        Assert.Equal("meilisearch", engine.Key);
    }

    private static SearchEngineRegistry Registry(SearchOptions options, IFeatureFlags flags, params ISearchEngine[] others)
        => new(
            [.. others, new StubEngine(PostgresSearchEngine.EngineKey, configured: true)],
            flags,
            new StubOptions(options),
            NullLogger<SearchEngineRegistry>.Instance);

    private static SearchOptions Configured(string provider) => new() { Provider = provider };

    /// <summary>An engine that answers nothing beyond its own key and configured state.</summary>
    private sealed class StubEngine(string key, bool configured) : ISearchEngine
    {
        public string Key => key;

        public bool IsConfigured => configured;

        public Task<SearchResults> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SearchSuggestion>> SuggestAsync(
            NormalizedQuery query,
            int limit,
            SearchSettings settings,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    /// <summary>Counts how many times the external-engine switch was asked about, and answers the same way every time.</summary>
    private sealed class StubFlags(bool enabled) : IFeatureFlags
    {
        public int CallCount { get; private set; }

        public ValueTask<bool> IsEnabledAsync(
            string key,
            FeatureAudience audience = default,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ValueTask.FromResult(enabled);
        }

        public ValueTask<IReadOnlyDictionary<string, bool>> GetAllAsync(
            FeatureAudience audience = default,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubOptions(SearchOptions value) : IOptions<SearchOptions>
    {
        public SearchOptions Value => value;
    }
}
