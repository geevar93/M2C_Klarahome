using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Step 19 rows about who may do what, the settings validator, output caching, and the one promise
/// that ties the storefront's two catalogue surfaces together: the buy box a search result shows is
/// the same offer the product page opens on.
/// </summary>
[Collection(KlaraHomeSchema.CollectionName)]
public sealed class SearchAuthorisationTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// Row 284: the three storefront routes are refused with 404 when their feature flags are off,
    /// and answer anonymously when they are on.
    /// </summary>
    [Fact]
    public async Task Storefront_routes_are_404_with_their_flag_off_and_answer_anonymously_when_on()
    {
        SkipWithoutDocker();

        var client = CreateClient();

        Factory.Features["search.faceted-browse"] = false;
        await RefusedAsync(
            await client.GetAsync(new Uri("/api/v1/store/products", UriKind.Relative), Cancellation),
            HttpStatusCode.NotFound);
        Factory.Features["search.faceted-browse"] = true;
        var browse = await client.GetAsync(new Uri("/api/v1/store/products", UriKind.Relative), Cancellation);
        Assert.Equal(HttpStatusCode.OK, browse.StatusCode);

        Factory.Features["search.suggestions"] = false;
        await RefusedAsync(
            await client.GetAsync(new Uri("/api/v1/store/search/suggest?q=a", UriKind.Relative), Cancellation),
            HttpStatusCode.NotFound);
        Factory.Features["search.suggestions"] = true;
        var suggest = await client.GetAsync(new Uri("/api/v1/store/search/suggest?q=a", UriKind.Relative), Cancellation);
        Assert.Equal(HttpStatusCode.OK, suggest.StatusCode);

        Factory.Features["search.query-logging"] = false;
        await RefusedAsync(
            await client.PostAsJsonAsync(
                "/api/v1/store/search/click",
                new { queryToken = (string?)null, position = 1, variantId = Guid.NewGuid() },
                Cancellation),
            HttpStatusCode.NotFound);
        Factory.Features["search.query-logging"] = true;

        var admin = await SignedInAdministratorAsync();
        var search = new SearchScenario(admin, Factory, Database, Cancellation);
        var taxonomy = await search.Catalog.TaxonomyAsync();
        var product = await search.Catalog.DraftAsync(taxonomy);
        await search.Catalog.ActivateVariantAsync(product.VariantId);
        await search.Catalog.PublishAsync(product.Id);
        await search.Catalog.OfferAsync((await search.DefaultSellerAsync()).Id, product.VariantId, 900m);
        await search.DrainAsync();
        await search.WaitForRowAsync(product.VariantId);

        var searched = await ReadAsync(await client.GetAsync(
            new Uri($"/api/v1/store/products?q={product.Sku}", UriKind.Relative),
            Cancellation));

        var token = searched.GetProperty("queryToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        var click = await client.PostAsJsonAsync(
            "/api/v1/store/search/click",
            new { queryToken = token, position = 1, variantId = product.VariantId },
            Cancellation);

        Assert.Equal(HttpStatusCode.NoContent, click.StatusCode);
    }

    /// <summary>
    /// Row 285: the three admin permissions are enforced — a caller without
    /// <c>search.vocabulary.manage</c> cannot edit a synonym, without <c>search.index.manage</c>
    /// cannot rebuild, and without <c>search.query.read</c> cannot read the log.
    /// </summary>
    [Fact]
    public async Task Every_admin_permission_is_enforced_on_its_own_route()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        // A vendor-owner holds none of the three: search merchandising and index operation are
        // platform-staff decisions, not a seller's own.
        var seller = await Sellers(admin).ActiveAsync();
        var (vendorClient, _) = await SignedInVendorOwnerAsync(admin, seller.Id);

        await RefusedAsync(
            await vendorClient.PostAsJsonAsync(
                "/api/v1/admin/search/synonyms",
                new { term = "sofa", expansions = new[] { "couch" }, isBidirectional = true, note = (string?)null },
                Cancellation),
            HttpStatusCode.Forbidden);

        await RefusedAsync(
            await vendorClient.PostAsJsonAsync(
                "/api/v1/admin/search/index/rebuild",
                new { afterVariantId = (Guid?)null, maxVariants = 1, variantIds = (Guid[]?)null },
                Cancellation),
            HttpStatusCode.Forbidden);

        await RefusedAsync(
            await vendorClient.GetAsync(new Uri("/api/v1/admin/search/queries", UriKind.Relative), Cancellation),
            HttpStatusCode.Forbidden);

        // The bootstrap administrator holds every permission and every one of these succeeds.
        var created = await admin.PostAsJsonAsync(
            "/api/v1/admin/search/synonyms",
            new { term = "permtest" + Guid.NewGuid().ToString("N")[..6], expansions = new[] { "permanswer" }, isBidirectional = false, note = (string?)null },
            Cancellation);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var rebuilt = await admin.PostAsJsonAsync(
            "/api/v1/admin/search/index/rebuild",
            new { afterVariantId = (Guid?)null, maxVariants = 1, variantIds = (Guid[]?)null },
            Cancellation);
        Assert.Equal(HttpStatusCode.OK, rebuilt.StatusCode);

        var queries = await admin.GetAsync(new Uri("/api/v1/admin/search/queries", UriKind.Relative), Cancellation);
        Assert.Equal(HttpStatusCode.OK, queries.StatusCode);
    }

    /// <summary>
    /// Row 286: the <c>search</c> settings section round-trips through
    /// <c>PUT /admin/settings/search</c>, and its validator refuses out-of-order price bands, an
    /// unknown default sort, and every weight at zero.
    /// </summary>
    [Fact]
    public async Task The_settings_validator_refuses_every_documented_bad_configuration()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        await RefusedAsync(await PutAsync(admin, priceBands: [999m, 499m]), HttpStatusCode.UnprocessableEntity);
        await RefusedAsync(await PutAsync(admin, defaultSort: "not-a-real-sort"), HttpStatusCode.UnprocessableEntity);
        await RefusedAsync(
            await PutAsync(admin, relevance: 0m, popularity: 0m, rating: 0m, availability: 0m),
            HttpStatusCode.UnprocessableEntity);

        try
        {
            var response = await ReadAsync(await PutAsync(admin, priceBands: [199m, 499m, 1499m]));
            Assert.Equal(3, response.GetProperty("value").GetProperty("priceBands").GetArrayLength());
            Assert.Equal(199m, response.GetProperty("value").GetProperty("priceBands")[0].GetDecimal());

            var read = await ReadAsync(await admin.GetAsync(
                new Uri("/api/v1/admin/settings", UriKind.Relative),
                Cancellation));

            var searchSection = read.GetProperty("sections").EnumerateArray()
                .First(section => section.GetProperty("key").GetString() == "search");

            Assert.Equal(199m, searchSection.GetProperty("value").GetProperty("priceBands")[0].GetDecimal());
        }
        finally
        {
            await ReadAsync(await PutAsync(admin, priceBands: [499m, 999m, 1999m, 4999m, 9999m]));
        }
    }

    /// <summary>
    /// Row 287: <c>GET /store/products</c> is served from the output cache for identical query
    /// strings and varies correctly by every filter parameter.
    /// </summary>
    [Fact]
    public async Task The_listing_page_is_cached_per_query_string_and_varies_by_filter()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var search = new SearchScenario(admin, Factory, Database, Cancellation);
        var taxonomy = await search.Catalog.TaxonomyAsync();

        var product = await search.Catalog.DraftAsync(taxonomy);
        await search.Catalog.ActivateVariantAsync(product.VariantId);
        await search.Catalog.PublishAsync(product.Id);
        var listingId = await search.Catalog.OfferAsync((await search.DefaultSellerAsync()).Id, product.VariantId, 400m);
        await search.DrainAsync();
        await search.WaitForRowAsync(product.VariantId);

        var client = CreateClient();
        var query = $"/api/v1/store/products?category={taxonomy.CategoryId}";

        var first = await ReadAsync(await client.GetAsync(new Uri(query, UriKind.Relative), Cancellation));
        Assert.Equal(1, first.GetProperty("total").GetInt64());

        // Withdraw the offer, and drain the event that would otherwise remove the row.
        await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/listings/{listingId}/deactivate",
                new { reason = "Step 19 test" },
                Cancellation),
            Cancellation);
        await search.DrainAsync();

        // The identical query string, immediately after: the cache — not a fresh read — is what
        // answers, so it still shows the withdrawn offer.
        var cached = await ReadAsync(await client.GetAsync(new Uri(query, UriKind.Relative), Cancellation));
        Assert.Equal(1, cached.GetProperty("total").GetInt64());

        // A different query string — one more filter — is a different cache key and reads fresh.
        var fresh = await ReadAsync(await client.GetAsync(
            new Uri($"{query}&sort=newest", UriKind.Relative),
            Cancellation));
        Assert.Equal(0, fresh.GetProperty("total").GetInt64());
    }

    /// <summary>
    /// Row 288: the buy box the search index holds is the same offer
    /// <c>GET /store/products/{slug}</c> shows, for a variant with several sellers.
    /// </summary>
    [Fact]
    public async Task The_search_index_buy_box_matches_the_product_pages_buy_box()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var search = new SearchScenario(admin, Factory, Database, Cancellation);
        var taxonomy = await search.Catalog.TaxonomyAsync();

        var sellerA = await Sellers(admin).ActiveAsync();
        var sellerB = await Sellers(admin).ActiveAsync();

        var product = await search.Catalog.DraftAsync(taxonomy, sellerA.Id);
        await search.Catalog.ActivateVariantAsync(product.VariantId);
        await search.Catalog.PublishAsync(product.Id);
        await search.Catalog.OfferAsync(sellerA.Id, product.VariantId, 1200m);
        var cheaperListing = await search.Catalog.OfferAsync(sellerB.Id, product.VariantId, 850m);

        await search.DrainAsync();
        var indexed = await search.WaitForRowAsync(product.VariantId);

        Assert.Equal(cheaperListing, indexed["listing_id"]);

        var page = await ReadAsync(await CreateClient().GetAsync(
            new Uri($"/api/v1/store/products/{product.Slug}", UriKind.Relative),
            Cancellation));

        var variant = page.GetProperty("variants").EnumerateArray()
            .First(candidate => candidate.GetProperty("id").GetGuid() == product.VariantId);

        var buyBox = variant.GetProperty("buyBox");

        Assert.Equal(cheaperListing, buyBox.GetProperty("listingId").GetGuid());
        Assert.Equal(850m, buyBox.GetProperty("sellingPrice").GetDecimal());
        Assert.Equal(sellerB.Id, buyBox.GetProperty("vendorId").GetGuid());
        Assert.Equal(Convert.ToDecimal(indexed["price"]), buyBox.GetProperty("sellingPrice").GetDecimal());
    }

    /// <summary>Builds a full, valid section body with one or two values overridden, for the validator tests.</summary>
    private static Task<HttpResponseMessage> PutAsync(
        HttpClient admin,
        IReadOnlyList<decimal>? priceBands = null,
        string? defaultSort = null,
        decimal? relevance = null,
        decimal? popularity = null,
        decimal? rating = null,
        decimal? availability = null)
        => admin.PutAsJsonAsync(
            "/api/v1/admin/settings/search",
            new
            {
                minimumQueryLength = 2,
                maxQueryLength = 120,
                defaultSort = defaultSort ?? "relevance",
                hideUnavailable = false,
                enableFuzzyFallback = true,
                fuzzyThreshold = 0.3m,
                relevanceWeight = relevance ?? 1.0m,
                popularityWeight = popularity ?? 0.35m,
                ratingWeight = rating ?? 0.15m,
                availabilityBoost = availability ?? 0.25m,
                maxFacetValues = 20,
                suggestionLimit = 8,
                priceBands = priceBands ?? [499m, 999m, 1999m, 4999m, 9999m],
                logQueries = true,
            },
            Cancellation);
}
