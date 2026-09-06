using System.Globalization;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Modules.Catalog.Application.Products;
using KlaraHome.Modules.Catalog.Application.Validation;
using KlaraHome.Modules.Catalog.Domain;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Primitives;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Catalog.Infrastructure.Import;

/// <summary>The columns a product import file may carry.</summary>
/// <remarks>
/// Named constants rather than string literals scattered through the mapper, because these names
/// are a published contract: they appear in the template a merchandiser downloads, in the export,
/// and in every validation message that says which column is wrong.
/// </remarks>
internal static class ImportColumns
{
    /// <summary>The variant's stock-keeping unit. The row's identity, and the only required column.</summary>
    public const string Sku = "sku";

    /// <summary>The product's title. Required when the row creates a product.</summary>
    public const string ProductName = "product_name";

    /// <summary>The product's URL segment, or blank to derive one from the name.</summary>
    public const string ProductSlug = "product_slug";

    /// <summary>The category's slug. Required when the row creates a product.</summary>
    public const string CategorySlug = "category_slug";

    /// <summary>The brand's slug, or blank for an unbranded good.</summary>
    public const string BrandSlug = "brand_slug";

    /// <summary>The one-line summary.</summary>
    public const string ShortDescription = "short_description";

    /// <summary>The long description.</summary>
    public const string Description = "description";

    /// <summary>The HSN code the GST rate is resolved from.</summary>
    public const string HsnCode = "hsn_code";

    /// <summary>The GST percentage, as a number.</summary>
    public const string GstRate = "gst_rate";

    /// <summary>ISO 3166-1 alpha-2 country of origin.</summary>
    public const string CountryOfOrigin = "country_of_origin";

    /// <summary>The manufacturer's name.</summary>
    public const string ManufacturerName = "manufacturer_name";

    /// <summary>The manufacturer's address.</summary>
    public const string ManufacturerAddress = "manufacturer_address";

    /// <summary>The importer's name, for imported goods.</summary>
    public const string ImporterName = "importer_name";

    /// <summary>The importer's address, for imported goods.</summary>
    public const string ImporterAddress = "importer_address";

    /// <summary>Whether the product may be returned.</summary>
    public const string IsReturnable = "is_returnable";

    /// <summary>The product's own return window in days.</summary>
    public const string ReturnWindowDays = "return_window_days";

    /// <summary>What distinguishes the variant, appended to the product name.</summary>
    public const string VariantName = "variant_name";

    /// <summary>The barcode on the pack.</summary>
    public const string Barcode = "barcode";

    /// <summary>Maximum retail price.</summary>
    public const string Mrp = "mrp";

    /// <summary>The declared net quantity.</summary>
    public const string NetQuantity = "net_quantity";

    /// <summary>Dead weight in grams.</summary>
    public const string WeightGrams = "weight_grams";

    /// <summary>Packed length in millimetres.</summary>
    public const string LengthMm = "length_mm";

    /// <summary>Packed width in millimetres.</summary>
    public const string WidthMm = "width_mm";

    /// <summary>Packed height in millimetres.</summary>
    public const string HeightMm = "height_mm";

    /// <summary>What the seller is asking. Present means the row also creates or updates an offer.</summary>
    public const string SellingPrice = "selling_price";

    /// <summary>The seller's own code for the goods.</summary>
    public const string VendorSku = "vendor_sku";

    /// <summary>Whether the seller accepts cash on delivery for this offer.</summary>
    public const string IsCodAllowed = "is_cod_allowed";

    /// <summary>The most units one order may take.</summary>
    public const string MaxOrderQuantity = "max_order_quantity";

    /// <summary>Every column, in the order the template and the export write them.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Sku, ProductName, ProductSlug, CategorySlug, BrandSlug, ShortDescription, Description,
        HsnCode, GstRate, CountryOfOrigin, ManufacturerName, ManufacturerAddress,
        ImporterName, ImporterAddress, IsReturnable, ReturnWindowDays,
        VariantName, Barcode, Mrp, NetQuantity, WeightGrams, LengthMm, WidthMm, HeightMm,
        SellingPrice, VendorSku, IsCodAllowed, MaxOrderQuantity,
    ];
}

