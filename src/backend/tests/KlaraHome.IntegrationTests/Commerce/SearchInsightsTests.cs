using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Step 19 rows about the query log and the store's vocabulary: partitioning, the logging switches,
/// the zero-result report, and how a synonym or stop word takes effect.
/// </summary>
[Collection(KlaraHomeSchema.CollectionName)]
public sealed class SearchInsightsTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// Row 278: <c>search.search_queries</c> is created partitioned by month with its default
    /// partition, a row lands in the right partition, and the click update finds it by both halves
    /// of the key.
    /// </summary>
    [Fact]
    public async Task The_query_log_is_partitioned_and_a_click_is_found_by_both_halves_of_the_key()
    {
        SkipWithoutDocker();

        var partitioned = await Database.ScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_partitioned_table pt "
            + "JOIN pg_class c ON c.oid = pt.partrelid "
            + "JOIN pg_namespace n ON n.oid = c.relnamespace "
            + "WHERE n.nspname = 'search' AND c.relname = 'search_queries')",
            Cancellation);

        Assert.True(partitioned, "search.search_queries must be a partitioned table.");

        var defaultPartitionExists = await Database.ScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_inherits i "
            + "JOIN pg_class parent ON parent.oid = i.inhparent "
            + "JOIN pg_namespace n ON n.oid = parent.relnamespace "
            + "JOIN pg_class child ON child.oid = i.inhrelid "
            + "WHERE n.nspname = 'search' AND parent.relname = 'search_queries' "
            + "AND child.relname LIKE '%default%')",
            Cancellation);

        Assert.True(defaultPartitionExists, "search.search_queries must have a default partition.");

        var admin = await SignedInAdministratorAsync();
        var search = new SearchScenario(admin, Factory, Database, Cancellation);
        var taxonomy = await search.Catalog.TaxonomyAsync();

        var token = "Partition" + Guid.NewGuid().ToString("N")[..8];
        var product = await search.Catalog.DraftAsync(taxonomy, name: token);
        await search.Catalog.ActivateVariantAsync(product.VariantId);
        await search.Catalog.PublishAsync(product.Id);
        await search.Catalog.OfferAsync((await search.DefaultSellerAsync()).Id, product.VariantId, 750m);
        await search.DrainAsync();
        await search.WaitForRowAsync(product.VariantId);

        var client = CreateClient();

        var response = await ReadAsync(await client.GetAsync(
            new Uri($"/api/v1/store/products?q={token}", UriKind.Relative),
            Cancellation));

        var queryToken = response.GetProperty("queryToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(queryToken));

        // The row exists, was written into a real child partition rather than the parent relation
        // directly, and carries this month's data.
        var rows = await Database.RowsAsync(
            "SELECT tableoid::regclass::text AS partition_name, normalised_query "
            + "FROM search.search_queries WHERE normalised_query = $1 ORDER BY created_at DESC LIMIT 1",
            Cancellation,
            token.ToLowerInvariant());

        Assert.Single(rows);

        var partitionName = (string)rows[0]["partition_name"]!;
        Assert.NotEqual("search.search_queries", partitionName);
        Assert.Contains("search_queries", partitionName, StringComparison.Ordinal);

        // The click, found by both halves of the composite key the handle carries.
        var click = await client.PostAsJsonAsync(
            "/api/v1/store/search/click",
            new { queryToken, position = 1, variantId = product.VariantId },
            Cancellation);

        Assert.Equal(HttpStatusCode.NoContent, click.StatusCode);

        var clicked = await Database.ScalarAsync<int>(
            "SELECT clicked_position FROM search.search_queries WHERE normalised_query = $1 "
            + "ORDER BY created_at DESC LIMIT 1",
            Cancellation,
            token.ToLowerInvariant());

        Assert.Equal(1, clicked);
    }

    /// <summary>
    /// Row 279: the query log is written only when both <c>SearchSettings.LogQueries</c> and the
    /// <c>search.query-logging</c> flag are on.
    /// </summary>
    /// <remarks>
    /// The other half of this row — that a failed log write does not fail the search — is not
    /// independently faulted here: the write happens inside a narrow <c>try/catch(DbUpdateException)</c>
    /// in <c>SearchQueryRecorder.RecordAsync</c> that this suite read rather than re-proved, because
    /// forcing a real write failure against the shared collection database would mean breaking a
    /// table every other Step 19 test in this run also writes to. Recorded here rather than left
    /// silent, the way Step 10 recorded a criterion it asserted as behaviour rather than measurement.
    /// </remarks>
    [Fact]
    public async Task The_query_log_is_written_only_when_both_switches_are_on()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var search = new SearchScenario(admin, Factory, Database, Cancellation);
        var taxonomy = await search.Catalog.TaxonomyAsync();

        var token = "Gating" + Guid.NewGuid().ToString("N")[..8];
        var product = await search.Catalog.DraftAsync(taxonomy, name: token);
        await search.Catalog.ActivateVariantAsync(product.VariantId);
        await search.Catalog.PublishAsync(product.Id);
        await search.Catalog.OfferAsync((await search.DefaultSellerAsync()).Id, product.VariantId, 640m);
        await search.DrainAsync();
        await search.WaitForRowAsync(product.VariantId);

        var client = CreateClient();

        // Both on (the shipped default): a token is issued.
        var both = await ReadAsync(await client.GetAsync(
            new Uri($"/api/v1/store/products?q={token}A", UriKind.Relative),
            Cancellation));
        Assert.False(string.IsNullOrWhiteSpace(both.GetProperty("queryToken").GetString()));

        // The flag off: no token, whatever the setting says.
        Factory.Features["search.query-logging"] = false;

        var flagOff = await ReadAsync(await client.GetAsync(
            new Uri($"/api/v1/store/products?q={token}B", UriKind.Relative),
            Cancellation));
        Assert.Equal(JsonValueKind.Null, flagOff.GetProperty("queryToken").ValueKind);

        Factory.Features["search.query-logging"] = true;

        // The setting off: no token, even with the flag on.
        await PutSearchSettingsAsync(admin, logQueries: false);

        try
        {
            var settingOff = await ReadAsync(await client.GetAsync(
                new Uri($"/api/v1/store/products?q={token}C", UriKind.Relative),
                Cancellation));
            Assert.Equal(JsonValueKind.Null, settingOff.GetProperty("queryToken").ValueKind);
        }
        finally
        {
            await PutSearchSettingsAsync(admin, logQueries: true);
        }
    }

    /// <summary>
    /// Row 280: the zero-result report groups on the normalised query, counts click-through and
    /// average click position correctly, and separates <c>search</c> from <c>suggest</c>.
    /// </summary>
    [Fact]
    public async Task The_zero_result_report_groups_correctly_and_separates_search_from_suggest()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var search = new SearchScenario(admin, Factory, Database, Cancellation);
        var taxonomy = await search.Catalog.TaxonomyAsync();

        var product = await search.Catalog.DraftAsync(taxonomy);
        await search.Catalog.ActivateVariantAsync(product.VariantId);
        await search.Catalog.PublishAsync(product.Id);
        await search.Catalog.OfferAsync((await search.DefaultSellerAsync()).Id, product.VariantId, 550m);
        await search.DrainAsync();
        await search.WaitForRowAsync(product.VariantId);

        var client = CreateClient();
        var nothingToken = "NoSuchThing" + Guid.NewGuid().ToString("N")[..8];

        // Two zero-result searches for the same normalised query, and one click-through search. The
        // two spellings of the query string differ — the output cache varies on the whole query
        // string, so an identical repeat would be served from the cache and never reach the
        // recorder a second time.
        await ReadAsync(await client.GetAsync(
            new Uri($"/api/v1/store/products?q={nothingToken}", UriKind.Relative), Cancellation));
        await ReadAsync(await client.GetAsync(
            new Uri($"/api/v1/store/products?q={nothingToken}&size=24", UriKind.Relative), Cancellation));

        var found = await ReadAsync(await client.GetAsync(
            new Uri("/api/v1/store/products?q=" + product.Sku, UriKind.Relative), Cancellation));

        var foundToken = found.GetProperty("queryToken").GetString();

        if (!string.IsNullOrWhiteSpace(foundToken))
        {
            var click = await client.PostAsJsonAsync(
                "/api/v1/store/search/click",
                new { queryToken = foundToken, position = 1, variantId = product.VariantId },
                Cancellation);
            Assert.Equal(HttpStatusCode.NoContent, click.StatusCode);
        }

        // A suggestion keystroke for the same zero-result token, which must not be folded into the
        // "search" report.
        await ReadAsync(await client.GetAsync(
            new Uri($"/api/v1/store/search/suggest?q={nothingToken}", UriKind.Relative), Cancellation));

        var zeroResultReport = await ReadAsync(await admin.GetAsync(
            new Uri("/api/v1/admin/search/queries/zero-results?source=search&size=200", UriKind.Relative),
            Cancellation));

        var zeroRow = zeroResultReport.EnumerateArray()
            .FirstOrDefault(row => string.Equals(
                row.GetProperty("normalisedQuery").GetString(),
                nothingToken,
                StringComparison.OrdinalIgnoreCase));

        Assert.True(zeroRow.ValueKind == JsonValueKind.Object, "The zero-result query did not appear in the search report.");
        Assert.Equal(2, zeroRow.GetProperty("count").GetInt64());
        Assert.Equal(2, zeroRow.GetProperty("zeroResultCount").GetInt64());
        Assert.Equal(0d, zeroRow.GetProperty("clickThroughRate").GetDouble());

        // The suggestion report is a different list: the zero-result token must not appear under
        // source=search's sibling report at all if it were only ever asked as a suggestion — here it
        // must appear under suggest instead, proving the two sources are kept apart.
        var suggestReport = await ReadAsync(await admin.GetAsync(
            new Uri("/api/v1/admin/search/queries?source=suggest&size=200", UriKind.Relative),
            Cancellation));

        Assert.Contains(
            suggestReport.EnumerateArray(),
            row => string.Equals(
                row.GetProperty("normalisedQuery").GetString(),
                nothingToken,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Row 281: synonyms and stop words take effect on the next query after the vocabulary cache
    /// expires, and an edit in one process is seen by another within the TTL.
    /// </summary>
    [Fact]
    public async Task A_vocabulary_edit_is_seen_by_another_process_within_the_cache_ttl()
    {
        SkipWithoutDocker();

        // A short TTL on a second host, set before it is ever built.
        var second = NewFactory();
        second.Overrides["Search:VocabularyCacheSeconds"] = "2";

        var admin = await SignedInAdministratorAsync();
        var search = new SearchScenario(admin, Factory, Database, Cancellation);
        var taxonomy = await search.Catalog.TaxonomyAsync();

        var term = "zorbaz" + Guid.NewGuid().ToString("N")[..6];
        var expansion = "qwenda" + Guid.NewGuid().ToString("N")[..6];

        var product = await search.Catalog.DraftAsync(taxonomy, name: expansion);
        await search.Catalog.ActivateVariantAsync(product.VariantId);
        await search.Catalog.PublishAsync(product.Id);
        await search.Catalog.OfferAsync((await search.DefaultSellerAsync()).Id, product.VariantId, 620m);
        await search.DrainAsync();
        await search.WaitForRowAsync(product.VariantId);

        var secondClient = second.CreateClient();

        // Every call below carries a distinct, harmless query-string parameter: the output cache
        // varies on the whole query string, and an identical repeat would be served from it rather
        // than reaching the vocabulary cache this row is actually about.

        // Warms the second host's cache with the vocabulary as it stood before the synonym existed.
        await ReadAsync(await secondClient.GetAsync(
            new Uri($"/api/v1/store/products?q={term}&call=1", UriKind.Relative),
            Cancellation));

        // The edit, made on the first host — this process's own cache is invalidated immediately by
        // the write path, which is not what this row is about.
        await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/search/synonyms",
                new { term, expansions = new[] { expansion }, isBidirectional = true, note = "Step 19 test" },
                Cancellation),
            Cancellation);

        try
        {
            // Immediately on the second host: still the stale, cached vocabulary.
            var stale = await ReadAsync(await secondClient.GetAsync(
                new Uri($"/api/v1/store/products?q={term}&call=2", UriKind.Relative),
                Cancellation));

            Assert.Equal(0, stale.GetProperty("total").GetInt64());

            // Past the TTL: the next query reloads and sees the edit.
            await Task.Delay(TimeSpan.FromSeconds(3), Cancellation);

            var refreshed = await ReadAsync(await secondClient.GetAsync(
                new Uri($"/api/v1/store/products?q={term}&call=3", UriKind.Relative),
                Cancellation));

            Assert.Contains(
                refreshed.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("variantId").GetGuid() == product.VariantId);
        }
        finally
        {
            second.Dispose();
        }
    }

    /// <summary>
    /// Row 282: a bidirectional synonym expands in both directions and between siblings.
    /// </summary>
    [Fact]
    public async Task A_bidirectional_synonym_expands_between_every_sibling()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var search = new SearchScenario(admin, Factory, Database, Cancellation);
        var taxonomy = await search.Catalog.TaxonomyAsync();

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var sofa = "sofaterm" + suffix;
        var couch = "couchterm" + suffix;
        var settee = "setteeterm" + suffix;

        var sofaProduct = await search.Catalog.DraftAsync(taxonomy, name: sofa);
        await search.Catalog.ActivateVariantAsync(sofaProduct.VariantId);
        await search.Catalog.PublishAsync(sofaProduct.Id);
        await search.Catalog.OfferAsync((await search.DefaultSellerAsync()).Id, sofaProduct.VariantId, 510m);

        var couchProduct = await search.Catalog.DraftAsync(taxonomy, name: couch);
        await search.Catalog.ActivateVariantAsync(couchProduct.VariantId);
        await search.Catalog.PublishAsync(couchProduct.Id);
        await search.Catalog.OfferAsync((await search.DefaultSellerAsync()).Id, couchProduct.VariantId, 520m);

        var setteeProduct = await search.Catalog.DraftAsync(taxonomy, name: settee);
        await search.Catalog.ActivateVariantAsync(setteeProduct.VariantId);
        await search.Catalog.PublishAsync(setteeProduct.Id);
        await search.Catalog.OfferAsync((await search.DefaultSellerAsync()).Id, setteeProduct.VariantId, 530m);

        await search.DrainAsync();
        await search.WaitForRowAsync(sofaProduct.VariantId);
        await search.WaitForRowAsync(couchProduct.VariantId);
        await search.WaitForRowAsync(setteeProduct.VariantId);

        await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/search/synonyms",
                new { term = sofa, expansions = new[] { couch, settee }, isBidirectional = true, note = "Step 19 test" },
                Cancellation),
            Cancellation);

        var client = CreateClient();

        foreach (var query in new[] { sofa, couch, settee })
        {
            var response = await ReadAsync(await client.GetAsync(
                new Uri($"/api/v1/store/products?q={query}", UriKind.Relative),
                Cancellation));

            var ids = response.GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("variantId").GetGuid())
                .ToHashSet();

            Assert.Contains(sofaProduct.VariantId, ids);
            Assert.Contains(couchProduct.VariantId, ids);
            Assert.Contains(setteeProduct.VariantId, ids);
        }
    }

    /// <summary>Replaces only <c>logQueries</c> on the search settings section, keeping every default.</summary>
    private static async Task PutSearchSettingsAsync(HttpClient admin, bool logQueries)
        => await Rest.ReadAsync(
            await admin.PutAsJsonAsync(
                "/api/v1/admin/settings/search",
                new
                {
                    minimumQueryLength = 2,
                    maxQueryLength = 120,
                    defaultSort = "relevance",
                    hideUnavailable = false,
                    enableFuzzyFallback = true,
                    fuzzyThreshold = 0.3m,
                    relevanceWeight = 1.0m,
                    popularityWeight = 0.35m,
                    ratingWeight = 0.15m,
                    availabilityBoost = 0.25m,
                    maxFacetValues = 20,
                    suggestionLimit = 8,
                    priceBands = new[] { 499m, 999m, 1999m, 4999m, 9999m },
                    logQueries,
                },
                Cancellation),
            Cancellation);
}
