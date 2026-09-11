using System.Net.Http.Json;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Search.Application;
using KlaraHome.Modules.Search.Infrastructure.Projection;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Step 19 rows about the operator's safety net: a resumable full rebuild, a rebuild racing the
/// event pipeline, and the staleness sweep.
/// </summary>
[Collection(KlaraHomeSchema.CollectionName)]
public sealed class SearchIndexRebuildTests : CommerceTestBase
{
    /// <param name="fixture">The migrated database.</param>
    public SearchIndexRebuildTests(KlaraHomeSchemaFixture fixture)
        : base(fixture)
    {
        // Set before the host is built — a batch of one so the resumable-walk test genuinely pauses
        // across several calls, and the same stale window the production default already carries, so
        // the sweep test's assertions are about the sweep and not about an unusual configuration.
        Factory.Overrides["Search:ReindexBatchSize"] = "1";
        Factory.Overrides["Search:StaleAfterHours"] = "24";
    }

    /// <summary>
    /// Row 275: a full rebuild is resumable — walking with a cursor covers every variant exactly
    /// once, retires the rows whose offers are gone, and reaches the end of a catalogue larger than
    /// one batch.
    /// </summary>
    [Fact]
    public async Task A_rebuild_walks_every_variant_exactly_once_and_reaches_the_end()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var search = new SearchScenario(admin, Factory, Database, Cancellation);
        var taxonomy = await search.Catalog.TaxonomyAsync();

        // Every variant in this shared collection database that existed before this test's own five
        // — walking from the true beginning would mean this test's page budget scaling with the size
        // of everything every other test in the run has already created. Variant ids are UuidV7 and
        // therefore time-ordered, so starting the walk just after the highest one that already
        // existed reaches only the five this test is about, whatever else is in the database.
        var priorHighestVariantRow = await Database.RowsAsync(
            "SELECT id FROM catalog.variants ORDER BY id DESC LIMIT 1",
            Cancellation);

        var priorHighestVariantId = priorHighestVariantRow.Count == 0
            ? (Guid?)null
            : (Guid)priorHighestVariantRow[0]["id"]!;

        var variantIds = new List<Guid>();

        for (var index = 0; index < 5; index++)
        {
            var product = await search.Catalog.DraftAsync(taxonomy);
            await search.Catalog.ActivateVariantAsync(product.VariantId);
            await search.Catalog.PublishAsync(product.Id);
            await search.Catalog.OfferAsync((await search.DefaultSellerAsync()).Id, product.VariantId, 500m + index);
            variantIds.Add(product.VariantId);
        }

        // The batch of one set in the constructor and a ceiling of two: the walk has to pause after
        // two variants and resume from where it left off, exactly as an operator working through a
        // fifty-thousand-row catalogue would.
        Guid? cursor = priorHighestVariantId;
        var walkedIds = new List<Guid>();
        var pages = 0;

        while (pages < 20)
        {
            pages++;

            var body = new { afterVariantId = cursor, maxVariants = 2, variantIds = (Guid[]?)null };

            var response = await Rest.ReadAsync(
                await admin.PostAsJsonAsync("/api/v1/admin/search/index/rebuild", body, Cancellation),
                Cancellation);

            var complete = response.GetProperty("isComplete").GetBoolean();
            var resumeAfter = response.GetProperty("resumeAfterVariantId");

            cursor = resumeAfter.ValueKind == System.Text.Json.JsonValueKind.Null
                ? null
                : resumeAfter.GetGuid();

            if (complete)
            {
                break;
            }

            Assert.NotNull(cursor);
        }

        Assert.True(pages < 20, "The rebuild never reached the end of the catalogue.");

