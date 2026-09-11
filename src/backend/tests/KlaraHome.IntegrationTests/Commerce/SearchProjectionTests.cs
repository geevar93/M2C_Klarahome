using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Step 19 rows about the query engine itself: the generated SQL runs, ranks by the declared
/// weights, pages without repeating or skipping a row, and the two GIN indexes it depends on are
/// actually used.
/// </summary>
/// <remarks>
/// Proved against a real, migrated Postgres database through <c>GET /store/products</c>, the same
/// host every other Step 29 commerce suite runs against. A handful of assertions read
/// <c>search.product_search_projection</c> directly — <c>EXPLAIN</c> for the index claims, and a raw
/// <c>UPDATE</c> to give one small catalogue two independent attribute dimensions without standing up
/// a second variant axis through the API, which the module treats as an opaque projection of another
/// module's facts either way.
/// </remarks>
[Collection(KlaraHomeSchema.CollectionName)]
public sealed class SearchProjectionTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>Row 265: the generated <c>search_vector</c> ranks by its declared weights.</summary>
    [Fact]
    public async Task A_name_match_outranks_the_same_term_appearing_only_in_a_category_name()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var search = new SearchScenario(admin, Factory, Database, Cancellation);
        var taxonomy = await search.Catalog.TaxonomyAsync();

        // Alphanumeric only, so the tokenizer and tsquery treat it as one clean lexeme with no
        // ambiguity about what a match against it means.
        var token = "Kalpataru" + Guid.NewGuid().ToString("N")[..10];

        // Product A: the token lives in the product's own name — weight A.
        var productA = await search.Catalog.DraftAsync(taxonomy, name: $"{token} Cushion Cover");
        await search.Catalog.ActivateVariantAsync(productA.VariantId);
        await search.Catalog.PublishAsync(productA.Id);
        await search.Catalog.OfferAsync((await search.DefaultSellerAsync()).Id, productA.VariantId, 999m);

        // Product B: the token lives only in a category's name — weight C. A real category under the
        // same root, not a decoy string, so the generated column's own weighting is what is on trial.
        var leafB = await search.Catalog.CategoryAsync($"{token} Furnishings", taxonomy.RootCategoryId);
        var taxonomyB = taxonomy with { CategoryId = leafB.Id, CategorySlug = leafB.Slug };
        var productB = await search.Catalog.DraftAsync(taxonomyB);
        await search.Catalog.ActivateVariantAsync(productB.VariantId);
        await search.Catalog.PublishAsync(productB.Id);
        await search.Catalog.OfferAsync((await search.DefaultSellerAsync()).Id, productB.VariantId, 999m);

        await search.DrainAsync();
        await search.WaitForRowAsync(productA.VariantId);
        await search.WaitForRowAsync(productB.VariantId);

        var client = CreateClient();

        var response = await ReadAsync(await client.GetAsync(
            new Uri($"/api/v1/store/products?q={token}", UriKind.Relative),
            Cancellation));

        var ids = response.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("variantId").GetGuid())
            .ToList();

        var indexA = ids.IndexOf(productA.VariantId);
        var indexB = ids.IndexOf(productB.VariantId);

        Assert.True(indexA >= 0, "The name match did not appear in the results at all.");
        Assert.True(indexB >= 0, "The category match did not appear in the results at all.");
        Assert.True(indexA < indexB, "A product-name match must outrank the same term in a category name.");
    }

    /// <summary>
    /// Row 266: the whole generated statement — the union of facet branches, the lateral
    /// <c>jsonb</c> expansion, the trigram predicate and <c>set_limit</c> — parses and runs.
    /// </summary>
    [Fact]
    public async Task The_generated_sql_runs_across_every_filter_and_sort_shape()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var search = new SearchScenario(admin, Factory, Database, Cancellation);
        var taxonomy = await search.Catalog.TaxonomyAsync();

        var product = await search.Catalog.DraftAsync(taxonomy);
        await search.Catalog.ActivateVariantAsync(product.VariantId);
        await search.Catalog.PublishAsync(product.Id);
        await search.Catalog.OfferAsync((await search.DefaultSellerAsync()).Id, product.VariantId, 899m);
        await search.DrainAsync();
        await search.WaitForRowAsync(product.VariantId);

        var client = CreateClient();

        // Every optional predicate at once, so the union of facet branches, the lateral jsonb
        // expansion over attributes, the price-band join and the rating/discount thresholds are all
        // on the plan for this one call.
        var everything = $"/api/v1/store/products?category={taxonomy.CategoryId}&brand={taxonomy.BrandId}"
            + $"&minPrice=0&maxPrice=100000&rating=0&discount=0&inStock=false"
            + $"&attr.{taxonomy.ColourCode}=beige,charcoal";

        await ReadAsync(await client.GetAsync(new Uri(everything, UriKind.Relative), Cancellation));

        // Every declared sort, each of which changes the ORDER BY and the keyset comparison.
        foreach (var sort in new[] { "relevance", "price-asc", "price-desc", "newest", "discount", "rating", "popularity" })
        {
            var byUri = new Uri($"/api/v1/store/products?category={taxonomy.CategoryId}&sort={sort}", UriKind.Relative);
            await ReadAsync(await client.GetAsync(byUri, Cancellation));
        }

        // The trigram pass and set_limit: a query with no exact match at all, which the handler only
        // retries fuzzily — proving the statement that runs set_limit and the trigram predicate.
        var fuzzy = await ReadAsync(await client.GetAsync(
            new Uri("/api/v1/store/products?q=zzzzznonwordzzzzz", UriKind.Relative),
            Cancellation));

        Assert.Equal(0, fuzzy.GetProperty("total").GetInt64());
    }

    /// <summary>
    /// Row 264: a facet group's count equals a ground-truth <c>COUNT(*)</c> over the same predicate
    /// set with that group's own filter lifted — for a browse, one filter, and two attribute filters
    /// applied at once.
    /// </summary>
    [Fact]
    public async Task Facet_counts_agree_with_a_ground_truth_count_with_their_own_filter_lifted()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var search = new SearchScenario(admin, Factory, Database, Cancellation);
        var taxonomy = await search.Catalog.TaxonomyAsync();

        // Two colours (the real variant axis) crossed with a synthetic "material" dimension written
        // straight onto the jsonb column — the projection treats attributes as an opaque copy of
        // another module's facts either way, so this proves the engine's own arithmetic without
        // standing up a second variant axis through the API.
        var beigeCotton = await OneVariantAsync(search, taxonomy, optionIndex: 0);
        var beigeLinen = await OneVariantAsync(search, taxonomy, optionIndex: 0);
        var charcoalCotton = await OneVariantAsync(search, taxonomy, optionIndex: 1);
        var charcoalLinen = await OneVariantAsync(search, taxonomy, optionIndex: 1);

        var variantIds = new[] { beigeCotton, beigeLinen, charcoalCotton, charcoalLinen };

        await SetMaterialAsync(beigeCotton, "cotton");
        await SetMaterialAsync(beigeLinen, "linen");
        await SetMaterialAsync(charcoalCotton, "cotton");
        await SetMaterialAsync(charcoalLinen, "linen");

        var client = CreateClient();
        var colour = taxonomy.ColourCode;

        var response = await ReadAsync(await client.GetAsync(
            new Uri(
                $"/api/v1/store/products?category={taxonomy.CategoryId}&attr.{colour}=beige&attr.material=cotton",
                UriKind.Relative),
            Cancellation));

        // The result set itself: both filters apply, so only the one row satisfying both.
        Assert.Equal(1, response.GetProperty("total").GetInt64());

        var facets = response.GetProperty("facets").EnumerateArray()
            .ToDictionary(group => group.GetProperty("key").GetString()!, group => group);

        // The colour facet's own filter is lifted, so it is computed with only the material filter
        // applied: both colours have a cotton row among our four, one each.
        var colourFacetGroundTruth = await Database.CountAsync(
            "SELECT COUNT(*) FROM search.product_search_projection "
            + "WHERE variant_id = ANY($1) AND attributes @> '{\"material\":[\"cotton\"]}'::jsonb "
            + "AND attributes @> jsonb_build_object($2::text, ARRAY[$3::text])",
            Cancellation,
            variantIds,
            colour,
            "beige");

        var colourFacet = facets["attr." + colour];

        var beigeValue = colourFacet.GetProperty("values").EnumerateArray()
            .First(value => value.GetProperty("value").GetString() == "beige");

        Assert.Equal(colourFacetGroundTruth, beigeValue.GetProperty("count").GetInt64());
        Assert.Equal(1, colourFacetGroundTruth); // exactly one beige row also carries material=cotton

        // The material facet's own filter is lifted, so it is computed with only the colour filter
        // applied: beige has one cotton row and one linen row among our four.
        var materialFacet = facets["attr.material"];
        var linenValue = materialFacet.GetProperty("values").EnumerateArray()
            .FirstOrDefault(value => value.GetProperty("value").GetString() == "linen");

        Assert.True(linenValue.ValueKind == JsonValueKind.Object, "A lifted material facet must still offer linen.");
        Assert.Equal(1, linenValue.GetProperty("count").GetInt64());

        async Task<Guid> OneVariantAsync(SearchScenario scenario, CatalogTaxonomy taxonomyArg, int optionIndex)
        {
            var drafted = await scenario.Catalog.DraftAsync(taxonomyArg);

            var variant = optionIndex == 0
                ? drafted.VariantId
                : (await scenario.Catalog.VariantAsync(drafted.Id, taxonomyArg, optionIndex))
                    .GetProperty("id").GetGuid();

            await scenario.Catalog.ActivateVariantAsync(variant);
            await scenario.Catalog.PublishAsync(drafted.Id);
            await scenario.Catalog.OfferAsync((await scenario.DefaultSellerAsync()).Id, variant, 999m);
            await scenario.DrainAsync();
            await scenario.WaitForRowAsync(variant);

            return variant;
        }

        Task SetMaterialAsync(Guid variantId, string material)
            => Database.ExecuteAsync(
                "UPDATE search.product_search_projection "
                + "SET attributes = attributes || jsonb_build_object('material', ARRAY[$2::text]), "
                + "attribute_meta = attribute_meta || jsonb_build_object("
                + "  'material', jsonb_build_object('name', 'Material', 'values', "
                + "    jsonb_build_object($2::text, initcap($2::text)))) "
                + "WHERE variant_id = $1",
                Cancellation,
                variantId,
                material);
    }

    /// <summary>Row 268: a category filter uses the GIN index over <c>category_ids</c>.</summary>
    [Fact]
    public async Task A_category_subtree_filter_uses_the_gin_index_on_category_ids()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var search = new SearchScenario(admin, Factory, Database, Cancellation);
        var taxonomy = await search.Catalog.TaxonomyAsync();

        var product = await search.Catalog.DraftAsync(taxonomy);
        await search.Catalog.ActivateVariantAsync(product.VariantId);
        await search.Catalog.PublishAsync(product.Id);
        await search.Catalog.OfferAsync((await search.DefaultSellerAsync()).Id, product.VariantId, 999m);
        await search.DrainAsync();
        await search.WaitForRowAsync(product.VariantId);

        var rows = await Database.RowsAfterAsync(
            "SET enable_seqscan = off",
            "EXPLAIN SELECT id FROM search.product_search_projection "
            + "WHERE category_ids @> ARRAY[$1::uuid]",
            Cancellation,
            taxonomy.CategoryId);

        var plan = string.Join('\n', rows.Select(row => row["QUERY PLAN"]?.ToString()));

        Assert.Contains("ix_product_search_projection_category_ids", plan, StringComparison.Ordinal);
    }

    /// <summary>
    /// Row 269: an attribute filter uses the GIN index over <c>attributes</c>, and two values of one
    /// attribute combine as OR while two attributes combine as AND.
    /// </summary>
    [Fact]
    public async Task An_attribute_filter_uses_the_gin_index_and_combines_or_within_and_across()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var search = new SearchScenario(admin, Factory, Database, Cancellation);
        var taxonomy = await search.Catalog.TaxonomyAsync();

        var beige = await search.Catalog.DraftAsync(taxonomy);
        await search.Catalog.ActivateVariantAsync(beige.VariantId);
        await search.Catalog.PublishAsync(beige.Id);
        await search.Catalog.OfferAsync((await search.DefaultSellerAsync()).Id, beige.VariantId, 999m);

        var charcoalVariant = (await search.Catalog.VariantAsync(beige.Id, taxonomy, optionIndex: 1))
            .GetProperty("id").GetGuid();
        await search.Catalog.ActivateVariantAsync(charcoalVariant);
        await search.Catalog.OfferAsync((await search.DefaultSellerAsync()).Id, charcoalVariant, 999m);

        await search.DrainAsync();
        await search.WaitForRowAsync(beige.VariantId);
        await search.WaitForRowAsync(charcoalVariant);

        var rows = await Database.RowsAfterAsync(
            "SET enable_seqscan = off",
            "EXPLAIN SELECT id FROM search.product_search_projection "
            + $"WHERE attributes @> '{{\"{taxonomy.ColourCode}\":[\"beige\"]}}'::jsonb",
            Cancellation);

        var plan = string.Join('\n', rows.Select(row => row["QUERY PLAN"]?.ToString()));
        Assert.Contains("ix_product_search_projection_attributes", plan, StringComparison.Ordinal);

        var client = CreateClient();

        // OR within one attribute: both values of colour match, so both variants come back.
        var either = await ReadAsync(await client.GetAsync(
            new Uri(
                $"/api/v1/store/products?category={taxonomy.CategoryId}"
                + $"&attr.{taxonomy.ColourCode}=beige,charcoal",
                UriKind.Relative),
            Cancellation));

        Assert.Equal(2, either.GetProperty("total").GetInt64());

        // AND across two attributes: colour=beige AND (a code nothing carries) matches nothing.
        var neither = await ReadAsync(await client.GetAsync(
            new Uri(
                $"/api/v1/store/products?category={taxonomy.CategoryId}"
                + $"&attr.{taxonomy.ColourCode}=beige&attr.material=nonexistent-value",
                UriKind.Relative),
            Cancellation));

        Assert.Equal(0, neither.GetProperty("total").GetInt64());
    }

    /// <summary>
    /// Row 267: keyset pagination over the computed score neither repeats nor skips a row, including
    /// when two rows score identically and the id breaks the tie, on the ascending price sort.
    /// </summary>
    [Fact]
    public async Task Keyset_pagination_neither_repeats_nor_skips_a_row_including_a_price_tie()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var search = new SearchScenario(admin, Factory, Database, Cancellation);
        var taxonomy = await search.Catalog.TaxonomyAsync();

        var variantIds = new List<Guid>();

        // Five products at the same price, so the ascending price sort ties on every row and the id
        // is the only thing left to break it.
        for (var index = 0; index < 5; index++)
        {
            var product = await search.Catalog.DraftAsync(taxonomy);
            await search.Catalog.ActivateVariantAsync(product.VariantId);
            await search.Catalog.PublishAsync(product.Id);
            await search.Catalog.OfferAsync((await search.DefaultSellerAsync()).Id, product.VariantId, 500m);
            variantIds.Add(product.VariantId);
        }

        await search.DrainAsync();

        foreach (var id in variantIds)
        {
            await search.WaitForRowAsync(id);
        }

        var client = CreateClient();
        var seen = new List<Guid>();
        string? cursor = null;

        for (var page = 0; page < 10 && seen.Count < variantIds.Count; page++)
        {
            var uri = $"/api/v1/store/products?category={taxonomy.CategoryId}&sort=price-asc&size=2";

            if (cursor is not null)
            {
                uri += $"&cursor={Uri.EscapeDataString(cursor)}";
            }

            var response = await ReadAsync(await client.GetAsync(new Uri(uri, UriKind.Relative), Cancellation));

            foreach (var item in response.GetProperty("items").EnumerateArray())
            {
                var id = item.GetProperty("variantId").GetGuid();

                if (variantIds.Contains(id))
                {
                    Assert.DoesNotContain(id, seen);
                    seen.Add(id);
                }
            }

            var next = response.GetProperty("nextCursor");
            cursor = next.ValueKind == JsonValueKind.Null ? null : next.GetString();

            if (cursor is null)
            {
                break;
            }
        }

        Assert.Equal(variantIds.Count, seen.Count);
    }
}
