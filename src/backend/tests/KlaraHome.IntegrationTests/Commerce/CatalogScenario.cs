using System.Net.Http.Json;
using System.Text.Json;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>The shared vocabulary a catalogue test describes its products with.</summary>
/// <param name="RootCategoryId">The top of the branch this scenario created.</param>
/// <param name="CategoryId">The leaf products are filed under.</param>
/// <param name="CategorySlug">That leaf's slug, which the importer resolves by.</param>
/// <param name="BrandId">A brand nothing else is using.</param>
/// <param name="BrandSlug">Its slug, which the importer resolves by.</param>
/// <param name="ColourId">A <c>select</c> attribute flagged as a variant axis.</param>
/// <param name="ColourOptionIds">Its options, in the order they were declared.</param>
/// <param name="MaterialId">A <c>text</c> attribute, which is deliberately not an axis.</param>
/// <param name="ColourCode">The colour attribute's machine code, for a Step 19 attribute filter.</param>
internal sealed record CatalogTaxonomy(
    Guid RootCategoryId,
    Guid CategoryId,
    string CategorySlug,
    Guid BrandId,
    string BrandSlug,
    Guid ColourId,
    IReadOnlyList<Guid> ColourOptionIds,
    Guid MaterialId,
    string ColourCode = "");

/// <summary>A product that exists, and the ids a test needs to act on it.</summary>
/// <param name="Id">The product.</param>
/// <param name="Slug">Its URL segment — the storefront routes on this.</param>
/// <param name="VariantId">Its first variant.</param>
/// <param name="Sku">That variant's SKU.</param>
/// <param name="VendorId">The seller who owns it, or null for a platform-owned product.</param>
internal sealed record DraftedProduct(Guid Id, string Slug, Guid VariantId, string Sku, Guid? VendorId);

/// <summary>
/// Builds the catalogue a Step 10 test stands on, through the API a merchandiser would use.
/// </summary>
/// <remarks>
/// <para>
/// The same reasoning as <see cref="VendorScenario"/>, and for the same reason: a taxonomy written
/// as rows is a taxonomy without the materialised path the handler computes, without the slug
/// normalisation, and without the option reconciliation that a variant is pinned to. A test
/// standing on those would be standing on a catalogue the product could never have produced.
/// </para>
/// <para>
/// Every name it mints carries a fresh suffix. The collection shares one database, so two tests
/// building "a category called Cushions" must not be building the same one — the slug is unique per
/// tenant and the second would be refused.
/// </para>
/// </remarks>
/// <param name="admin">A client signed in as platform staff.</param>
/// <param name="cancellationToken">Cancellation token.</param>
internal sealed class CatalogScenario(HttpClient admin, CancellationToken cancellationToken)
{
    /// <summary>The client this scenario drives, for a test that wants to carry on from here.</summary>
    public HttpClient Admin => admin;

    /// <summary>A category, brand and attribute vocabulary nothing else is using.</summary>
    /// <remarks>
    /// Two levels of category rather than one, because "everything under Home &amp; Kitchen" is the
    /// query the materialised path exists for, and a single root would prove nothing about it.
    /// </remarks>
    public async Task<CatalogTaxonomy> TaxonomyAsync()
    {
        var root = await CategoryAsync("Home & Kitchen");
        var leaf = await CategoryAsync("Cushion Covers", root.Id);

        var brand = await BrandAsync();

        var colour = await AttributeAsync(
            "Colour",
            "Select",
            isVariantDefining: true,
            options: [Option("beige", "Beige", 0), Option("charcoal", "Charcoal", 1)]);

        var material = await AttributeAsync("Material", "Text", isVariantDefining: false);

        return new CatalogTaxonomy(
            root.Id,
            leaf.Id,
            leaf.Slug,
            brand.Id,
            brand.Slug,
            colour.GetProperty("id").GetGuid(),
            [.. colour.GetProperty("options").EnumerateArray().Select(option => option.GetProperty("id").GetGuid())],
            material.GetProperty("id").GetGuid(),
            colour.GetProperty("code").GetString() ?? string.Empty);
    }

