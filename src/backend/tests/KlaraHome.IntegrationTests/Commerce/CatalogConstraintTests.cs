using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using KlaraHome.IntegrationTests.Database;
using Npgsql;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Step 10's remaining criteria: the tree the materialised path maintains, the indexes that hold
/// when two requests race, the <c>CHECK</c> constraints behind them, and the generated search
/// vector.
/// </summary>
/// <remarks>
/// These are the rows the build sprint could not close without an engine. A path rewrite over a
/// subtree, a partial unique index under concurrency and a stored generated <c>tsvector</c> all
/// behave perfectly in memory and only differ against PostgreSQL.
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class CatalogConstraintTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// Moving a category rewrites every descendant's path and level, not only the moved node's.
    /// </summary>
    /// <remarks>
    /// The path is built from ids and is what makes "everything under Home &amp; Kitchen" an index
    /// range scan instead of a recursive CTE on every listing page. A move that rewrote only the
    /// node itself would leave its children pointing at a prefix that no longer describes where
    /// they are, and the browse query would quietly return the wrong products.
    /// </remarks>
    [Fact]
    public async Task Moving_a_category_rewrites_every_descendants_path_and_level()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var catalogue = new CatalogScenario(admin, Cancellation);

        var root = await catalogue.CategoryAsync("Home");
        var middle = await catalogue.CategoryAsync("Soft furnishing", root.Id);
        var leaf = await catalogue.CategoryAsync("Cushions", middle.Id);

        var elsewhere = await catalogue.CategoryAsync("Outdoor");

        Assert.Equal(0, root.Level);
        Assert.Equal(2, leaf.Level);
        Assert.StartsWith(root.Path, leaf.Path, StringComparison.Ordinal);

        await ReadAsync(await catalogue.MoveCategoryAsync(middle.Id, elsewhere.Id, "Soft furnishing"));

        var movedMiddle = await catalogue.ReadCategoryAsync(middle.Id);
        var movedLeaf = await catalogue.ReadCategoryAsync(leaf.Id);

        Assert.Equal(elsewhere.Id, movedMiddle.ParentId);
        Assert.Equal(1, movedMiddle.Level);
        Assert.StartsWith(elsewhere.Path, movedMiddle.Path, StringComparison.Ordinal);

        // The descendant is the assertion that matters: it was never named in the request.
        Assert.Equal(2, movedLeaf.Level);
        Assert.StartsWith(movedMiddle.Path, movedLeaf.Path, StringComparison.Ordinal);
        Assert.DoesNotContain(root.Path, movedLeaf.Path, StringComparison.Ordinal);
    }

    /// <summary>A category cannot be moved beneath one of its own descendants.</summary>
    /// <remarks>
    /// A cycle in a materialised-path tree is not a slow query, it is a subtree that no longer
    /// terminates: the descendant rewrite would chase its own prefix.
    /// </remarks>
    [Fact]
    public async Task A_category_cannot_be_moved_beneath_its_own_descendant()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var catalogue = new CatalogScenario(admin, Cancellation);

        var root = await catalogue.CategoryAsync("Bedroom");
        var child = await catalogue.CategoryAsync("Bedding", root.Id);
        var grandchild = await catalogue.CategoryAsync("Duvets", child.Id);

        await RefusedAsync(
            await catalogue.MoveCategoryAsync(root.Id, grandchild.Id, "Bedroom"),
            HttpStatusCode.UnprocessableEntity,
            "CATALOG_CATEGORY_CYCLE");

        // Under itself is the degenerate case of the same rule.
        await RefusedAsync(
            await catalogue.MoveCategoryAsync(root.Id, root.Id, "Bedroom"),
            HttpStatusCode.UnprocessableEntity,
            "CATALOG_CATEGORY_CYCLE");

        var unmoved = await catalogue.ReadCategoryAsync(root.Id);
        Assert.Null(unmoved.ParentId);
    }

    /// <summary>
    /// The depth cap is applied to the whole subtree being moved, not only to the node named in the
    /// request.
    /// </summary>
    /// <remarks>
    /// This is the half that is easy to miss. A node two levels from the bottom of the tree fits
    /// under a deep parent perfectly well on its own; it is its grandchildren that would land past
    /// the limit, and they are rows the request never mentions.
    /// </remarks>
    [Fact]
    public async Task The_depth_cap_holds_for_the_whole_subtree_being_moved()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var catalogue = new CatalogScenario(admin, Cancellation);

        // Six levels is the limit, so a chain of six exists and a seventh is refused outright.
        var chain = new List<(Guid Id, string Path, int Level)>();
        Guid? parent = null;

        for (var depth = 0; depth < 6; depth++)
        {
            var created = await catalogue.CategoryAsync($"Level {depth}", parent);

            Assert.Equal(depth, created.Level);
            chain.Add((created.Id, created.Path, created.Level));
            parent = created.Id;
        }

        await RefusedAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/categories",
                new
                {
                    parentId = chain[^1].Id,
                    name = "One too deep",
                    slug = (string?)null,
                    description = (string?)null,
                    imageFileId = (Guid?)null,
                    attributeSetId = (Guid?)null,
                    position = 0,
                    isActive = true,
                    seo = (object?)null,
                },
                Cancellation),
            HttpStatusCode.UnprocessableEntity,
            "CATALOG_CATEGORY_TOO_DEEP");

        // A two-level subtree of its own, standing at the root.
        var subtreeRoot = await catalogue.CategoryAsync("Subtree root");
        var subtreeLeaf = await catalogue.CategoryAsync("Subtree leaf", subtreeRoot.Id);

        // Its own new level would be 4, which fits. Its leaf's would be 5, which also fits.
        await ReadAsync(await catalogue.MoveCategoryAsync(subtreeRoot.Id, chain[3].Id, "Subtree root"));
        Assert.Equal(5, (await catalogue.ReadCategoryAsync(subtreeLeaf.Id)).Level);

        // One level further and the moved node would still fit at 5 — its leaf would not.
        await RefusedAsync(
            await catalogue.MoveCategoryAsync(subtreeRoot.Id, chain[4].Id, "Subtree root"),
            HttpStatusCode.UnprocessableEntity,
            "CATALOG_CATEGORY_TOO_DEEP");

        Assert.Equal(4, (await catalogue.ReadCategoryAsync(subtreeRoot.Id)).Level);
    }

    /// <summary>
    /// The unique indexes hold when several requests race: one offer per seller and variant, one
    /// variant per combination of options, one open moderation submission per product.
    /// </summary>
    /// <remarks>
    /// Each of the three has a pre-check in its handler, and a pre-check is not a guarantee — two
    /// requests read "no clash" a microsecond apart and both proceed. The index is the guarantee,
    /// and the only way to show it is doing anything is to make the race happen.
    /// </remarks>
    [Fact]
    public async Task The_unique_indexes_hold_when_requests_race()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await sellers.ActiveAsync();
        var (owner, _) = await SignedInVendorOwnerAsync(admin, seller.Id);

        using (owner)
        {
            var product = await catalogue.DraftAsync(taxonomy, client: owner);

            // One variant per (product, attribute hash). Five racing requests, all naming the same
            // colour, against a product that already has the other one.
            var variantRace = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
                owner.PostAsJsonAsync(
                    $"/api/v1/admin/products/{product.Id}/variants",
                    new
                    {
                        sku = (string?)null,
                        barcode = (string?)null,
                        nameSuffix = "Charcoal",
                        mrp = 1299m,
                        netQuantity = "1 N",
                        shelfLifeDays = (int?)null,
                        expiresOn = (DateOnly?)null,
                        weightGrams = 220,
                        lengthMm = 400,
                        widthMm = 400,
                        heightMm = 20,
                        position = 1,
                        isDefault = false,
                        options = new[]
                        {
                            new { attributeId = taxonomy.ColourId, optionId = taxonomy.ColourOptionIds[1] },
                        },
                        media = (object?)null,
                    },
                    Cancellation)));

            Assert.Equal(1, variantRace.Count(response => response.IsSuccessStatusCode));

            var variants = await Database.CountAsync(
                "SELECT COUNT(*) FROM catalog.variants WHERE product_id = $1 AND deleted_at IS NULL",
                Cancellation,
                product.Id);

            Assert.Equal(2, variants);

            // One open submission per product. The submissions race the same way, and a second
            // pending row would let one moderator approve while another rejects.
            await ReadAsync(await catalogue.ActivateVariantAsync(product.VariantId, owner));

            var submitRace = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
                owner.PostAsJsonAsync(
                    $"/api/v1/admin/products/{product.Id}/submit",
                    new { },
                    Cancellation)));

            Assert.Equal(1, submitRace.Count(response => response.IsSuccessStatusCode));

            var pending = await Database.CountAsync(
                "SELECT COUNT(*) FROM catalog.product_moderations WHERE product_id = $1 AND status = 'Pending'",
                Cancellation,
                product.Id);

            Assert.Equal(1, pending);

            await ReadAsync(await catalogue.TransitionAsync(product.Id, "approve", "Fine."));

            // One offer per (seller, variant). Five racing requests from the same seller for the
            // same variant; a second row would put one seller in the buy box twice.
            var offerRace = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
                owner.PostAsJsonAsync(
                    "/api/v1/admin/listings",
                    new
                    {
                        variantId = product.VariantId,
                        vendorId = (Guid?)null,
                        mrp = (decimal?)null,
                        sellingPrice = 899m,
                        vendorSku = (string?)null,
                        handlingTimeHours = 24,
                        isCodAllowed = true,
                        maxOrderQuantity = (int?)null,
                    },
                    Cancellation)));

            Assert.Equal(1, offerRace.Count(response => response.IsSuccessStatusCode));

            var offers = await Database.CountAsync(
                "SELECT COUNT(*) FROM catalog.listings "
                + "WHERE vendor_id = $1 AND variant_id = $2 AND deleted_at IS NULL",
                Cancellation,
                seller.Id,
                product.VariantId);

            Assert.Equal(1, offers);

            foreach (var response in variantRace.Concat(submitRace).Concat(offerRace))
            {
                response.Dispose();
            }
        }
    }

    /// <summary>
    /// Every <c>CHECK</c> refuses what it is meant to, behind the API as well as through it.
    /// </summary>
    /// <remarks>
    /// Through the API first, because that is the message an operator reads; then behind it with a
    /// direct <c>UPDATE</c>, because a validator is a policy and a constraint is a guarantee. The
    /// price ceiling is the one that matters most: selling above MRP is not a preference in India,
    /// which is why it is in the schema and not only in a handler.
    /// </remarks>
    [Fact]
    public async Task Every_check_constraint_refuses_what_it_is_meant_to()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await sellers.ActiveAsync();

        var product = await catalogue.DraftAsync(taxonomy);
        var imageId = await catalogue.ImageAsync("gallery.png");

        await ReadAsync(await catalogue.SetProductMediaAsync(product.Id, imageId));
        await ReadAsync(await catalogue.ActivateVariantAsync(product.VariantId));
        await catalogue.PublishAsync(product.Id);

        var listingId = await catalogue.OfferAsync(seller.Id, product.VariantId, 899m, activate: false);

        // Above MRP, through the API.
        await RefusedAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/listings",
                new
                {
                    variantId = product.VariantId,
                    vendorId = (await sellers.ActiveAsync()).Id,
                    mrp = 1299m,
                    sellingPrice = 1499m,
                    vendorSku = (string?)null,
                    handlingTimeHours = 24,
                    isCodAllowed = true,
                    maxOrderQuantity = (int?)null,
                },
                Cancellation),
            HttpStatusCode.UnprocessableEntity,
            "CATALOG_PRICE_ABOVE_MRP");

        // A text attribute offered as a variant axis, through the API.
        await RefusedAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/products/{product.Id}/variants",
                new
                {
                    sku = (string?)null,
                    barcode = (string?)null,
                    nameSuffix = "Linen",
                    mrp = 1299m,
                    netQuantity = "1 N",
                    shelfLifeDays = (int?)null,
                    expiresOn = (DateOnly?)null,
                    weightGrams = 220,
                    lengthMm = 400,
                    widthMm = 400,
                    heightMm = 20,
                    position = 5,
                    isDefault = false,
                    options = new[] { new { attributeId = taxonomy.MaterialId, optionId = Guid.NewGuid() } },
                    media = (object?)null,
                },
                Cancellation),
            HttpStatusCode.UnprocessableEntity,
            "CATALOG_NOT_A_VARIANT_AXIS");

        var mediaId = await Database.ScalarAsync<Guid>(
            "SELECT id FROM catalog.media_assets WHERE product_id = $1 AND variant_id IS NULL LIMIT 1",
            Cancellation,
            product.Id);

        // And behind it: five statements, each of which the schema refuses on its own.
        (string Constraint, string Statement, object?[] Parameters)[] refusals =
        [
            ("selling price above MRP",
                "UPDATE catalog.listings SET selling_price_amount = mrp_amount + 1 WHERE id = $1",
                [listingId]),
            ("an offer with no seller",
                "UPDATE catalog.listings SET vendor_id = NULL WHERE id = $1",
                [listingId]),
            ("an HSN code of five digits",
                "UPDATE catalog.products SET hsn_code = '63022' WHERE id = $1",
                [product.Id]),
            ("a text attribute as a variant axis",
                "UPDATE catalog.attributes SET is_variant_defining = true WHERE id = $1",
                [taxonomy.MaterialId]),
            ("a media asset owned by nothing",
                "UPDATE catalog.media_assets SET product_id = NULL, variant_id = NULL WHERE id = $1",
                [mediaId]),
        ];

        foreach (var (constraint, statement, parameters) in refusals)
        {
            var sqlState = await Database.RefusalAsync(statement, Cancellation, parameters);

            Assert.True(
                sqlState == "23514",
                $"The database accepted {constraint}; it answered {sqlState ?? "nothing at all"} instead of 23514.");
        }
    }

    /// <summary>
    /// The generated <c>tsvector</c> is populated by the database, and its GIN index can serve the
    /// query Step 19 will run against it.
    /// </summary>
    /// <remarks>
    /// Sequential scans are turned off for the plan, deliberately. On a fixture holding a few
    /// hundred products PostgreSQL will always prefer a sequential scan, and a test that asserted
    /// the index was chosen anyway would be asserting the size of the fixture. Turning the
    /// alternative off asks the only question worth asking: is there an index here that <em>can</em>
    /// answer this.
    /// </remarks>
    [Fact]
    public async Task The_generated_search_vector_populates_and_its_gin_index_serves_the_query()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var catalogue = new CatalogScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var product = await catalogue.DraftAsync(taxonomy);

        // Nothing in C# ever writes this column: it is a stored generated column, so the database
        // filled it in as part of the insert the API performed.
        var vector = await Database.ScalarAsync<string>(
            "SELECT search_vector::text FROM catalog.products WHERE id = $1",
            Cancellation,
            product.Id);

        Assert.NotNull(vector);
        Assert.Contains("cushion", vector, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cotton", vector, StringComparison.OrdinalIgnoreCase);

        // And it stays in step with the columns it is built from, which is the whole reason it is
        // generated rather than maintained by a trigger somebody could forget to fire.
        // Both columns the expression reads, so what disappears from the vector is proof that it
        // was recomputed rather than merely appended to.
        await Database.ExecuteAsync(
            "UPDATE catalog.products SET name = 'Jacquard bolster', short_description = 'Woven silk.' "
            + "WHERE id = $1",
            Cancellation,
            product.Id);

        var rewritten = await Database.ScalarAsync<string>(
            "SELECT search_vector::text FROM catalog.products WHERE id = $1",
            Cancellation,
            product.Id);

        Assert.Contains("jacquard", rewritten!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cushion", rewritten, StringComparison.OrdinalIgnoreCase);

        var plan = await Database.RowsAfterAsync(
            "SET enable_seqscan = off",
            "EXPLAIN SELECT id FROM catalog.products "
            + "WHERE search_vector @@ to_tsquery('english', 'jacquard')",
            Cancellation);

        var text = string.Join(
            '\n',
            plan.Select(row => row.Values.First()?.ToString() ?? string.Empty));

        Assert.Contains("ix_products_search_vector", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The published listing read answers one row per known id, omits the rest, prefers the
    /// variant's own image, and reports what is purchasable.
    /// </summary>
    /// <remarks>
    /// This is the seam Inventory, Carts, Pricing and Orders all read the catalogue through, and
    /// none of them may join to this schema. <c>IsPurchasable</c> is computed in SQL from three
    /// states rather than being handed over as three booleans, precisely so four modules cannot
    /// combine them four different ways.
    /// </remarks>
    [Fact]
    public async Task The_published_listing_read_answers_one_row_per_known_id_and_omits_the_rest()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await sellers.ActiveAsync();

        var product = await catalogue.DraftAsync(taxonomy);
        var productImage = await catalogue.ImageAsync("product.png");
        var variantImage = await catalogue.ImageAsync("variant.png");

        await ReadAsync(await catalogue.SetProductMediaAsync(product.Id, productImage));
        await ReadAsync(await catalogue.ActivateVariantAsync(product.VariantId));

        // A second variant with its own image, and a third with none, so both branches of the image
        // fallback are exercised on one page.
        var ownGallery = await catalogue.VariantAsync(
            product.Id,
            taxonomy,
            optionIndex: 1,
            mediaFileId: variantImage);

        var ownGalleryId = ownGallery.GetProperty("id").GetGuid();

        await ReadAsync(await catalogue.ActivateVariantAsync(ownGalleryId));
        await catalogue.PublishAsync(product.Id);

        var live = await catalogue.OfferAsync(seller.Id, product.VariantId, 899m);
        var withImage = await catalogue.OfferAsync(seller.Id, ownGalleryId, 949m);
        var unknown = Guid.NewGuid();

        await RunOnceAsync<Contracts.Catalog.IProductCatalog>(async (directory, cancellation) =>
        {
            var found = await directory.FindListingsAsync([live, withImage, unknown, live], cancellation);

            // One row per known id, the unknown one silently omitted, the duplicate collapsed.
            Assert.Equal(2, found.Count);
            Assert.False(found.ContainsKey(unknown));

            Assert.Equal(productImage, found[live].PrimaryImageFileId);
            Assert.Equal(variantImage, found[withImage].PrimaryImageFileId);

            Assert.True(found[live].IsPurchasable);
            Assert.Equal(product.Id, found[live].ProductId);
            Assert.Equal(seller.Id, found[live].VendorId);
            Assert.Equal(899m, found[live].SellingPrice);

            // The category's materialised path travels with the row, because a promotion scoped to
            // a parent category has to apply to every descendant and Pricing cannot walk this tree.
            Assert.False(string.IsNullOrWhiteSpace(found[live].CategoryPath));
        });

        // Withdraw the variant. The offer's own status is untouched by the direct statement below,
        // so this isolates the variant's contribution to IsPurchasable.
        await Database.ExecuteAsync(
            "UPDATE catalog.variants SET status = 'Inactive' WHERE id = $1",
            Cancellation,
            product.VariantId);

        await RunOnceAsync<Contracts.Catalog.IProductCatalog>(async (directory, cancellation) =>
        {
            var found = await directory.FindListingsAsync([live], cancellation);

            Assert.False(
                found[live].IsPurchasable,
                "An offer against a withdrawn variant is not purchasable, whatever the offer itself says.");
        });

        // And the product's contribution, with the variant put back.
        await Database.ExecuteAsync(
            "UPDATE catalog.variants SET status = 'Active' WHERE id = $1",
            Cancellation,
            product.VariantId);

        await Database.ExecuteAsync(
            "UPDATE catalog.products SET status = 'Inactive' WHERE id = $1",
            Cancellation,
            product.Id);

        await RunOnceAsync<Contracts.Catalog.IProductCatalog>(async (directory, cancellation) =>
        {
            var found = await directory.FindListingsAsync([live], cancellation);

            Assert.False(
                found[live].IsPurchasable,
                "An offer against an unpublished product is not purchasable.");
        });
    }

    /// <summary>
    /// A queued job is claimed by exactly one worker: a second worker polling at the same instant
    /// steps over the claim rather than blocking on it or taking it twice.
    /// </summary>
    /// <remarks>
    /// <c>FOR UPDATE SKIP LOCKED</c> is what makes running more than one worker safe by
    /// construction rather than by convention. Both transactions are held open at once
    /// deliberately: the lock lives for the length of a transaction, and a claim taken outside one
    /// is released the instant the query returns — which is exactly the bug this shows is absent.
    /// </remarks>
    [Fact]
    public async Task A_queued_job_is_claimed_by_exactly_one_worker()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        var first = await QueueImportAsync(admin, Csv());
        var second = await QueueImportAsync(admin, Csv());

        await using var worker = new NpgsqlConnection(Database.ConnectionString);
        await using var rival = new NpgsqlConnection(Database.ConnectionString);

        await worker.OpenAsync(Cancellation);
        await rival.OpenAsync(Cancellation);

        await using var held = await worker.BeginTransactionAsync(Cancellation);
        await using var racing = await rival.BeginTransactionAsync(Cancellation);

        var mine = await ClaimAsync(worker, first, second);
        Assert.NotNull(mine);

        // The second worker, polling while the first still holds its claim. It gets the *other*
        // job — not this one, and not nothing.
        var theirs = await ClaimAsync(rival, first, second);

        Assert.NotNull(theirs);
        Assert.NotEqual(mine, theirs);

        // Nothing is left for a third, which is the other half: two jobs, two workers, no double
        // claim and no phantom.
        await using var spare = new NpgsqlConnection(Database.ConnectionString);
        await spare.OpenAsync(Cancellation);

        await using var idle = await spare.BeginTransactionAsync(Cancellation);

        Assert.Null(await ClaimAsync(spare, first, second));

        await held.RollbackAsync(Cancellation);
        await racing.RollbackAsync(Cancellation);
        await idle.RollbackAsync(Cancellation);
    }

    /// <summary>The claim query the dispatcher runs, on a connection whose transaction is open.</summary>
    /// <param name="connection">A connection with a transaction already open.</param>
    /// <param name="first">One candidate job.</param>
    /// <param name="second">The other.</param>
    private static async Task<Guid?> ClaimAsync(NpgsqlConnection connection, Guid first, Guid second)
    {
        await using var command = new NpgsqlCommand(
            "SELECT id FROM catalog.catalog_jobs WHERE status = 'Queued' AND id IN ($1, $2) "
            + "ORDER BY created_at LIMIT 1 FOR UPDATE SKIP LOCKED",
            connection);

        command.Parameters.Add(new NpgsqlParameter { Value = first });
        command.Parameters.Add(new NpgsqlParameter { Value = second });

        return await command.ExecuteScalarAsync(Cancellation) as Guid?;
    }

    /// <summary>Queues an import and answers the job id.</summary>
    /// <param name="client">The caller.</param>
    /// <param name="content">The file's text.</param>
    private static async Task<Guid> QueueImportAsync(HttpClient client, string content)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content));

        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "import.csv");

        var queued = await Rest.ReadAsync(
            await client.PostAsync(new Uri("/api/v1/admin/products/import", UriKind.Relative), form, Cancellation),
            Cancellation);

        return queued.GetProperty("id").GetGuid();
    }

    /// <summary>A one-row import file, structurally valid and never actually run.</summary>
    private static string Csv()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"sku,product_name\nKH-CLAIM-{Guid.NewGuid():N},Claim probe\n");
}
