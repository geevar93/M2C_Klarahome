using System.Net.Http.Json;
using KlaraHome.Contracts.Inventory;
using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Pricing;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Step 19 rows about keeping the index current: the fuzzy fallback, and the six event handlers in
/// <c>SearchProjectionHandlers</c>.
/// </summary>
[Collection(KlaraHomeSchema.CollectionName)]
public sealed class SearchEventTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// Row 270: the fuzzy fallback runs only once the exact pass matched nothing, corrects a real
    /// misspelling, and honours the store's configured threshold through <c>set_limit</c>.
    /// </summary>
    [Fact]
    public async Task The_fuzzy_pass_runs_only_after_an_exact_miss_and_corrects_a_misspelling()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var search = new SearchScenario(admin, Factory, Database, Cancellation);
        var taxonomy = await search.Catalog.TaxonomyAsync();

        var token = "Cushion" + Guid.NewGuid().ToString("N")[..6];
        var product = await search.Catalog.DraftAsync(taxonomy, name: $"{token} Cover");
        await search.Catalog.ActivateVariantAsync(product.VariantId);
        await search.Catalog.PublishAsync(product.Id);
        await search.Catalog.OfferAsync((await search.DefaultSellerAsync()).Id, product.VariantId, 999m);
        await search.DrainAsync();
        await search.WaitForRowAsync(product.VariantId);

        var client = CreateClient();

        // The exact pass matches the token itself.
        var exact = await ReadAsync(await client.GetAsync(
            new Uri($"/api/v1/store/products?q={token}", UriKind.Relative),
            Cancellation));

        Assert.False(exact.GetProperty("corrected").GetBoolean());
        Assert.True(exact.GetProperty("total").GetInt64() > 0);

        // A real misspelling — one letter dropped — matches no lexeme at all on the exact pass, so
        // the fuzzy trigram pass has to be the one that found it.
        var misspelled = token.Remove(token.Length - 4, 1);

        var fuzzy = await ReadAsync(await client.GetAsync(
            new Uri($"/api/v1/store/products?q={misspelled}", UriKind.Relative),
            Cancellation));

        Assert.True(fuzzy.GetProperty("corrected").GetBoolean());
        Assert.Contains(
            fuzzy.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("variantId").GetGuid() == product.VariantId);

        // Nothing similar enough to anything in the catalogue: the fuzzy pass runs and still finds
        // nothing, rather than the exact pass alone deciding that.
        var nothing = await ReadAsync(await client.GetAsync(
            new Uri("/api/v1/store/products?q=zzzzznonwordzzzzz", UriKind.Relative),
            Cancellation));

        Assert.False(nothing.GetProperty("corrected").GetBoolean());
        Assert.Equal(0, nothing.GetProperty("total").GetInt64());
    }

    /// <summary>
    /// Row 271: <c>ListingPublished</c>, <c>ListingUpdated</c> (via a second, cheaper offer
    /// activating), <c>ListingDeactivated</c> and <c>PriceChanged</c> each re-resolve the buy box, so
    /// a change that changes the winner changes the row's seller and price together.
    /// </summary>
    [Fact]
    public async Task Listing_and_price_events_each_re_resolve_the_buy_box()
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
        var listingA = await search.Catalog.OfferAsync(sellerA.Id, product.VariantId, 1000m);
        await search.DrainAsync();

        // ListingPublished: the row exists, and the only offer wins.
        var afterPublish = await search.WaitForRowAsync(product.VariantId);
        Assert.Equal(listingA, afterPublish["listing_id"]);
        Assert.Equal(sellerA.Id, afterPublish["vendor_id"]);
        Assert.Equal(1000m, Convert.ToDecimal(afterPublish["price"]));

        // A cheaper competing offer activating is ListingPublished for the new listing — it re-
        // resolves the whole variant, so the row changes hands to whichever offer wins the rule.
        var listingB = await search.Catalog.OfferAsync(sellerB.Id, product.VariantId, 700m);
        await search.DrainAsync();

        var afterCompetitor = await search.WaitForRowAsync(product.VariantId);
        Assert.Equal(listingB, afterCompetitor["listing_id"]);
        Assert.Equal(sellerB.Id, afterCompetitor["vendor_id"]);
        Assert.Equal(700m, Convert.ToDecimal(afterCompetitor["price"]));

        // ListingDeactivated: withdrawing the winner hands the row back to the surviving offer.
        await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/listings/{listingB}/deactivate",
                new { reason = "Step 19 test" },
                Cancellation),
            Cancellation);
        await search.DrainAsync();

        var afterWithdrawal = await search.WaitForRowAsync(product.VariantId);
        Assert.Equal(listingA, afterWithdrawal["listing_id"]);
        Assert.Equal(sellerA.Id, afterWithdrawal["vendor_id"]);

        // PriceChanged: the seam this suite reaches directly, exactly as Step 18 reached
        // IOrderSettlement — the real, unmodified handler re-resolves the variant from the catalogue,
        // which now prices listing A at 250, changing the row's price without another listing event.
        var putResponse = await Rest.ReadAsync(
            await admin.PutAsJsonAsync(
                $"/api/v1/admin/listings/{listingA}",
                new { mrp = 1299m, sellingPrice = 250m, vendorSku = (string?)null, handlingTimeHours = 24, isCodAllowed = true, maxOrderQuantity = (int?)null },
                Cancellation),
            Cancellation);
        Assert.Equal(250m, putResponse.GetProperty("sellingPrice").GetDecimal());

        await search.DispatchAsync(new PriceChanged(listingA, sellerA.Id, 1000m, 250m, "INR", null));

        var afterPriceChange = await search.RowAsync(product.VariantId);
        Assert.NotNull(afterPriceChange);
        Assert.Equal(250m, Convert.ToDecimal(afterPriceChange!["price"]));
    }

    /// <summary>
    /// Row 272: <c>StockLevelChanged</c> updates only the row's two availability columns, and touches
    /// nothing when the listing named is not the current buy-box winner.
    /// </summary>
    [Fact]
    public async Task Stock_events_touch_only_the_availability_columns_of_the_winning_row()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var search = new SearchScenario(admin, Factory, Database, Cancellation);
        var taxonomy = await search.Catalog.TaxonomyAsync();

        var seller = await Sellers(admin).ActiveAsync();
        var product = await search.Catalog.DraftAsync(taxonomy, seller.Id);
        await search.Catalog.ActivateVariantAsync(product.VariantId);
        await search.Catalog.PublishAsync(product.Id);
        var listingId = await search.Catalog.OfferAsync(seller.Id, product.VariantId, 1000m);
        await search.DrainAsync();

        var before = await search.WaitForRowAsync(product.VariantId);
        Assert.False((bool)before["is_available"]!);
        Assert.Equal(0, Convert.ToInt32(before["quantity_available"]));

        await search.Catalog.StockAsync(listingId, quantity: 5, seller.Id);
        await search.DrainAsync();

        var after = await search.RowAsync(product.VariantId);
        Assert.NotNull(after);
        Assert.True((bool)after!["is_available"]!);
        Assert.Equal(5, Convert.ToInt32(after["quantity_available"]));

        // Everything else on the row is untouched — the price is exactly what it was, proving this
        // was the two-column update and not a re-resolve.
        Assert.Equal(Convert.ToDecimal(before["price"]), Convert.ToDecimal(after["price"]));
        Assert.Equal(before["listing_id"], after["listing_id"]);

        // A stock movement against a listing that is not the current buy-box winner matches no row:
        // a second, losing offer's stock changing must not touch the winner's row at all.
        var loser = await Sellers(admin).ActiveAsync();
        var losingListing = await search.Catalog.OfferAsync(loser.Id, product.VariantId, 1250m);
        await search.DrainAsync();
        await search.WaitForRowAsync(product.VariantId);

        await search.Catalog.StockAsync(losingListing, quantity: 9, loser.Id);
        await search.DrainAsync();

        var afterLoserStock = await search.RowAsync(product.VariantId);
        Assert.NotNull(afterLoserStock);
        Assert.Equal(listingId, afterLoserStock!["listing_id"]); // still the winner
        Assert.Equal(5, Convert.ToInt32(afterLoserStock["quantity_available"])); // unchanged
    }

    /// <summary>
    /// Row 273: <c>SubOrderConfirmed</c> counts units once and only once — a redelivered event finds
    /// its inbox row and adds nothing, and a concurrent redelivery fails the primary key rather than
    /// double-counting.
    /// </summary>
    [Fact]
    public async Task SubOrderConfirmed_counts_units_exactly_once_even_when_redelivered()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var search = new SearchScenario(admin, Factory, Database, Cancellation);
        var taxonomy = await search.Catalog.TaxonomyAsync();

        var seller = await Sellers(admin).ActiveAsync();
        var product = await search.Catalog.DraftAsync(taxonomy, seller.Id);
        await search.Catalog.ActivateVariantAsync(product.VariantId);
        await search.Catalog.PublishAsync(product.Id);
        var listingId = await search.Catalog.OfferAsync(seller.Id, product.VariantId, 1000m);
        await search.DrainAsync();
        await search.WaitForRowAsync(product.VariantId);

        var confirmed = new SubOrderConfirmed(
            Guid.CreateVersion7(),
            "ORD-TEST-1",
            Guid.CreateVersion7(),
            "SUB-TEST-1",
            seller.Id,
            Guid.CreateVersion7(),
            1000m,
            "INR",
            null,
            [new OrderLineFact(Guid.CreateVersion7(), listingId, product.Sku, 3, 3000m)]);

        await search.DispatchAsync(confirmed);

        var afterFirst = await search.RowAsync(product.VariantId);
        Assert.NotNull(afterFirst);
        Assert.Equal(3, Convert.ToInt64(afterFirst!["units_sold"]));
        var popularityAfterFirst = Convert.ToDecimal(afterFirst["popularity_score"]);
        Assert.True(popularityAfterFirst > 0m);

        // Redelivered with the same EventId: the inbox row it wrote the first time is found, and
        // nothing is added a second time.
        await search.DispatchAsync(confirmed);

        var afterRedelivery = await search.RowAsync(product.VariantId);
        Assert.NotNull(afterRedelivery);
        Assert.Equal(3, Convert.ToInt64(afterRedelivery!["units_sold"]));
        Assert.Equal(popularityAfterFirst, Convert.ToDecimal(afterRedelivery["popularity_score"]));

        // A genuinely concurrent redelivery — a fresh event nothing has counted yet, dispatched
        // twice at once — fails the primary key on the inbox row rather than double-counting: two
        // scopes race to insert the same (MessageId, Handler) row, and only one commit can win.
        var raced = confirmed with
        {
            EventId = Guid.CreateVersion7(),
            Lines = [new OrderLineFact(Guid.CreateVersion7(), listingId, product.Sku, 4, 4000m)],
        };

        var first = search.DispatchAsync(raced);
        var second = search.DispatchAsync(raced);

        var outcomes = await Task.WhenAll(
            first.ContinueWith(task => task.Exception, TaskScheduler.Default),
            second.ContinueWith(task => task.Exception, TaskScheduler.Default));

        Assert.Single(outcomes, exception => exception is not null);

        var afterConcurrent = await search.RowAsync(product.VariantId);
        Assert.NotNull(afterConcurrent);

        // Counted exactly once: 3 from the first dispatch above, plus 4 from whichever of the two
        // racing dispatches actually won — never 3 + 4 + 4.
        Assert.Equal(7, Convert.ToInt64(afterConcurrent!["units_sold"]));
    }

    /// <summary>
    /// Row 274: a variant whose every offer is withdrawn is deactivated rather than deleted, keeps
    /// its <c>units_sold</c>, and returns to results with its popularity intact when an offer comes
    /// back.
    /// </summary>
    [Fact]
    public async Task A_variant_with_no_surviving_offer_is_deactivated_not_deleted()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var search = new SearchScenario(admin, Factory, Database, Cancellation);
        var taxonomy = await search.Catalog.TaxonomyAsync();

        var seller = await Sellers(admin).ActiveAsync();
        var product = await search.Catalog.DraftAsync(taxonomy, seller.Id);
        await search.Catalog.ActivateVariantAsync(product.VariantId);
        await search.Catalog.PublishAsync(product.Id);
        var listingId = await search.Catalog.OfferAsync(seller.Id, product.VariantId, 1000m);
        await search.DrainAsync();
        await search.WaitForRowAsync(product.VariantId);

        // Give it some sales history to lose, if the write were wrong.
        var confirmed = new SubOrderConfirmed(
            Guid.CreateVersion7(),
            "ORD-TEST-2",
            Guid.CreateVersion7(),
            "SUB-TEST-2",
            seller.Id,
            Guid.CreateVersion7(),
            2000m,
            "INR",
            null,
            [new OrderLineFact(Guid.CreateVersion7(), listingId, product.Sku, 7, 7000m)]);

        await search.DispatchAsync(confirmed);

        var withSales = await search.RowAsync(product.VariantId);
        var unitsSold = Convert.ToInt64(withSales!["units_sold"]);
        var popularity = Convert.ToDecimal(withSales["popularity_score"]);
        Assert.Equal(7, unitsSold);

        // Every offer withdrawn: the row is retired, not removed.
        await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/listings/{listingId}/deactivate",
                new { reason = "Step 19 test" },
                Cancellation),
            Cancellation);
        await search.DrainAsync();

        var deactivated = await search.RowAsync(product.VariantId);
        Assert.NotNull(deactivated);
        Assert.False((bool)deactivated!["is_active"]!);
        Assert.Equal(unitsSold, Convert.ToInt64(deactivated["units_sold"]));

        // An offer comes back: the row returns to results with its popularity intact.
        await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/listings/{listingId}/activate",
                new { reason = (string?)null },
                Cancellation),
            Cancellation);
        await search.DrainAsync();

        var revived = await search.RowAsync(product.VariantId);
        Assert.NotNull(revived);
        Assert.True((bool)revived!["is_active"]!);
        Assert.Equal(unitsSold, Convert.ToInt64(revived["units_sold"]));
        Assert.Equal(popularity, Convert.ToDecimal(revived["popularity_score"]));
    }
}
