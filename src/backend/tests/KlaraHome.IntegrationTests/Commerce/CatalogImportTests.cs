using System.Globalization;
using System.Text;
using System.Text.Json;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Catalog.Infrastructure;
using KlaraHome.Modules.Catalog.Infrastructure.Import;
using KlaraHome.SharedKernel.Time;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Step 10's other named acceptance criterion: a bulk import of a thousand SKUs validates and
/// loads, reports what it rejected, and converges when the same file is uploaded again.
/// </summary>
/// <remarks>
/// The import is asynchronous by design — a thousand rows is minutes of work and a request that
/// held a connection open for minutes would die behind a proxy with no record of how far it got —
/// so every test here queues the job through the API and then runs one pass of the real worker,
/// rather than waiting on a timer.
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class CatalogImportTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>How many rows the acceptance criterion names.</summary>
    private const int Thousand = 1_000;

    /// <summary>A thousand SKUs validate and load, and the catalogue holds them afterwards.</summary>
    /// <remarks>
    /// The step's own words. Loaded means the rows are in the database and readable through the
    /// API, not merely that the job reported a number: a runner that counted rows it never wrote
    /// would satisfy the report and nothing else.
    /// </remarks>
    [Fact]
    public async Task A_bulk_import_of_a_thousand_skus_validates_and_loads()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var catalogue = new CatalogScenario(admin, Cancellation);
        var taxonomy = await catalogue.TaxonomyAsync();

        var prefix = $"KH{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var file = new StringBuilder(Header());

        for (var index = 0; index < Thousand; index++)
        {
            file.Append(Row(prefix, index, taxonomy));
        }

        var job = await RunImportAsync(admin, file.ToString());

        Assert.Equal("Succeeded", job.GetProperty("status").GetString());
        Assert.Equal(Thousand, job.GetProperty("totalRows").GetInt32());
        Assert.Equal(Thousand, job.GetProperty("succeededRows").GetInt32());
        Assert.Equal(0, job.GetProperty("failedRows").GetInt32());

        var loaded = await Database.CountAsync(
            "SELECT COUNT(*) FROM catalog.variants WHERE sku LIKE $1",
            Cancellation,
            $"{prefix}-%");

        Assert.Equal(Thousand, loaded);

        // And they are real products, not orphan rows: one of them reads back through the API with
        // the compliance fields the file carried.
        var sku = string.Create(CultureInfo.InvariantCulture, $"{prefix}-000500");

        var productId = await Database.ScalarAsync<Guid>(
            "SELECT product_id FROM catalog.variants WHERE sku = $1",
            Cancellation,
            sku);

        var product = await ReadAsync(await admin.GetAsync(
            new Uri($"/api/v1/admin/products/{productId}", UriKind.Relative),
            Cancellation));

        Assert.Equal("630222", product.GetProperty("hsnCode").GetString());
        Assert.Equal("IN", product.GetProperty("countryOfOrigin").GetString());
        Assert.Equal(taxonomy.CategoryId, product.GetProperty("categoryId").GetGuid());

        var variant = Assert.Single(
            product.GetProperty("variants").EnumerateArray(),
            candidate => candidate.GetProperty("sku").GetString() == sku);

        Assert.Equal(1299m, variant.GetProperty("mrp").GetDecimal());
        Assert.Equal("1 N", variant.GetProperty("netQuantity").GetString());
        Assert.Equal(220, variant.GetProperty("weightGrams").GetInt32());
    }

    /// <summary>
    /// A file with bad rows loads the good ones and names every bad one, with its row number, its
    /// column and what was wrong.
    /// </summary>
    /// <remarks>
    /// Partial success is the contract, not a compromise. A single transaction over a thousand rows
    /// would let one bad row discard the other nine hundred and ninety-nine, and the report is what
    /// makes partial success usable: the merchandiser fixes the named rows and re-uploads only
    /// those.
    /// </remarks>
    [Fact]
    public async Task A_file_with_bad_rows_loads_the_good_ones_and_reports_the_rest()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var catalogue = new CatalogScenario(admin, Cancellation);
        var taxonomy = await catalogue.TaxonomyAsync();

        var prefix = $"KH{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var file = new StringBuilder(Header());

        // Row 2 is fine. Rows 3 to 6 are each wrong in a different, named way.
        file.Append(Row(prefix, 0, taxonomy));

        file.Append(CultureInfo.InvariantCulture,
            $",Nameless,,{taxonomy.CategorySlug},,,,630222,5,IN,1299,1 N,220\n");

        file.Append(CultureInfo.InvariantCulture,
            $"{prefix}/SLASH,Illegal sku,,{taxonomy.CategorySlug},,,,630222,5,IN,1299,1 N,220\n");

        file.Append(CultureInfo.InvariantCulture,
            $"{prefix}-BADCAT,Unknown category,,no-such-category-{Guid.NewGuid():N},,,,630222,5,IN,1299,1 N,220\n");

        file.Append(CultureInfo.InvariantCulture,
            $"{prefix}-BADHSN,Bad hsn,,{taxonomy.CategorySlug},,,,63022,5,IN,1299,1 N,220\n");

        var job = await RunImportAsync(admin, file.ToString());

        Assert.Equal("PartiallySucceeded", job.GetProperty("status").GetString());
        Assert.Equal(5, job.GetProperty("totalRows").GetInt32());
        Assert.Equal(1, job.GetProperty("succeededRows").GetInt32());
        Assert.Equal(4, job.GetProperty("failedRows").GetInt32());

        var errors = job.GetProperty("errors").EnumerateArray().ToList();
        Assert.Equal(4, errors.Count);

        // Row numbers are the file's own, counting the header as row one, because that is the
        // number the merchandiser's spreadsheet shows them.
        Assert.Equal([3, 4, 5, 6], errors.ConvertAll(error => error.GetProperty("rowNumber").GetInt32()));

        Assert.Equal("sku", errors[0].GetProperty("column").GetString());
        Assert.Equal("sku", errors[1].GetProperty("column").GetString());
        Assert.Equal("category_slug", errors[2].GetProperty("column").GetString());
        Assert.Equal("hsn_code", errors[3].GetProperty("column").GetString());

        Assert.Contains("required", errors[0].GetProperty("message").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("upper-case", errors[1].GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Contains("No category", errors[2].GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Contains("four, six or eight", errors[3].GetProperty("message").GetString()!, StringComparison.Ordinal);

        // The good row really did load, which is the half a batch-abort would have thrown away.
        var loaded = await Database.CountAsync(
            "SELECT COUNT(*) FROM catalog.variants WHERE sku LIKE $1",
            Cancellation,
            $"{prefix}-%");

        Assert.Equal(1, loaded);
    }

    /// <summary>
    /// Re-running the same file converges: nothing is duplicated, and a blank cell leaves what is
    /// already there alone.
    /// </summary>
    /// <remarks>
    /// The blank-cell rule is the one that costs a catalogue if it is wrong. A merchandiser
    /// uploading a two-column price file has not asked for every description in the catalogue to be
    /// blanked, and an importer that wrote null for every column the file omitted would do exactly
    /// that.
    /// </remarks>
    [Fact]
    public async Task Re_running_the_same_file_converges_and_a_blank_cell_changes_nothing()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var catalogue = new CatalogScenario(admin, Cancellation);
        var taxonomy = await catalogue.TaxonomyAsync();

        var prefix = $"KH{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var file = Header() + Row(prefix, 0, taxonomy) + Row(prefix, 1, taxonomy);

        var first = await RunImportAsync(admin, file);
        Assert.Equal(2, first.GetProperty("succeededRows").GetInt32());

        var second = await RunImportAsync(admin, file);
        Assert.Equal(2, second.GetProperty("succeededRows").GetInt32());
        Assert.Equal(0, second.GetProperty("failedRows").GetInt32());

        // Two rows, two SKUs, two products — after two runs of the same file.
        Assert.Equal(
            2,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM catalog.variants WHERE sku LIKE $1",
                Cancellation,
                $"{prefix}-%"));

        var sku = string.Create(CultureInfo.InvariantCulture, $"{prefix}-000000");

        var productId = await Database.ScalarAsync<Guid>(
            "SELECT product_id FROM catalog.variants WHERE sku = $1",
            Cancellation,
            sku);

        // A price-only file: the SKU, and nothing else but a new MRP. Every other column is blank.
        var priceOnly = string.Create(
            CultureInfo.InvariantCulture,
            $"sku,mrp\n{sku},1499\n");

        var third = await RunImportAsync(admin, priceOnly);
        Assert.Equal(1, third.GetProperty("succeededRows").GetInt32());

        var product = await ReadAsync(await admin.GetAsync(
            new Uri($"/api/v1/admin/products/{productId}", UriKind.Relative),
            Cancellation));

        // The price moved and nothing else did: the description, the HSN code and the country of
        // origin are all still what the first file set.
        var variant = Assert.Single(
            product.GetProperty("variants").EnumerateArray(),
            candidate => candidate.GetProperty("sku").GetString() == sku);

        Assert.Equal(1499m, variant.GetProperty("mrp").GetDecimal());
        Assert.Equal("1 N", variant.GetProperty("netQuantity").GetString());
        Assert.Equal(220, variant.GetProperty("weightGrams").GetInt32());

        Assert.Equal("630222", product.GetProperty("hsnCode").GetString());
        Assert.Equal("IN", product.GetProperty("countryOfOrigin").GetString());
        Assert.Equal("Woven in Panipat.", product.GetProperty("shortDescription").GetString());
        Assert.Equal(taxonomy.CategoryId, product.GetProperty("categoryId").GetGuid());
    }

    /// <summary>
    /// A seller's import cannot edit the platform's shared product, even though the seller can see
    /// it.
    /// </summary>
    /// <remarks>
    /// The one write path that does not go through a handler, and therefore the one that does not
    /// get <c>CatalogScope.CanWrite</c> for free. Without the check a seller uploading a file whose
    /// slug the platform already owns would silently rewrite the platform's copy — for every other
    /// seller on the marketplace at the same time. **This was missing and was added here.**
    /// </remarks>
    [Fact]
    public async Task A_sellers_import_cannot_rewrite_the_platforms_shared_product()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var shared = await catalogue.DraftAsync(taxonomy);

        var seller = await sellers.ActiveAsync();
        var (owner, _) = await SignedInVendorOwnerAsync(admin, seller.Id);

        using (owner)
        {
            var sku = $"KH-TAKEOVER-{Guid.NewGuid():N}".ToUpperInvariant();

            var file = string.Create(
                CultureInfo.InvariantCulture,
                $"sku,product_name,product_slug,category_slug\n{sku},Mine now,{shared.Slug},{taxonomy.CategorySlug}\n");

            var job = await RunImportAsync(owner, file);

            Assert.Equal("Failed", job.GetProperty("status").GetString());
            Assert.Equal(1, job.GetProperty("failedRows").GetInt32());

            var error = Assert.Single(job.GetProperty("errors").EnumerateArray());

            Assert.Equal("product_slug", error.GetProperty("column").GetString());
            Assert.Contains(
                "not yours to edit",
                error.GetProperty("message").GetString()!,
                StringComparison.Ordinal);
        }

        // The platform's product is untouched.
        var product = await ReadAsync(await admin.GetAsync(
            new Uri($"/api/v1/admin/products/{shared.Id}", UriKind.Relative),
            Cancellation));

        Assert.StartsWith("Cotton Cushion Cover", product.GetProperty("name").GetString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A row the database itself refuses is reported, and the job still finishes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two defects, both found here. The import file has no attribute columns, so every variant it
    /// creates carries the same "no options" combination hash and a second row for one product
    /// collides with the first — the card claimed later rows for a slug added variants to it, and
    /// they never could. It is now refused with a message a merchandiser can act on rather than
    /// with a constraint name.
    /// </para>
    /// <para>
    /// The second was worse. Recovering from a refused row cleared the change tracker, which
    /// detached the <em>job</em> along with the failed entity: every later count and the completion
    /// itself were applied to an entity nothing was tracking, the final save wrote nothing, and the
    /// job sat in <c>Running</c> for ever — unreportable and never re-claimed, because the poller
    /// only looks at <c>Queued</c>. One bad row lost the whole upload. The job is re-attached now,
    /// and this test fails by timing out if it ever is not.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_row_the_database_refuses_is_reported_and_the_job_still_finishes()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var catalogue = new CatalogScenario(admin, Cancellation);
        var taxonomy = await catalogue.TaxonomyAsync();

        var prefix = $"KH{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var slug = $"probe-{Guid.NewGuid():N}"[..20];

        var file = new StringBuilder(
            "sku,product_name,product_slug,category_slug,mrp,net_quantity,weight_grams,variant_name\n");

        for (var index = 0; index < 3; index++)
        {
            file.Append(
                CultureInfo.InvariantCulture,
                $"{prefix}-{index:D3},Shared product,{slug},{taxonomy.CategorySlug},1299,1 N,220,Colour {index}\n");
        }

        var job = await RunImportAsync(admin, file.ToString());

        // It finished at all, which is the second defect's whole assertion.
        Assert.Equal("PartiallySucceeded", job.GetProperty("status").GetString());
        Assert.Equal(3, job.GetProperty("processedRows").GetInt32());
        Assert.Equal(1, job.GetProperty("succeededRows").GetInt32());
        Assert.Equal(2, job.GetProperty("failedRows").GetInt32());

        var errors = job.GetProperty("errors").EnumerateArray().ToList();
        Assert.Equal(2, errors.Count);

        foreach (var error in errors)
        {
            Assert.Contains(
                "already has a variant",
                error.GetProperty("message").GetString()!,
                StringComparison.Ordinal);
        }

        // The first row loaded, and exactly one variant exists.
        Assert.Equal(
            1,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM catalog.variants WHERE sku LIKE $1",
                Cancellation,
                $"{prefix}-%"));
    }

    /// <summary>Queues an import, runs one pass of the real worker, and reads the finished job.</summary>
    /// <param name="client">The caller whose seller the job belongs to.</param>
    /// <param name="content">The file's text.</param>
    private async Task<JsonElement> RunImportAsync(HttpClient client, string content)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content));

        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "import.csv");

        var queued = await ReadAsync(await client.PostAsync(
            new Uri("/api/v1/admin/products/import", UriKind.Relative),
            form,
            Cancellation));

        var jobId = queued.GetProperty("id").GetGuid();

        Assert.Equal("Queued", queued.GetProperty("status").GetString());

        await DrainJobsAsync(jobId);

        return await ReadAsync(await client.GetAsync(
            new Uri($"/api/v1/admin/jobs/{jobId}", UriKind.Relative),
            Cancellation));
    }

    /// <summary>
    /// Runs the real job dispatcher until the named job has finished.
    /// </summary>
    /// <remarks>
    /// Constructed rather than resolved: the API host registers the dispatcher but leaves it
    /// switched off, because every replica polling would contend for the same rows. This is the
    /// same type the worker runs, over the same service provider, with the loop turned on for the
    /// length of one test — so what passes here is what the worker will do.
    /// </remarks>
    /// <param name="jobId">The job to wait for.</param>
    private async Task DrainJobsAsync(Guid jobId)
    {
        var options = new CatalogOptions { JobRunnerEnabled = true, JobPollIntervalSeconds = 1 };

        using var dispatcher = new CatalogJobDispatcher(
            Factory.Services,
            new FixedCatalogOptions(options),
            SystemClock.Instance,
            NullLogger<CatalogJobDispatcher>.Instance);

        await dispatcher.StartAsync(Cancellation);

        try
        {
            var deadline = DateTimeOffset.UtcNow.AddMinutes(5);

            while (DateTimeOffset.UtcNow < deadline)
            {
                var status = await Database.ScalarAsync<string>(
                    "SELECT status FROM catalog.catalog_jobs WHERE id = $1",
                    Cancellation,
                    jobId);

                if (status is "Succeeded" or "PartiallySucceeded" or "Failed")
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200), Cancellation);
            }

            Assert.Fail($"Job {jobId} had not finished after five minutes.");
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>The importer's own column layout, which is also the exporter's and the template's.</summary>
    private static string Header()
        => "sku,product_name,product_slug,category_slug,brand_slug,short_description,description,"
           + "hsn_code,gst_rate,country_of_origin,mrp,net_quantity,weight_grams\n";

    /// <summary>One complete, valid row: its own product and its own variant.</summary>
    /// <param name="prefix">A SKU prefix nothing else in the collection is using.</param>
    /// <param name="index">Which row this is.</param>
    /// <param name="taxonomy">The category and brand it files under.</param>
    private static string Row(string prefix, int index, CatalogTaxonomy taxonomy)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{prefix}-{index:D6},Imported cover {prefix} {index:D6},,{taxonomy.CategorySlug},"
            + $"{taxonomy.BrandSlug},Woven in Panipat.,A cotton cushion cover.,630222,5,IN,1299,1 N,220\n");

    /// <summary>The dispatcher reads its options from a monitor; this one never changes.</summary>
    /// <param name="value">The settings for this run.</param>
    private sealed class FixedCatalogOptions(CatalogOptions value) : IOptionsMonitor<CatalogOptions>
    {
        public CatalogOptions CurrentValue => value;

        public CatalogOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<CatalogOptions, string?> listener) => null;
    }
}