    /// <summary>Creates a category, optionally beneath another.</summary>
    /// <param name="name">What it is called. A suffix is appended so the slug is unique.</param>
    /// <param name="parentId">Its parent, or null for a root.</param>
    public async Task<(Guid Id, string Slug, string Path, int Level)> CategoryAsync(
        string name,
        Guid? parentId = null)
    {
        var created = await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/categories",
                new
                {
                    parentId,
                    name = $"{name} {Suffix()}",
                    slug = (string?)null,
                    description = (string?)null,
                    imageFileId = (Guid?)null,
                    attributeSetId = (Guid?)null,
                    position = 0,
                    isActive = true,
                    seo = (object?)null,
                },
                cancellationToken),
            cancellationToken);

        return (
            created.GetProperty("id").GetGuid(),
            created.GetProperty("slug").GetString()!,
            created.GetProperty("path").GetString()!,
            created.GetProperty("level").GetInt32());
    }

    /// <summary>Moves a category under a new parent, or to the root when told null.</summary>
    /// <param name="categoryId">The category to move.</param>
    /// <param name="parentId">Its new parent.</param>
    /// <param name="name">Its name, which the update also carries.</param>
    public Task<HttpResponseMessage> MoveCategoryAsync(Guid categoryId, Guid? parentId, string name)
        => admin.PutAsJsonAsync(
            $"/api/v1/admin/categories/{categoryId}",
            new
            {
                parentId,
                name,
                slug = (string?)null,
                description = (string?)null,
                imageFileId = (Guid?)null,
                attributeSetId = (Guid?)null,
                position = 0,
                isActive = true,
                seo = (object?)null,
            },
            cancellationToken);

    /// <summary>Reads one category, for the path and level a move is asserted on.</summary>
    /// <param name="categoryId">The category.</param>
    public async Task<(string Path, int Level, Guid? ParentId)> ReadCategoryAsync(Guid categoryId)
    {
        var category = await Rest.ReadAsync(
            await admin.GetAsync(new Uri($"/api/v1/admin/categories/{categoryId}", UriKind.Relative), cancellationToken),
            cancellationToken);

        var parent = category.GetProperty("parentId");

        return (
            category.GetProperty("path").GetString()!,
            category.GetProperty("level").GetInt32(),
            parent.ValueKind == JsonValueKind.Null ? null : parent.GetGuid());
    }

    /// <summary>Creates a brand nothing else is using.</summary>
    public async Task<(Guid Id, string Slug)> BrandAsync()
    {
        var created = await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/brands",
                new
                {
                    name = $"Klara Living {Suffix()}",
                    slug = (string?)null,
                    description = (string?)null,
                    logoFileId = (Guid?)null,
                    isActive = true,
                    seo = (object?)null,
                },
                cancellationToken),
            cancellationToken);

        return (created.GetProperty("id").GetGuid(), created.GetProperty("slug").GetString()!);
    }

    /// <summary>Declares an attribute, with its options for a list type.</summary>
    /// <param name="name">The shopper-facing label. A suffix is appended so the code is unique.</param>
    /// <param name="dataType">One of the six data types, as the API names them.</param>
    /// <param name="isVariantDefining">Whether it is offered as a variant axis.</param>
    /// <param name="options">Its permitted values.</param>
    public async Task<JsonElement> AttributeAsync(
        string name,
        string dataType,
        bool isVariantDefining,
        object[]? options = null)
        => await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/attributes",
                new
                {
                    code = (string?)null,
                    name = $"{name} {Suffix()}",
                    dataType,
                    unit = (string?)null,
                    isVariantDefining,
                    isFilterable = true,
                    isSearchable = false,
                    isRequired = false,
                    position = 0,
                    options = options ?? [],
                },
                cancellationToken),
            cancellationToken);

    /// <summary>One permitted value of a list attribute.</summary>
    /// <param name="value">The machine value.</param>
    /// <param name="label">What a shopper sees.</param>
    /// <param name="position">Sort order.</param>
    public static object Option(string value, string label, int position)
        => new { id = (Guid?)null, value, label, swatchHex = (string?)null, position };

    /// <summary>Uploads an image into the public bucket and answers its file id.</summary>
    /// <param name="name">A readable file name.</param>
    public async Task<Guid> ImageAsync(string name)
    {
        var uploaded = await Rest.ReadAsync(
            await Rest.UploadAsync(admin, Rest.Png(600, 600), name, "image/png", "public", cancellationToken),
            cancellationToken);

        return uploaded.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Drafts a product whose mandatory disclosures are complete, with one variant.
    /// </summary>
    /// <remarks>
    /// Complete by default, because a test about the buy box or about scope should not be failing
    /// on Legal Metrology. The tests that are about the disclosures ask for them to be left out.
    /// </remarks>
    /// <param name="taxonomy">The category, brand and attributes to describe it with.</param>
    /// <param name="vendorId">The seller it belongs to, or null for a platform-owned product.</param>
    /// <param name="compliant">Whether to supply the mandatory disclosures at all.</param>
    /// <param name="client">The client to create it through. Defaults to the scenario's own.</param>
    /// <param name="name">
    /// The product's title, or null for the scenario's own default. Named so a test that needs a
    /// particular token in the product's own name — to prove a weighted rank against the same token
    /// appearing only in a category or a brand — can ask for one without writing the whole product.
    /// </param>
    public async Task<DraftedProduct> DraftAsync(
        CatalogTaxonomy taxonomy,
        Guid? vendorId = null,
        bool compliant = true,
        HttpClient? client = null,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(taxonomy);

        var caller = client ?? admin;
        var suffix = Suffix();

        var created = await Rest.ReadAsync(
            await caller.PostAsJsonAsync(
                "/api/v1/admin/products",
                new
                {
                    name = $"{name ?? "Cotton Cushion Cover"} {suffix}",
                    slug = (string?)null,
                    categoryId = taxonomy.CategoryId,
                    brandId = taxonomy.BrandId,
                    vendorId,
                    shortDescription = "A 40x40 cotton cushion cover.",
                    description = "Woven in Panipat from long-staple cotton.",
                    hsnCode = compliant ? "630222" : null,
                    gstRate = 5m,
                    countryOfOrigin = compliant ? "IN" : null,
                    manufacturer = compliant
                        ? new { name = "Klara Textiles Pvt Ltd", address = "Plot 9, Panipat, Haryana 132103", contact = "care@klarahome.test" }
                        : null,
                    packer = (object?)null,
                    importer = (object?)null,
                    isReturnable = true,
                    returnWindowDays = 7,
                    warranty = (string?)null,
                    specifications = new[] { new { label = "Weave", value = "Panama", group = (string?)null } },
                    seo = (object?)null,
                    attributes = Array.Empty<object>(),
                },
                cancellationToken),
            cancellationToken);

        var productId = created.GetProperty("id").GetGuid();

        var variant = await VariantAsync(
            productId,
            taxonomy,
            optionIndex: 0,
            compliant: compliant,
            client: caller);

        return new DraftedProduct(
            productId,
            created.GetProperty("slug").GetString()!,
            variant.GetProperty("id").GetGuid(),
            variant.GetProperty("sku").GetString()!,
            vendorId);
    }

    /// <summary>Adds a variant, on one of the colour axis's options.</summary>
    /// <param name="productId">The product.</param>
    /// <param name="taxonomy">The vocabulary its options come from.</param>
    /// <param name="optionIndex">Which colour, by position in the declared list.</param>
    /// <param name="compliant">Whether to supply the pack declarations.</param>
    /// <param name="mrp">Its maximum retail price.</param>
    /// <param name="client">The client to create it through.</param>
    /// <param name="mediaFileId">An image for its own gallery.</param>
    public async Task<JsonElement> VariantAsync(
        Guid productId,
        CatalogTaxonomy taxonomy,
        int optionIndex,
        bool compliant = true,
        decimal mrp = 1299m,
        HttpClient? client = null,
        Guid? mediaFileId = null)
    {
        ArgumentNullException.ThrowIfNull(taxonomy);

        var caller = client ?? admin;

        return await Rest.ReadAsync(
            await caller.PostAsJsonAsync(
                $"/api/v1/admin/products/{productId}/variants",
                new
                {
                    sku = (string?)null,
                    barcode = (string?)null,
                    nameSuffix = optionIndex == 0 ? "Beige" : "Charcoal",
                    mrp,
                    netQuantity = compliant ? "1 N" : null,
                    shelfLifeDays = (int?)null,
                    expiresOn = (DateOnly?)null,
                    weightGrams = compliant ? 220 : 0,
                    lengthMm = 400,
                    widthMm = 400,
                    heightMm = 20,
                    position = optionIndex,
                    isDefault = optionIndex == 0,
                    options = new[]
                    {
                        new { attributeId = taxonomy.ColourId, optionId = taxonomy.ColourOptionIds[optionIndex] },
                    },
                    media = mediaFileId is { } file
                        ? new[] { new { fileId = file, kind = "Image", altText = "The cover, folded.", position = 0 } }
                        : null,
                },
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Makes a variant sellable.
    /// </summary>
    /// <remarks>
    /// A variant is created <c>Draft</c> and stays there until somebody says otherwise, so this is
    /// a step every path to a live offer has to take: an offer is refused while its variant is not
    /// active, and a draft variant never reaches a product page.
    /// </remarks>
    /// <param name="variantId">The variant.</param>
    /// <param name="client">The client to act as. Defaults to the scenario's own.</param>
    public Task<HttpResponseMessage> ActivateVariantAsync(Guid variantId, HttpClient? client = null)
        => (client ?? admin).PostAsJsonAsync(
            $"/api/v1/admin/variants/{variantId}/activate",
            new { },
            cancellationToken);

    /// <summary>Replaces a product's gallery.</summary>
    /// <param name="productId">The product.</param>
    /// <param name="fileId">The image it should end up with.</param>
    public Task<HttpResponseMessage> SetProductMediaAsync(Guid productId, Guid fileId)
        => admin.PutAsJsonAsync(
            $"/api/v1/admin/products/{productId}/media",
            new
            {
                media = new[]
                {
                    new { fileId, kind = "Image", altText = "The cover on a sofa.", position = 0 },
                },
            },
            cancellationToken);

    /// <summary>Moves a product through one life-cycle transition.</summary>
    /// <param name="productId">The product.</param>
    /// <param name="transition">The route segment: <c>submit</c>, <c>approve</c>, <c>publish</c>…</param>
    /// <param name="notes">The reviewer's note, for the transitions that carry one.</param>
    /// <param name="client">The client to act as. Defaults to the scenario's own.</param>
    public Task<HttpResponseMessage> TransitionAsync(
        Guid productId,
        string transition,
        string? notes = null,
        HttpClient? client = null)
        => (client ?? admin).PostAsJsonAsync(
            $"/api/v1/admin/products/{productId}/{transition}",
            new { notes },
            cancellationToken);

    /// <summary>Publishes a platform-owned product outright, as staff do.</summary>
    /// <param name="productId">The product.</param>
    public async Task PublishAsync(Guid productId)
    {
        var published = await Rest.ReadAsync(await TransitionAsync(productId, "submit"), cancellationToken);

        Assert.Equal("Active", published.GetProperty("status").GetString());
    }

    /// <summary>Opens an offer against a variant, and activates it unless told not to.</summary>
    /// <param name="vendorId">The seller. Null when the caller is the seller.</param>
    /// <param name="variantId">The variant being offered.</param>
    /// <param name="sellingPrice">What they are asking.</param>
    /// <param name="mrp">The declared MRP, or null to take the variant's.</param>
    /// <param name="handlingTimeHours">How long they need before dispatch.</param>
    /// <param name="activate">Whether to put it on the storefront.</param>
    /// <param name="client">The client to act as. Defaults to the scenario's own.</param>
    public async Task<Guid> OfferAsync(
        Guid? vendorId,
        Guid variantId,
        decimal sellingPrice,
        decimal? mrp = null,
        int handlingTimeHours = 24,
        bool activate = true,
        HttpClient? client = null)
    {
        var caller = client ?? admin;

        var created = await Rest.ReadAsync(
            await caller.PostAsJsonAsync(
                "/api/v1/admin/listings",
                new
                {
                    variantId,
                    vendorId,
                    mrp,
                    sellingPrice,
                    vendorSku = (string?)null,
                    handlingTimeHours,
                    isCodAllowed = true,
                    maxOrderQuantity = (int?)null,
                },
                cancellationToken),
            cancellationToken);

        var listingId = created.GetProperty("id").GetGuid();

        if (activate)
        {
            await Rest.ReadAsync(
                await caller.PostAsJsonAsync(
                    $"/api/v1/admin/listings/{listingId}/activate",
                    new { reason = (string?)null },
                    cancellationToken),
                cancellationToken);
        }

        return listingId;
    }

    /// <summary>
    /// Puts stock behind an offer, so a checkout for it does not refuse with
    /// <c>CART_ITEM_OUT_OF_STOCK</c>.
    /// </summary>
    /// <remarks>
    /// A listing opens with a stock row and nothing in it — the same honest position the checkout
    /// itself takes about a hole in a rate card — so a Step 16 test that needs to place a real order
    /// has to receive stock exactly as a seller would: open a location, then a stock row against it,
    /// then a purchase receipt. One warehouse is opened per scenario instance and reused, since two
    /// tests opening "a location called Central" in the same collection must not collide.
    /// </remarks>
    /// <param name="listingId">The offer.</param>
    /// <param name="quantity">How many units to receive.</param>
    /// <param name="vendorId">The seller the location belongs to, or null for a platform warehouse.</param>
    public async Task StockAsync(Guid listingId, int quantity, Guid? vendorId = null)
    {
        var warehouseId = await WarehouseIdAsync(vendorId);

        var stockItem = await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/stock",
                new { listingId, warehouseId },
                cancellationToken),
            cancellationToken);

        var stockItemId = stockItem.GetProperty("id").GetGuid();

        await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/stock/adjustments",
                new { stockItemId, change = quantity, reason = "Adjustment", note = "Received for a Step 16 test." },
                cancellationToken),
            cancellationToken);
    }

    /// <summary>A warehouse for the given seller (or the platform), opened once and reused.</summary>
    private async Task<Guid> WarehouseIdAsync(Guid? vendorId)
    {
        if (_warehouseIds.TryGetValue(vendorId ?? Guid.Empty, out var existing))
        {
            return existing;
        }

        var created = await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/warehouses",
                new
                {
                    vendorId,
                    code = $"WH-{Suffix()}",
                    name = "Central",
                    pincode = "500034",
                    address = (object?)null,
                    priority = 0,
                },
                cancellationToken),
            cancellationToken);

        var id = created.GetProperty("id").GetGuid();
        _warehouseIds[vendorId ?? Guid.Empty] = id;
        return id;
    }

    private readonly Dictionary<Guid, Guid> _warehouseIds = [];

    /// <summary>A short suffix nothing else in the collection is using.</summary>
    private static string Suffix() => Guid.NewGuid().ToString("N")[..8];
}
