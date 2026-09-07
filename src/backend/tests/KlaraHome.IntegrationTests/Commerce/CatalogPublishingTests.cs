using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Step 10's own acceptance criterion and the money-and-visibility rules around it: a product goes
/// through moderation to the storefront, the mandatory disclosures are enforced, the buy box is
/// resolved from the database and the configured rule, and an offer's life is announced.
/// </summary>
/// <param name="fixture">The migrated database.</param>
public sealed class CatalogPublishingTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// The step's full acceptance criterion, end to end: "a variant with attributes, media and two
    /// competing vendor offers can be created, moderated, published and retrieved".
    /// </summary>
    /// <remarks>
    /// Every step of it through the API a person would use, and the retrieval through the
    /// storefront rather than the admin surface — a shopper reading the page is what "published"
    /// means, and the admin read would pass just as happily on a product no shopper can see.
    /// </remarks>
    [Fact]
    public async Task A_variant_with_attributes_media_and_two_offers_is_moderated_published_and_retrieved()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var first = await sellers.ActiveAsync();
        var second = await sellers.ActiveAsync();

        var (owner, _) = await SignedInVendorOwnerAsync(admin, first.Id);

        // Created by the seller, so the moderation half of the criterion is the real path and not
        // the straight-through one platform staff get.
        var product = await catalogue.DraftAsync(taxonomy, client: owner);

        var charcoal = await catalogue.VariantAsync(
            product.Id,
            taxonomy,
            optionIndex: 1,
            client: owner,
            mediaFileId: await catalogue.ImageAsync("charcoal.png"));

        var charcoalId = charcoal.GetProperty("id").GetGuid();

        await ReadAsync(await catalogue.SetProductMediaAsync(product.Id, await catalogue.ImageAsync("cover.png")));

        await ReadAsync(await catalogue.ActivateVariantAsync(product.VariantId, owner));
        await ReadAsync(await catalogue.ActivateVariantAsync(charcoalId, owner));

        // Moderated: the seller submits, the platform decides.
        var submitted = await ReadAsync(await catalogue.TransitionAsync(product.Id, "submit", client: owner));
        Assert.Equal("PendingApproval", submitted.GetProperty("status").GetString());

        var queue = await ReadAsync(await admin.GetAsync(
            new Uri("/api/v1/admin/product-moderation?status=Pending&size=100", UriKind.Relative),
            Cancellation));

        Assert.Contains(
            queue.GetProperty("items").EnumerateArray(),
            row => row.GetProperty("productId").GetGuid() == product.Id);

        var approved = await ReadAsync(
            await catalogue.TransitionAsync(product.Id, "approve", "Photography and disclosures check out."));

        Assert.Equal("Active", approved.GetProperty("status").GetString());

        // Two competing offers for the same variant. The second is opened by platform staff on the
        // other seller's behalf, which is the only way a seller who cannot see somebody else's
        // product still ends up offering against it.
        await catalogue.OfferAsync(null, product.VariantId, 999m, client: owner);
        await catalogue.OfferAsync(second.Id, product.VariantId, 949m);

        var page = await ReadAsync(await CreateClient().GetAsync(
            new Uri($"/api/v1/store/products/{product.Slug}", UriKind.Relative),
            Cancellation));

        Assert.Equal(product.Id, page.GetProperty("id").GetGuid());
        Assert.NotEmpty(page.GetProperty("media").EnumerateArray());

        var variants = page.GetProperty("variants").EnumerateArray().ToList();
        Assert.Equal(2, variants.Count);

        var beige = Assert.Single(variants, variant => variant.GetProperty("id").GetGuid() == product.VariantId);

        // Attributes: the defining combination came back as the swatch row it was created as.
        var option = Assert.Single(beige.GetProperty("options").EnumerateArray());
        Assert.Equal(taxonomy.ColourId, option.GetProperty("attributeId").GetGuid());
        Assert.Equal(taxonomy.ColourOptionIds[0], option.GetProperty("optionId").GetGuid());

        // Media: the charcoal variant carries its own gallery, distinct from the product's.
        var withMedia = Assert.Single(variants, variant => variant.GetProperty("id").GetGuid() == charcoalId);
        Assert.NotEmpty(withMedia.GetProperty("media").EnumerateArray());

        // Two offers, and the cheaper one holds the buy box.
        Assert.Equal(2, beige.GetProperty("offerCount").GetInt32());

        var buyBox = beige.GetProperty("buyBox");
        Assert.Equal(second.Id, buyBox.GetProperty("vendorId").GetGuid());
        Assert.Equal(949m, buyBox.GetProperty("sellingPrice").GetDecimal());
        Assert.True(buyBox.GetProperty("isBuyBox").GetBoolean());

        // And the mandatory disclosures a shopper is entitled to see are on the page.
        Assert.Equal("630222", page.GetProperty("hsnCode").GetString());
        Assert.Equal("IN", page.GetProperty("countryOfOrigin").GetString());
        Assert.False(string.IsNullOrWhiteSpace(page.GetProperty("manufacturer").GetProperty("name").GetString()));
        Assert.Equal("1 N", beige.GetProperty("netQuantity").GetString());
    }

    /// <summary>
    /// Publication is refused while any mandatory disclosure is missing, and the refusal names
    /// every gap rather than the first — product-level and variant-level together.
    /// </summary>
    /// <remarks>
    /// Naming them all is the difference between one round trip and six. The operator pressing
    /// "publish" is being told what to go and fix, and a rule that reveals the next omission only
    /// after the last one is corrected is a rule nobody finishes.
    /// </remarks>
    [Fact]
    public async Task Publication_is_refused_and_names_every_disclosure_gap_at_once()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var catalogue = new CatalogScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var product = await catalogue.DraftAsync(taxonomy, compliant: false);

        var problem = await RefusedAsync(
            await catalogue.TransitionAsync(product.Id, "submit"),
            HttpStatusCode.UnprocessableEntity,
            "CATALOG_COMPLIANCE_INCOMPLETE");

        var gaps = problem.GetProperty("errors").GetProperty("compliance").EnumerateArray()
            .Select(gap => gap.GetString() ?? string.Empty)
            .ToList();

        // Three on the product — HSN, country of origin, the manufacturer — and two on the variant,
        // its net quantity and its weight. All five, in one refusal.
        Assert.Contains(gaps, gap => gap.Contains("HSN", StringComparison.Ordinal));
        Assert.Contains(gaps, gap => gap.Contains("country of origin", StringComparison.Ordinal));
        Assert.Contains(gaps, gap => gap.Contains("manufacturer", StringComparison.Ordinal));
        Assert.Contains(gaps, gap => gap.Contains("net quantity", StringComparison.Ordinal));
        Assert.Contains(gaps, gap => gap.Contains("weight", StringComparison.Ordinal));

        // It really is still a draft: a refusal that had published it anyway would pass every
        // assertion above.
        var read = await ReadAsync(await admin.GetAsync(
            new Uri($"/api/v1/admin/products/{product.Id}", UriKind.Relative),
            Cancellation));

        Assert.Equal("Draft", read.GetProperty("status").GetString());

        // A product with no variant at all is refused for a different, named reason.
        var empty = await ReadAsync(await admin.PostAsJsonAsync(
            "/api/v1/admin/products",
            new
            {
                name = $"Empty product {Guid.NewGuid():N}"[..24],
                slug = (string?)null,
                categoryId = taxonomy.CategoryId,
                brandId = (Guid?)null,
                vendorId = (Guid?)null,
                shortDescription = (string?)null,
                description = (string?)null,
                hsnCode = "630222",
                gstRate = 5m,
                countryOfOrigin = "IN",
                manufacturer = new { name = "Klara Textiles Pvt Ltd", address = "Panipat", contact = (string?)null },
                packer = (object?)null,
                importer = (object?)null,
                isReturnable = true,
                returnWindowDays = 7,
                warranty = (string?)null,
                specifications = Array.Empty<object>(),
                seo = (object?)null,
                attributes = Array.Empty<object>(),
            },
            Cancellation));

        await RefusedAsync(
            await catalogue.TransitionAsync(empty.GetProperty("id").GetGuid(), "publish"),
            HttpStatusCode.UnprocessableEntity,
            "CATALOG_NOT_PUBLISHABLE");
    }

    /// <summary>
    /// The buy box is resolved from the offers in the database, the sellers behind them, and the
    /// rule held in store settings — not from a rule compiled into the code.
    /// </summary>
    /// <remarks>
    /// The resolver itself is unit-tested and is a pure function. What was never proved is the half
    /// around it: that the candidates are loaded, that the sellers are resolved through
    /// <c>IVendorDirectory</c>, and that the criteria are read from the settings section rather
    /// than assumed. Changing the rule so that the <em>more expensive</em> offer wins is the only
    /// assertion that can tell those apart, and the section is put back afterwards because the
    /// collection shares one database.
    /// </remarks>
    [Fact]
    public async Task The_buy_box_is_resolved_from_the_database_and_the_configured_rule()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var cheap = await sellers.ActiveAsync();
        var quick = await sellers.ActiveAsync();

        var product = await catalogue.DraftAsync(taxonomy);
        await ReadAsync(await catalogue.ActivateVariantAsync(product.VariantId));
        await catalogue.PublishAsync(product.Id);

        // Cheaper but slower, against dearer but faster. Under either rule exactly one of them
        // wins, and they are not the same one.
        await catalogue.OfferAsync(cheap.Id, product.VariantId, 899m, handlingTimeHours: 72);
        await catalogue.OfferAsync(quick.Id, product.VariantId, 999m, handlingTimeHours: 4);

        Assert.Equal(cheap.Id, await BuyBoxSellerAsync(product.Slug, product.VariantId));

        var original = await ReadAsync(await admin.GetAsync(
            new Uri("/api/v1/admin/settings", UriKind.Relative),
            Cancellation));

        var before = Assert.Single(
            original.GetProperty("sections").EnumerateArray(),
            section => section.GetProperty("key").GetString() == "buy-box");

        try
        {
            await ReadAsync(await admin.PutAsJsonAsync(
                "/api/v1/admin/settings/buy-box",
                new { criteria = new[] { "dispatch-sla" }, treatUnratedAsAverage = true },
                Cancellation));

            Assert.Equal(quick.Id, await BuyBoxSellerAsync(product.Slug, product.VariantId));
        }
        finally
        {
            await ReadAsync(await admin.PutAsJsonAsync(
                "/api/v1/admin/settings/buy-box",
                before.GetProperty("value"),
                Cancellation));
        }

        // Back to the shipped rule, and back to the cheaper seller.
        Assert.Equal(cheap.Id, await BuyBoxSellerAsync(product.Slug, product.VariantId));

        // The "other sellers" panel is the same comparison, not a second one that agrees by
        // accident: it is the ranking with its head still on.
        var offers = await ReadAsync(await CreateClient().GetAsync(
            new Uri($"/api/v1/store/products/{product.Slug}/offers?variantId={product.VariantId}", UriKind.Relative),
            Cancellation));

        var ranked = offers.EnumerateArray().ToList();

        Assert.Equal(2, ranked.Count);
        Assert.Equal(cheap.Id, ranked[0].GetProperty("vendorId").GetGuid());
        Assert.True(ranked[0].GetProperty("isBuyBox").GetBoolean());
        Assert.False(ranked[1].GetProperty("isBuyBox").GetBoolean());

        // And the seller's own facts came from the Vendors module rather than from this schema,
        // which holds nothing but an id.
        Assert.False(string.IsNullOrWhiteSpace(ranked[0].GetProperty("vendorName").GetString()));
        Assert.Equal(cheap.Slug, ranked[0].GetProperty("vendorSlug").GetString());
    }

    /// <summary>
    /// An offer going live, changing its terms and being withdrawn each land in the outbox, in the
    /// transaction that changed the offer.
    /// </summary>
    /// <remarks>
    /// From the keyed per-context outbox: enqueuing through the unkeyed registration would add the
    /// row to whichever module registered first, this module's save would not write it, and the
    /// event would vanish with no error anywhere. A refused transition writing no event is the
    /// other half of the same proof.
    /// </remarks>
    [Fact]
    public async Task The_offer_lifecycle_events_are_written_to_the_outbox_with_the_state_change()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await sellers.ActiveAsync();

        var product = await catalogue.DraftAsync(taxonomy);
        await ReadAsync(await catalogue.ActivateVariantAsync(product.VariantId));
        await catalogue.PublishAsync(product.Id);

        var listingId = await catalogue.OfferAsync(seller.Id, product.VariantId, 899m, activate: false);

        // Opening an offer is not news: nothing downstream indexes a draft.
        Assert.Equal(0, await EventsAbout(listingId, "ListingPublished"));

        await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/listings/{listingId}/activate",
            new { reason = (string?)null },
            Cancellation));

        Assert.Equal(1, await EventsAbout(listingId, "ListingPublished"));

        await ReadAsync(await admin.PutAsJsonAsync(
            $"/api/v1/admin/listings/{listingId}",
            new
            {
                mrp = 1299m,
                sellingPrice = 849m,
                vendorSku = "KL-CUSH-40",
                handlingTimeHours = 12,
                isCodAllowed = true,
                maxOrderQuantity = (int?)null,
            },
            Cancellation));

        Assert.Equal(1, await EventsAbout(listingId, "ListingUpdated"));

        await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/listings/{listingId}/deactivate",
            new { reason = "Out of season." },
            Cancellation));

        Assert.Equal(1, await EventsAbout(listingId, "ListingDeactivated"));

        // A refused move announces nothing. An offer that is already inactive cannot be
        // deactivated again, and the outbox must not carry an event for a change that never
        // happened.
        await RefusedAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/listings/{listingId}/deactivate",
                new { reason = "Again." },
                Cancellation),
            HttpStatusCode.Conflict,
            "CATALOG_INVALID_TRANSITION");

        Assert.Equal(1, await EventsAbout(listingId, "ListingDeactivated"));
    }

    /// <summary>
    /// Suspending a seller actually takes their live offers off the storefront, and a redelivery of
    /// the same event does nothing further.
    /// </summary>
    /// <remarks>
    /// "A Listing cannot be Active if its Vendor is not Active" is an invariant no query can
    /// maintain: Vendors owns the seller's status, Catalog owns the offers, and the event is the
    /// only join between them. Delivery is at-least-once, so the second half is not optional —
    /// this test redelivers the very row the first half consumed.
    /// </remarks>
    [Fact]
    public async Task A_suspended_sellers_live_offers_are_withdrawn_and_a_redelivery_changes_nothing()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await sellers.ActiveAsync();

        var product = await catalogue.DraftAsync(taxonomy);
        await ReadAsync(await catalogue.ActivateVariantAsync(product.VariantId));
        await catalogue.PublishAsync(product.Id);

        var listingId = await catalogue.OfferAsync(seller.Id, product.VariantId, 899m);

        Assert.Equal("Active", await ListingStatusAsync(admin, listingId));

        await ReadAsync(await sellers.TransitionAsync(seller.Id, "suspend", "Repeated late dispatch."));

        // Still live: the seller's status changed, and nothing has dispatched the event yet.
        Assert.Equal("Active", await ListingStatusAsync(admin, listingId));

        await OutboxDrain.RunAsync(Factory, Database, Cancellation);

        Assert.Equal("Inactive", await ListingStatusAsync(admin, listingId));
        Assert.Equal(1, await EventsAbout(listingId, "ListingDeactivated"));

        // The offer is off the storefront too, which is what the shopper-visible half of the
        // invariant actually means.
        var offers = await ReadAsync(await CreateClient().GetAsync(
            new Uri($"/api/v1/store/products/{product.Slug}/offers?variantId={product.VariantId}", UriKind.Relative),
            Cancellation));

        Assert.Empty(offers.EnumerateArray());

        // The redelivery: the same row, put back on the queue exactly as an at-least-once broker
        // would replay it.
        var replayed = await Database.ExecuteAsync(
            "UPDATE platform.outbox_messages SET processed_at = NULL, attempts = 0 "
            + "WHERE type LIKE '%VendorSuspended%' AND payload::text LIKE $1",
            Cancellation,
            $"%{seller.Id}%");

        Assert.Equal(1, replayed);

        await OutboxDrain.RunAsync(Factory, Database, Cancellation);

        Assert.Equal("Inactive", await ListingStatusAsync(admin, listingId));
        Assert.Equal(1, await EventsAbout(listingId, "ListingDeactivated"));
    }

    /// <summary>The seller holding the buy box for one variant, read from the product page.</summary>
    /// <param name="slug">The product's URL segment.</param>
    /// <param name="variantId">The variant.</param>
    private async Task<Guid> BuyBoxSellerAsync(string slug, Guid variantId)
    {
        var page = await ReadAsync(await CreateClient().GetAsync(
            new Uri($"/api/v1/store/products/{slug}", UriKind.Relative),
            Cancellation));

        var variant = Assert.Single(
            page.GetProperty("variants").EnumerateArray(),
            candidate => candidate.GetProperty("id").GetGuid() == variantId);

        return variant.GetProperty("buyBox").GetProperty("vendorId").GetGuid();
    }

    /// <summary>Where an offer is in its life, as the admin surface reports it.</summary>
    /// <param name="admin">A client signed in as platform staff.</param>
    /// <param name="listingId">The offer.</param>
    private static async Task<string?> ListingStatusAsync(HttpClient admin, Guid listingId)
    {
        var listing = await ReadAsync(await admin.GetAsync(
            new Uri($"/api/v1/admin/listings/{listingId}", UriKind.Relative),
            Cancellation));

        return listing.GetProperty("status").GetString();
    }

    /// <summary>How many events of a kind the outbox holds about one offer.</summary>
    /// <param name="listingId">The offer.</param>
    /// <param name="eventType">The contract type's name.</param>
    private async Task<long> EventsAbout(Guid listingId, string eventType)
        => await Database.CountAsync(
            "SELECT COUNT(*) FROM platform.outbox_messages WHERE type LIKE $1 AND payload::text LIKE $2",
            Cancellation,
            $"%{eventType}%",
            $"%{listingId}%");
}