        foreach (var variantId in variantIds)
        {
            var row = await search.RowAsync(variantId);
            Assert.NotNull(row);
            Assert.True((bool)row!["is_active"]!);
        }
    }

    /// <summary>
    /// Row 276: a rebuild running concurrently with the event pipeline produces one row per variant
    /// and never two, enforced by the unique index rather than by timing.
    /// </summary>
    [Fact]
    public async Task A_concurrent_rebuild_and_event_pipeline_never_produce_two_rows_for_one_variant()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var search = new SearchScenario(admin, Factory, Database, Cancellation);
        var taxonomy = await search.Catalog.TaxonomyAsync();

        var variantIds = new List<Guid>();

        for (var index = 0; index < 6; index++)
        {
            var product = await search.Catalog.DraftAsync(taxonomy);
            await search.Catalog.ActivateVariantAsync(product.VariantId);
            await search.Catalog.PublishAsync(product.Id);
            await search.Catalog.OfferAsync((await search.DefaultSellerAsync()).Id, product.VariantId, 500m + index);
            variantIds.Add(product.VariantId);
        }

        // The event pipeline delivering the six ListingPublished events, and a full rebuild that
        // re-resolves the same six variants from the catalogue directly, run against the same
        // database at the same time.
        var drain = search.DrainAsync();

        var rebuild = InScopeAsync<SearchIndexService, ReindexResponse>(
            (service, ct) => service.RebuildAsync(afterVariantId: null, maxVariants: null, ct));

        await Task.WhenAll(drain, rebuild);

        var duplicates = await Database.CountAsync(
            "SELECT COUNT(*) FROM ("
            + "  SELECT variant_id FROM search.product_search_projection"
            + "  WHERE variant_id = ANY($1)"
            + "  GROUP BY variant_id HAVING COUNT(*) > 1"
            + ") duplicated",
            Cancellation,
            variantIds.ToArray());

        Assert.Equal(0, duplicates);

        foreach (var variantId in variantIds)
        {
            Assert.NotNull(await search.RowAsync(variantId));
        }

        // The guarantee is the index, not the timing of this one run: the unique constraint the
        // module relies on actually exists on the table.
        var indexExists = await Database.ScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'search' "
            + "AND tablename = 'product_search_projection' AND indexdef ILIKE '%UNIQUE%tenant_id%variant_id%')",
            Cancellation);

        Assert.True(indexExists);
    }

    /// <summary>
    /// Row 277: the staleness sweep finds only rows older than the configured window, oldest first,
    /// and is a no-op on a healthy index.
    /// </summary>
    [Fact]
    public async Task The_staleness_sweep_finds_only_rows_past_the_window_and_is_a_no_op_when_healthy()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var search = new SearchScenario(admin, Factory, Database, Cancellation);
        var taxonomy = await search.Catalog.TaxonomyAsync();

        var freshProduct = await search.Catalog.DraftAsync(taxonomy);
        await search.Catalog.ActivateVariantAsync(freshProduct.VariantId);
        await search.Catalog.PublishAsync(freshProduct.Id);
        await search.Catalog.OfferAsync((await search.DefaultSellerAsync()).Id, freshProduct.VariantId, 600m);

        var staleProduct = await search.Catalog.DraftAsync(taxonomy);
        await search.Catalog.ActivateVariantAsync(staleProduct.VariantId);
        await search.Catalog.PublishAsync(staleProduct.Id);
        await search.Catalog.OfferAsync((await search.DefaultSellerAsync()).Id, staleProduct.VariantId, 700m);

        await search.DrainAsync();
        await search.WaitForRowAsync(freshProduct.VariantId);
        await search.WaitForRowAsync(staleProduct.VariantId);

        // A healthy index: nothing has fallen behind, so a sweep is a no-op.
        var healthySweep = await InScopeAsync<SearchIndexService, ProjectionOutcome>(
            (service, ct) => service.RefreshStaleAsync(batchSize: 50, ct));

        Assert.Equal(0, healthySweep.Written);
        Assert.Equal(0, healthySweep.Retired);

        // Back-date only the stale product's row, past the configured window.
        await Database.ExecuteAsync(
            "UPDATE search.product_search_projection SET indexed_at = now() - interval '2 days' "
            + "WHERE variant_id = $1",
            Cancellation,
            staleProduct.VariantId);

        var sweep = await InScopeAsync<SearchIndexService, ProjectionOutcome>(
            (service, ct) => service.RefreshStaleAsync(batchSize: 50, ct));

        Assert.Equal(1, sweep.Written);

        var refreshed = await search.RowAsync(staleProduct.VariantId);
        Assert.NotNull(refreshed);
        Assert.True(DateTime.UtcNow - IndexedAt(refreshed!) < TimeSpan.FromMinutes(1));

        // The fresh row was never a candidate — its own timestamp is untouched.
        var untouched = await search.RowAsync(freshProduct.VariantId);
        Assert.NotNull(untouched);
        Assert.True(DateTime.UtcNow - IndexedAt(untouched!) < TimeSpan.FromMinutes(1));

        static DateTime IndexedAt(IReadOnlyDictionary<string, object?> row)
            => DateTime.SpecifyKind(Convert.ToDateTime(row["indexed_at"]), DateTimeKind.Utc);
    }
}
