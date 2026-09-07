using System.Net;
using System.Net.Http.Json;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Step 10's security criteria: what a seller may do to the shared catalogue, to somebody else's
/// products, and what no seller may do at all.
/// </summary>
/// <remarks>
/// <para>
/// The catalogue draws a line the Vendors module does not have to: a seller can <em>see</em> the
/// platform's own products — that is what makes the catalogue shared — and must not be able to
/// write to them. So there are two refusals here rather than one, and which one is correct depends
/// on whether the caller can see the row at all. A product they cannot see answers 404, because a
/// scope error would confirm that the id belongs to somebody
/// (<c>docs/07-security-compliance.md</c> §2). A product they can see and must not touch answers
/// the scope error, because pretending it does not exist would contradict the read that just
/// returned it.
/// </para>
/// <para>
/// Permission is the third rule and it is separate from both: moderation and the taxonomy are
/// platform-staff-only, on every product including a seller's own.
/// </para>
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class CatalogAuthorisationTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// A seller can read the platform's shared product and cannot write a single thing to it.
    /// </summary>
    /// <remarks>
    /// The read succeeding is half the assertion. A rule that hid the platform's catalogue from
    /// sellers would pass every refusal below and destroy the shared-catalogue model at the same
    /// time.
    /// </remarks>
    [Fact]
    public async Task A_seller_can_read_a_platform_owned_product_and_cannot_write_to_it()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var shared = await catalogue.DraftAsync(taxonomy);

        var mine = await sellers.ActiveAsync();
        var (owner, _) = await SignedInVendorOwnerAsync(admin, mine.Id);

        using (owner)
        {
            var read = await ReadAsync(await owner.GetAsync(
                new Uri($"/api/v1/admin/products/{shared.Id}", UriKind.Relative),
                Cancellation));

            Assert.Equal(shared.Id, read.GetProperty("id").GetGuid());

            // Every write against it, refused with the scope error rather than a 404 — the seller
            // has just been shown the row, so denying its existence would be a lie.
            await RefusedAsync(
                await owner.PutAsJsonAsync(
                    $"/api/v1/admin/products/{shared.Id}",
                    ProductEdit(taxonomy.CategoryId),
                    Cancellation),
                HttpStatusCode.UnprocessableEntity,
                "CATALOG_SCOPE");

            await RefusedAsync(
                await owner.PostAsJsonAsync(
                    $"/api/v1/admin/products/{shared.Id}/variants",
                    NewVariant(),
                    Cancellation),
                HttpStatusCode.UnprocessableEntity,
                "CATALOG_SCOPE");

            await RefusedAsync(
                await owner.PutAsJsonAsync(
                    $"/api/v1/admin/products/{shared.Id}/media",
                    new { media = Array.Empty<object>() },
                    Cancellation),
                HttpStatusCode.UnprocessableEntity,
                "CATALOG_SCOPE");

            await RefusedAsync(
                await owner.PostAsJsonAsync(
                    $"/api/v1/admin/products/{shared.Id}/submit",
                    new { },
                    Cancellation),
                HttpStatusCode.UnprocessableEntity,
                "CATALOG_SCOPE");

            await RefusedAsync(
                await owner.DeleteAsync(
                    new Uri($"/api/v1/admin/products/{shared.Id}", UriKind.Relative),
                    Cancellation),
                HttpStatusCode.UnprocessableEntity,
                "CATALOG_SCOPE");

            // And its variants, which are reachable by their own ids and not through the product.
            await RefusedAsync(
                await owner.PutAsJsonAsync(
                    $"/api/v1/admin/variants/{shared.VariantId}",
                    NewVariant(shared.Sku),
                    Cancellation),
                HttpStatusCode.UnprocessableEntity,
                "CATALOG_SCOPE");

            await RefusedAsync(
                await owner.PostAsJsonAsync(
                    $"/api/v1/admin/variants/{shared.VariantId}/activate",
                    new { },
                    Cancellation),
                HttpStatusCode.UnprocessableEntity,
                "CATALOG_SCOPE");

            await RefusedAsync(
                await owner.DeleteAsync(
                    new Uri($"/api/v1/admin/variants/{shared.VariantId}", UriKind.Relative),
                    Cancellation),
                HttpStatusCode.UnprocessableEntity,
                "CATALOG_SCOPE");
        }
    }

    /// <summary>
    /// A seller reaching another seller's product, variant, offer or import job is told it does not
    /// exist.
    /// </summary>
    /// <remarks>
    /// A variant is the one that is easy to get wrong, because a variant row is not itself
    /// vendor-scoped — the catalogue is shared, so the query filter is on the product — and the
    /// refusal has to be derived from a product the caller cannot see. Answering the scope error
    /// there would make a variant id an existence oracle: 422 for one that belongs to a competitor,
    /// 404 for one that belongs to nobody. **It did exactly that, and was fixed here.**
    /// </remarks>
    [Fact]
    public async Task A_seller_cannot_reach_another_sellers_product_variant_offer_or_import_job()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();

        var theirs = await sellers.ActiveAsync();
        var (competitor, _) = await SignedInVendorOwnerAsync(admin, theirs.Id);

        var product = await catalogue.DraftAsync(taxonomy, client: competitor);
        await ReadAsync(await catalogue.ActivateVariantAsync(product.VariantId, competitor));
        await ReadAsync(await catalogue.TransitionAsync(product.Id, "submit", client: competitor));
        await ReadAsync(await catalogue.TransitionAsync(product.Id, "approve"));

        var listingId = await catalogue.OfferAsync(null, product.VariantId, 899m, client: competitor);
        var jobId = await QueueImportAsync(competitor, "sku,product_name\nKH-NOT-YOURS-1,Their cushion\n");

        competitor.Dispose();

        var mine = await sellers.ActiveAsync();
        var (owner, _) = await SignedInVendorOwnerAsync(admin, mine.Id);

        using (owner)
        {
            string[] reads =
            [
                $"/api/v1/admin/products/{product.Id}",
                $"/api/v1/admin/listings/{listingId}",
                $"/api/v1/admin/jobs/{jobId}",
            ];

            foreach (var route in reads)
            {
                var response = await owner.GetAsync(new Uri(route, UriKind.Relative), Cancellation);

                Assert.True(
                    response.StatusCode == HttpStatusCode.NotFound,
                    $"{route} answered {(int)response.StatusCode}. Anything but 404 confirms the row exists.");
            }

            // The lists are filtered too, which is the same rule stated positively: a seller's own
            // page of products never contains somebody else's.
            var listed = await ReadAsync(await owner.GetAsync(
                new Uri("/api/v1/admin/products?size=100", UriKind.Relative),
                Cancellation));

            Assert.DoesNotContain(
                listed.GetProperty("items").EnumerateArray(),
                row => row.GetProperty("id").GetGuid() == product.Id);

            // Writes answer the same way, on the product and on its variant. A variant id is not
            // vendor-scoped, so this is the one that has to derive its 404 from the product.
            await RefusedAsync(
                await owner.PutAsJsonAsync(
                    $"/api/v1/admin/products/{product.Id}",
                    ProductEdit(taxonomy.CategoryId),
                    Cancellation),
                HttpStatusCode.NotFound,
                "CATALOG_NOT_FOUND");

            await RefusedAsync(
                await owner.PutAsJsonAsync(
                    $"/api/v1/admin/variants/{product.VariantId}",
                    NewVariant(product.Sku),
                    Cancellation),
                HttpStatusCode.NotFound,
                "CATALOG_NOT_FOUND");

            await RefusedAsync(
                await owner.PostAsJsonAsync(
                    $"/api/v1/admin/variants/{product.VariantId}/deactivate",
                    new { },
                    Cancellation),
                HttpStatusCode.NotFound,
                "CATALOG_NOT_FOUND");

            await RefusedAsync(
                await owner.DeleteAsync(
                    new Uri($"/api/v1/admin/variants/{product.VariantId}", UriKind.Relative),
                    Cancellation),
                HttpStatusCode.NotFound,
                "CATALOG_NOT_FOUND");

            // A variant that belongs to nobody answers exactly the same, which is what stops the
            // pair of them being an existence oracle.
            await RefusedAsync(
                await owner.PostAsJsonAsync(
                    $"/api/v1/admin/variants/{Guid.NewGuid()}/deactivate",
                    new { },
                    Cancellation),
                HttpStatusCode.NotFound,
                "CATALOG_NOT_FOUND");

            // And the offer, which is vendor-scoped in its own right.
            await RefusedAsync(
                await owner.PostAsJsonAsync(
                    $"/api/v1/admin/listings/{listingId}/deactivate",
                    new { reason = "Not mine." },
                    Cancellation),
                HttpStatusCode.NotFound,
                "CATALOG_NOT_FOUND");
        }
    }

    /// <summary>
    /// A seller cannot approve, reject, publish, unpublish or archive a product — not even their
    /// own.
    /// </summary>
    /// <remarks>
    /// This is a permission refusal about an operation, not a scope refusal about a resource, so it
    /// is a 403 and it is the same 403 on the seller's own product. A seller who could publish
    /// their own submission is a marketplace with no moderation at all, which is the one thing the
    /// queue exists to prevent.
    /// </remarks>
    [Fact]
    public async Task A_seller_cannot_approve_publish_or_archive_a_product_even_their_own()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var mine = await sellers.ActiveAsync();
        var (owner, _) = await SignedInVendorOwnerAsync(admin, mine.Id);

        using (owner)
        {
            var product = await catalogue.DraftAsync(taxonomy, client: owner);

            await ReadAsync(await catalogue.ActivateVariantAsync(product.VariantId, owner));
            await ReadAsync(await catalogue.TransitionAsync(product.Id, "submit", client: owner));

            string[] moderation = ["approve", "reject", "publish", "unpublish", "archive"];

            foreach (var transition in moderation)
            {
                var response = await owner.PostAsJsonAsync(
                    $"/api/v1/admin/products/{product.Id}/{transition}",
                    new { notes = "Looks good to me." },
                    Cancellation);

                Assert.True(
                    response.StatusCode == HttpStatusCode.Forbidden,
                    $"A seller reached {transition} and got {(int)response.StatusCode}, not 403.");
            }

            var bulk = await owner.PostAsJsonAsync(
                "/api/v1/admin/products/bulk-status",
                new { productIds = new[] { product.Id }, status = "Active" },
                Cancellation);

            Assert.Equal(HttpStatusCode.Forbidden, bulk.StatusCode);

            // It really is still waiting: a refusal that had published it anyway would satisfy
            // every status assertion above.
            var read = await ReadAsync(await owner.GetAsync(
                new Uri($"/api/v1/admin/products/{product.Id}", UriKind.Relative),
                Cancellation));

            Assert.Equal("PendingApproval", read.GetProperty("status").GetString());
        }
    }

    /// <summary>A seller reads the taxonomy and changes none of it.</summary>
    /// <remarks>
    /// The tree, the brands and the attribute vocabulary are shared by every seller on the
    /// marketplace, so one seller renaming an attribute would reshape everybody else's products and
    /// every facet built on them. Reading it is necessary — a seller has to file their product
    /// somewhere — and that is why the two permissions are separate.
    /// </remarks>
    [Fact]
    public async Task A_seller_reads_the_taxonomy_and_cannot_change_it()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var mine = await sellers.ActiveAsync();
        var (owner, _) = await SignedInVendorOwnerAsync(admin, mine.Id);

        using (owner)
        {
            var tree = await owner.GetAsync(new Uri("/api/v1/admin/categories", UriKind.Relative), Cancellation);
            Assert.Equal(HttpStatusCode.OK, tree.StatusCode);

            // The two flags are spelled out because this listing declares them as required query
            // parameters rather than optional filters, unlike every other module's — see the
            // Parking Lot row this test raised.
            var attributes = await owner.GetAsync(
                new Uri("/api/v1/admin/attributes?filterableOnly=false&variantDefiningOnly=false", UriKind.Relative),
                Cancellation);

            Assert.Equal(HttpStatusCode.OK, attributes.StatusCode);

            var brands = await owner.GetAsync(
                new Uri("/api/v1/admin/brands?activeOnly=false", UriKind.Relative),
                Cancellation);

            Assert.Equal(HttpStatusCode.OK, brands.StatusCode);

            (string Route, HttpMethod Method)[] writes =
            [
                ("/api/v1/admin/categories", HttpMethod.Post),
                ($"/api/v1/admin/categories/{taxonomy.CategoryId}", HttpMethod.Put),
                ($"/api/v1/admin/categories/{taxonomy.CategoryId}", HttpMethod.Delete),
                ("/api/v1/admin/brands", HttpMethod.Post),
                ($"/api/v1/admin/brands/{taxonomy.BrandId}", HttpMethod.Put),
                ($"/api/v1/admin/brands/{taxonomy.BrandId}", HttpMethod.Delete),
                ("/api/v1/admin/attributes", HttpMethod.Post),
                ($"/api/v1/admin/attributes/{taxonomy.ColourId}", HttpMethod.Put),
                ($"/api/v1/admin/attributes/{taxonomy.ColourId}", HttpMethod.Delete),
                ("/api/v1/admin/attribute-sets", HttpMethod.Post),
            ];

            foreach (var (route, method) in writes)
            {
                using var request = new HttpRequestMessage(method, new Uri(route, UriKind.Relative))
                {
                    Content = JsonContent.Create(new { name = "Mine now", code = (string?)null }),
                };

                var response = await owner.SendAsync(request, Cancellation);

                Assert.True(
                    response.StatusCode == HttpStatusCode.Forbidden,
                    $"{method} {route} answered {(int)response.StatusCode}, not 403.");
            }
        }
    }

    /// <summary>Queues an import and answers the job id, without waiting for a worker.</summary>
    /// <param name="client">The caller whose seller the job belongs to.</param>
    /// <param name="content">The file's text.</param>
    private static async Task<Guid> QueueImportAsync(HttpClient client, string content)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content));

        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "import.csv");

        var queued = await ReadAsync(await client.PostAsync(
            new Uri("/api/v1/admin/products/import", UriKind.Relative),
            form,
            Cancellation));

        return queued.GetProperty("id").GetGuid();
    }

    /// <summary>A minimally valid product body, for a write that is expected to be refused.</summary>
    /// <param name="categoryId">A category that exists, so the refusal is about scope and not shape.</param>
    private static object ProductEdit(Guid categoryId)
        => new
        {
            name = "Renamed by somebody else",
            slug = (string?)null,
            categoryId,
            brandId = (Guid?)null,
            vendorId = (Guid?)null,
            shortDescription = (string?)null,
            description = (string?)null,
            hsnCode = "630222",
            gstRate = 5m,
            countryOfOrigin = "IN",
            manufacturer = (object?)null,
            packer = (object?)null,
            importer = (object?)null,
            isReturnable = true,
            returnWindowDays = 7,
            warranty = (string?)null,
            specifications = Array.Empty<object>(),
            seo = (object?)null,
            attributes = Array.Empty<object>(),
        };

    /// <summary>A minimally valid variant body, for a write that is expected to be refused.</summary>
    /// <param name="sku">The SKU to send, or null to let the sequence mint one.</param>
    private static object NewVariant(string? sku = null)
        => new
        {
            sku,
            barcode = (string?)null,
            nameSuffix = "Taken over",
            mrp = 1299m,
            netQuantity = "1 N",
            shelfLifeDays = (int?)null,
            expiresOn = (DateOnly?)null,
            weightGrams = 220,
            lengthMm = 400,
            widthMm = 400,
            heightMm = 20,
            position = 9,
            isDefault = false,
            options = Array.Empty<object>(),
            media = (object?)null,
        };
}