/// <summary>
/// Applies a bulk product import, one row at a time, and writes the validation report.
/// </summary>
/// <remarks>
/// <para>
/// One row is one variant, with its product's fields repeated on every row of that product — which
/// is the shape a spreadsheet actually takes, and the shape every marketplace's import template
/// has. The first row for a product creates it; later rows for the same slug add variants to it.
/// </para>
/// <para>
/// <b>Row-at-a-time, each in its own save.</b> A single transaction over a thousand rows would mean
/// one bad row discards the other nine hundred and ninety-nine, and the merchandiser's afternoon
/// with it. Partial success is the correct behaviour here, and the report is what makes it usable:
/// the operator fixes the named rows and re-uploads only those.
/// </para>
/// <para>
/// Re-running the same file is safe. A SKU that already exists is updated rather than rejected, so
/// a corrected re-upload converges rather than duplicating.
/// </para>
/// </remarks>
/// <param name="context">The Catalog data context.</param>
/// <param name="vendors">Checks that the seller behind an offer may trade.</param>
/// <param name="options">Supplies the row cap and the vendor rule.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class ProductImportRunner(
    CatalogDbContext context,
    IVendorDirectory vendors,
    IOptions<CatalogOptions> options,
    IClock clock)
{
    /// <summary>Reads the file, applies what it can, and records the rest on the job.</summary>
    /// <param name="job">The job, already claimed and tracked.</param>
    /// <param name="content">The uploaded file's text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RunAsync(CatalogJob job, string content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(content);

        var rows = Csv.Parse(content);

        if (rows.Count < 2)
        {
            job.Fail("The file has no data rows beneath its header.", clock.UtcNow);
            return;
        }

        var columns = CsvRow.MapColumns(rows[0]);

        if (!columns.ContainsKey(ImportColumns.Sku))
        {
            job.Fail($"The file has no '{ImportColumns.Sku}' column, which every row needs.", clock.UtcNow);
            return;
        }

        var data = rows.Skip(1).ToList();

        if (data.Count > options.Value.MaxImportRows)
        {
            job.Fail(
                $"The file has {data.Count} rows; this deployment accepts at most {options.Value.MaxImportRows}.",
                clock.UtcNow);

            return;
        }

        job.CountRows(data.Count);

        for (var index = 0; index < data.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var row = new CsvRow(columns, data[index], index + 2);

            if (row.IsBlank)
            {
                continue;
            }

            var error = await ApplyRowAsync(job, row, cancellationToken).ConfigureAwait(false);

            if (error is null)
            {
                job.RowSucceeded();
            }
            else
            {
                job.RowFailed(error);
            }
        }

        job.Complete(clock.UtcNow);
    }

    /// <summary>Applies one row, or returns why it could not be.</summary>
    private async Task<ImportRowError?> ApplyRowAsync(
        CatalogJob job,
        CsvRow row,
        CancellationToken cancellationToken)
    {
        var sku = row.Text(ImportColumns.Sku)?.ToUpperInvariant();

        if (sku is null)
        {
            return new ImportRowError(row.Number, ImportColumns.Sku, null, "A SKU is required.");
        }

        if (!CatalogFormats.Sku().IsMatch(sku))
        {
            return new ImportRowError(
                row.Number,
                ImportColumns.Sku,
                sku,
                "A SKU is upper-case letters, digits, hyphens and underscores.");
        }

        try
        {
            var product = await ResolveProductAsync(job, row, cancellationToken).ConfigureAwait(false);

            if (product.IsFailure)
            {
                return new ImportRowError(row.Number, product.Column, sku, product.Message!);
            }

            var variant = await UpsertVariantAsync(product.Value!, sku, row, cancellationToken)
                .ConfigureAwait(false);

            var offer = await UpsertListingAsync(job, product.Value!, variant, row, cancellationToken)
                .ConfigureAwait(false);

            if (offer is not null)
            {
                return offer with { RowNumber = row.Number, Sku = sku };
            }

            // Saved per row, so a rejected row costs only itself. See the class remarks.
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return null;
        }
        catch (DbUpdateException exception)
        {
            // A unique-index collision the pre-checks did not catch — two rows of the same file
            // claiming one slug, most often. The row is reported and the change tracker is left
            // holding a failed entry, so it is detached before the next row is attempted.
            context.ChangeTracker.Clear();

            return new ImportRowError(
                row.Number,
                null,
                sku,
                $"The database refused this row: {exception.InnerException?.Message ?? exception.Message}");
        }
    }

    /// <summary>Finds or creates the product this row belongs to.</summary>
    private async Task<ResolvedProduct> ResolveProductAsync(
        CatalogJob job,
        CsvRow row,
        CancellationToken cancellationToken)
    {
        var name = row.Text(ImportColumns.ProductName);
        var slugText = row.Text(ImportColumns.ProductSlug);

        var slug = slugText?.ToLowerInvariant()
                   ?? (name is null ? null : CatalogFormats.ToSlug(name, 320));

        if (slug is null)
        {
            return ResolvedProduct.Rejected(
                ImportColumns.ProductName,
                $"A row needs either '{ImportColumns.ProductName}' or '{ImportColumns.ProductSlug}'.");
        }

        var existing = await context.Products
            .FirstOrDefaultAsync(candidate => candidate.Slug == slug, cancellationToken)
            .ConfigureAwait(false);

        var categorySlug = row.Text(ImportColumns.CategorySlug)?.ToLowerInvariant();
        Guid? categoryId = null;

        if (categorySlug is not null)
        {
            categoryId = await context.Categories
                .Where(category => category.Slug == categorySlug)
                .Select(category => (Guid?)category.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (categoryId is null)
            {
                return ResolvedProduct.Rejected(
                    ImportColumns.CategorySlug,
                    $"No category has the slug '{categorySlug}'.");
            }
        }

        var brandSlug = row.Text(ImportColumns.BrandSlug)?.ToLowerInvariant();
        Guid? brandId = null;

        if (brandSlug is not null)
        {
            brandId = await context.Brands
                .Where(brand => brand.Slug == brandSlug)
                .Select(brand => (Guid?)brand.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (brandId is null)
            {
                return ResolvedProduct.Rejected(ImportColumns.BrandSlug, $"No brand has the slug '{brandSlug}'.");
            }
        }

        if (existing is null)
        {
            if (name is null)
            {
                return ResolvedProduct.Rejected(
                    ImportColumns.ProductName,
                    $"No product has the slug '{slug}', so '{ImportColumns.ProductName}' is required to create one.");
            }

            if (categoryId is null)
            {
                return ResolvedProduct.Rejected(
                    ImportColumns.CategorySlug,
                    $"'{ImportColumns.CategorySlug}' is required to create a product.");
            }

            existing = Product.Draft(name, slug, categoryId.Value, job.VendorId);
            context.Products.Add(existing);
        }

        var hsn = row.Text(ImportColumns.HsnCode);

        if (hsn is not null && !CatalogFormats.HsnCode().IsMatch(hsn))
        {
            return ResolvedProduct.Rejected(ImportColumns.HsnCode, "An HSN code is four, six or eight digits.");
        }

        var origin = row.Text(ImportColumns.CountryOfOrigin)?.ToUpperInvariant();

        if (origin is not null && !CatalogFormats.CountryCode().IsMatch(origin))
        {
            return ResolvedProduct.Rejected(
                ImportColumns.CountryOfOrigin,
                "A country of origin is a two-letter ISO 3166-1 code, for example IN.");
        }

        // Blank means "leave what is there". An import that overwrote every unfilled column with
        // null would let a two-column price-update file wipe a catalogue's descriptions.
        ProductWriter.Describe(
            existing,
            name ?? existing.Name,
            existing.Slug,
            categoryId ?? existing.CategoryId,
            brandId ?? existing.BrandId,
            row.Text(ImportColumns.ShortDescription) ?? existing.ShortDescription,
            row.Text(ImportColumns.Description) ?? existing.Description,
            [.. existing.Specifications.Select(spec => new SpecificationPayload(spec.Label, spec.Value, spec.Group))],
            null);

        ProductWriter.DeclareCompliance(
            existing,
            hsn ?? existing.HsnCode,
            row.Amount(ImportColumns.GstRate) ?? existing.GstRate,
            origin ?? existing.CountryOfOrigin,
            Party(row, ImportColumns.ManufacturerName, ImportColumns.ManufacturerAddress, existing.Manufacturer),
            new PartyPayload(existing.Packer.Name, existing.Packer.Address, existing.Packer.Contact),
            Party(row, ImportColumns.ImporterName, ImportColumns.ImporterAddress, existing.Importer));

        existing.SetAfterSalesTerms(
            row.Flag(ImportColumns.IsReturnable, existing.IsReturnable),
            row.Integer(ImportColumns.ReturnWindowDays) ?? existing.ReturnWindowDays,
            existing.Warranty);

        return ResolvedProduct.Accepted(existing);
    }

    /// <summary>Creates or updates the variant this row names.</summary>
    private async Task<Variant> UpsertVariantAsync(
        Product product,
        string sku,
        CsvRow row,
        CancellationToken cancellationToken)
    {
        var variant = await context.Variants
            .FirstOrDefaultAsync(candidate => candidate.Sku == sku, cancellationToken)
            .ConfigureAwait(false);

        if (variant is null)
        {
            variant = Variant.Create(product.Id, sku);
            context.Variants.Add(variant);

            // The first variant of a product is the one the PDP opens on. Nothing else in an import
            // decides it, and a product whose PDP has no variant to open on renders nothing.
            var hasSibling = await context.Variants
                .AnyAsync(candidate => candidate.ProductId == product.Id, cancellationToken)
                .ConfigureAwait(false);

            variant.SetDefault(!hasSibling);
        }

        variant.Describe(
            sku,
            row.Text(ImportColumns.Barcode) ?? variant.Barcode,
            row.Text(ImportColumns.VariantName) ?? variant.NameSuffix,
            variant.Position);

        variant.SetDimensions(
            row.Integer(ImportColumns.WeightGrams) ?? variant.WeightGrams,
            row.Integer(ImportColumns.LengthMm) ?? variant.LengthMm,
            row.Integer(ImportColumns.WidthMm) ?? variant.WidthMm,
            row.Integer(ImportColumns.HeightMm) ?? variant.HeightMm);

        variant.DeclarePack(
            Money.Rupees(row.Amount(ImportColumns.Mrp) ?? variant.Mrp.Amount),
            row.Text(ImportColumns.NetQuantity) ?? variant.NetQuantity,
            variant.ShelfLifeDays,
            variant.ExpiresOn);

        return variant;
    }

    /// <summary>Creates or updates this row's offer, or returns why it could not be.</summary>
    /// <remarks>
    /// Only when the row carries a price. A platform merchandiser loading a shared catalogue for
    /// sellers to offer against supplies no price at all, and inventing one for them would put
    /// products on sale that nobody agreed to sell.
    /// </remarks>
    private async Task<ImportRowError?> UpsertListingAsync(
        CatalogJob job,
        Product product,
        Variant variant,
        CsvRow row,
        CancellationToken cancellationToken)
    {
        if (row.Amount(ImportColumns.SellingPrice) is not { } price)
        {
            return null;
        }

        if (job.VendorId is not { } vendorId)
        {
            return new ImportRowError(
                0,
                ImportColumns.SellingPrice,
                null,
                "Only a seller's own import can set a price. Platform imports load products, not offers.");
        }

        if (!options.Value.AllowListingsFromInactiveVendors
            && !await vendors.IsActiveAsync(vendorId, cancellationToken).ConfigureAwait(false))
        {
            return new ImportRowError(0, null, null, "That seller is not currently trading.");
        }

        var mrp = variant.Mrp.Amount;

        if (mrp <= 0m)
        {
            return new ImportRowError(
                0,
                ImportColumns.Mrp,
                null,
                $"An offer needs an MRP. Supply '{ImportColumns.Mrp}' on this row or on an earlier one.");
        }

        if (price > mrp)
        {
            return new ImportRowError(
                0,
                ImportColumns.SellingPrice,
                null,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The selling price {price} is above the MRP {mrp}, which is not permitted in India."));
        }

        var listing = await context.Listings
            .FirstOrDefaultAsync(
                candidate => candidate.VendorId == vendorId && candidate.VariantId == variant.Id,
                cancellationToken)
            .ConfigureAwait(false);

        if (listing is null)
        {
            listing = Listing.Open(vendorId, variant.Id, product.Id);
            context.Listings.Add(listing);
        }

        listing.SetTerms(
            Money.Rupees(mrp),
            Money.Rupees(price),
            row.Text(ImportColumns.VendorSku) ?? listing.VendorSku,
            listing.HandlingTimeHours,
            row.Flag(ImportColumns.IsCodAllowed, listing.IsCodAllowed),
            row.Integer(ImportColumns.MaxOrderQuantity) ?? listing.MaxOrderQuantity);

        return null;
    }

    private static PartyPayload Party(CsvRow row, string nameColumn, string addressColumn, PartyDetails current)
        => new(
            row.Text(nameColumn) ?? current.Name,
            row.Text(addressColumn) ?? current.Address,
            current.Contact);

    /// <summary>Either the product a row belongs to, or why it could not be resolved.</summary>
    /// <param name="Value">The product, when the row was accepted.</param>
    /// <param name="Column">The column at fault, when it was not.</param>
    /// <param name="Message">What is wrong.</param>
    private readonly record struct ResolvedProduct(Product? Value, string? Column, string? Message)
    {
        public bool IsFailure => Value is null;

        public static ResolvedProduct Accepted(Product product) => new(product, null, null);

        public static ResolvedProduct Rejected(string? column, string message) => new(null, column, message);
    }
}
