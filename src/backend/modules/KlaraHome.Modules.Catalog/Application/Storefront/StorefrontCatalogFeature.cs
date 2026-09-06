using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Catalog.Application.Products;
using KlaraHome.Modules.Catalog.Domain;
using KlaraHome.Modules.Catalog.Infrastructure.BuyBox;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Catalog.Application.Storefront;

/// <summary>One seller's offer, as a shopper sees it.</summary>
/// <param name="ListingId">The offer. This is what a cart line points at.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="VendorName">The name shoppers see.</param>
/// <param name="VendorSlug">Their storefront path segment.</param>
/// <param name="VendorRating">Their average review score.</param>
/// <param name="Mrp">The declared MRP, always displayed (statutory in India).</param>
/// <param name="SellingPrice">What they are asking, inclusive of GST.</param>
/// <param name="DiscountPercent">How far below MRP that is, rounded for display.</param>
/// <param name="IsCodAllowed">Whether they accept cash on delivery.</param>
/// <param name="MaxOrderQuantity">The most units one order may take.</param>
/// <param name="DispatchHours">How long before they hand the parcel over.</param>
/// <param name="IsBuyBox">Whether this is the offer the page opens on.</param>
internal sealed record StorefrontOffer(
    Guid ListingId,
    Guid VendorId,
    string VendorName,
    string VendorSlug,
    decimal? VendorRating,
    decimal Mrp,
    decimal SellingPrice,
    int DiscountPercent,
    bool IsCodAllowed,
    int? MaxOrderQuantity,
    int DispatchHours,
    bool IsBuyBox);

/// <summary>A variant, as a shopper sees it.</summary>
/// <param name="Id">The variant.</param>
/// <param name="Sku">Its SKU.</param>
/// <param name="NameSuffix">What distinguishes it.</param>
/// <param name="NetQuantity">The declared net quantity (Legal Metrology).</param>
/// <param name="Mrp">Its MRP.</param>
/// <param name="IsDefault">Whether the page opens on it.</param>
/// <param name="Options">Its defining combination, for the swatch row.</param>
/// <param name="Media">Its own gallery.</param>
/// <param name="BuyBox">The winning offer, or null when nobody is currently selling it.</param>
/// <param name="OfferCount">How many sellers are offering it.</param>
internal sealed record StorefrontVariant(
    Guid Id,
    string Sku,
    string? NameSuffix,
    string? NetQuantity,
    decimal Mrp,
    bool IsDefault,
    IReadOnlyList<AttributeValueResponse> Options,
    IReadOnlyList<MediaResponse> Media,
    StorefrontOffer? BuyBox,
    int OfferCount);

/// <summary>A product page.</summary>
/// <param name="Id">The product.</param>
/// <param name="Name">Its title.</param>
/// <param name="Slug">Its URL segment.</param>
/// <param name="CategoryId">The category it browses under.</param>
/// <param name="BrandId">Its brand.</param>
/// <param name="ShortDescription">The one-line summary.</param>
/// <param name="Description">The long description.</param>
/// <param name="Specifications">The specification table.</param>
/// <param name="Attributes">Its described properties.</param>
/// <param name="Media">Its gallery.</param>
/// <param name="Variants">Its sellable variants, each with its buy box.</param>
/// <param name="HsnCode">The HSN code the GST rate is resolved from.</param>
/// <param name="GstRate">The GST percentage. Prices are shown inclusive of it.</param>
/// <param name="CountryOfOrigin">Where it was made. A mandatory disclosure.</param>
/// <param name="Manufacturer">Who made it. A mandatory disclosure.</param>
/// <param name="Packer">Who packed it.</param>
/// <param name="Importer">Who imported it. Mandatory for imported goods.</param>
/// <param name="IsReturnable">Whether it may be returned.</param>
/// <param name="ReturnWindowDays">Its own return window, or null to use the store's.</param>
/// <param name="Warranty">The warranty statement.</param>
/// <param name="Seo">Crawler metadata.</param>
/// <param name="RatingAverage">Its average review score.</param>
/// <param name="RatingCount">How many reviews that is over.</param>
internal sealed record StorefrontProduct(
    Guid Id,
    string Name,
    string Slug,
    Guid CategoryId,
    Guid? BrandId,
    string? ShortDescription,
    string? Description,
    IReadOnlyList<SpecificationPayload> Specifications,
    IReadOnlyList<AttributeValueResponse> Attributes,
    IReadOnlyList<MediaResponse> Media,
    IReadOnlyList<StorefrontVariant> Variants,
    string? HsnCode,
    decimal GstRate,
    string? CountryOfOrigin,
    PartyPayload Manufacturer,
    PartyPayload Packer,
    PartyPayload Importer,
    bool IsReturnable,
    int? ReturnWindowDays,
    string? Warranty,
    Taxonomy.SeoPayload Seo,
    decimal? RatingAverage,
    int RatingCount);

/// <summary>Reads a product page by slug.</summary>
/// <param name="Slug">Its URL segment.</param>
internal sealed record GetStorefrontProductQuery(string Slug) : IQuery<StorefrontProduct>;

/// <summary>Reads every seller's offer for one variant, in buy-box order.</summary>
/// <param name="Slug">The product's URL segment.</param>
/// <param name="VariantId">The variant, or null for the default one.</param>
internal sealed record GetStorefrontOffersQuery(string Slug, Guid? VariantId)
    : IQuery<IReadOnlyList<StorefrontOffer>>;

/// <summary>
/// Assembles a product page and resolves its buy boxes.
/// </summary>
/// <remarks>
/// <para>
/// The one read on this platform that genuinely spans three modules' worth of facts: the product
/// (here), the sellers behind each offer (Vendors, over <see cref="IVendorDirectory"/>) and the
/// buy-box rule (Platform, over <see cref="IStoreSettings"/>). All three are batched — one
/// directory call for every seller on the page, one settings read for the whole page — because
/// this is the most-requested authenticated-or-not endpoint the storefront has.
/// </para>
/// <para>
/// Stock is deliberately not consulted. Inventory does not exist until Step 11, and the buy-box
/// resolver treats "unknown" as neutral rather than as out of stock, so the criterion is a no-op
/// until it does.
/// </para>
/// </remarks>
/// <param name="context">The Catalog data context.</param>
/// <param name="reader">Assembles the product itself.</param>
/// <param name="vendors">Resolves the sellers behind the offers.</param>
/// <param name="settings">Supplies the configured buy-box rule.</param>
internal sealed class StorefrontCatalogService(
    CatalogDbContext context,
    ProductReader reader,
    IVendorDirectory vendors,
    IStoreSettings settings)
{
    /// <summary>The published product behind a slug, or null.</summary>
    /// <param name="slug">Its URL segment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Product?> FindPublishedAsync(string slug, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slug);

        var normalised = slug.Trim().ToLowerInvariant();

        var product = await context.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Slug == normalised, cancellationToken)
            .ConfigureAwait(false);

        // An unpublished product answers 404 rather than an empty page: the storefront must not
        // confirm the existence of a draft somebody has not finished writing.
        return product is { Status: ProductStatus.Active } ? product : null;
    }

    /// <summary>Every live offer for a set of variants, in buy-box order, keyed by variant.</summary>
    /// <param name="variantIds">The variants.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Dictionary<Guid, List<StorefrontOffer>>> OffersAsync(
        IReadOnlyList<Guid> variantIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(variantIds);

        if (variantIds.Count == 0)
        {
            return [];
        }

        var listings = await context.Listings
            .AsNoTracking()
            .Where(listing => variantIds.Contains(listing.VariantId) && listing.Status == ListingStatus.Active)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (listings.Count == 0)
        {
            return [];
        }

        // One directory call for every seller on the page, not one per offer.
        var sellers = await vendors
            .FindManyAsync(
                [.. listings.Select(listing => listing.VendorId!.Value).Distinct()],
                cancellationToken)
            .ConfigureAwait(false);

        var rule = await settings.GetAsync<BuyBoxSettings>(cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<Guid, List<StorefrontOffer>>();

        foreach (var group in listings.GroupBy(listing => listing.VariantId))
        {
            var candidates = new List<BuyBoxCandidate>();
            var byId = new Dictionary<Guid, Listing>();

            foreach (var listing in group)
            {
                // A seller the directory does not know, or one who has stopped trading since the
                // listing went live, is dropped rather than shown. The withdrawal event may not have
                // been dispatched yet, and a shopper must never be offered a seller who cannot ship.
                if (!sellers.TryGetValue(listing.VendorId!.Value, out var seller) || !seller.IsActive)
                {
                    continue;
                }

                byId[listing.Id] = listing;

                candidates.Add(new BuyBoxCandidate(
                    listing.Id,
                    listing.VendorId!.Value,
                    listing.SellingPrice.Amount,
                    seller.Rating,
                    Math.Max(listing.HandlingTimeHours, seller.DispatchSlaHours),
                    HasStock: null,
                    listing.PublishedAt));
            }

            if (candidates.Count == 0)
            {
                continue;
            }

            var ranked = BuyBoxResolver.Rank(candidates, rule);
            var offers = new List<StorefrontOffer>(ranked.Count);

            for (var index = 0; index < ranked.Count; index++)
            {
                var candidate = ranked[index];
                var listing = byId[candidate.ListingId];
                var seller = sellers[candidate.VendorId];

                offers.Add(new StorefrontOffer(
                    listing.Id,
                    seller.Id,
                    seller.DisplayName,
                    seller.Slug,
                    seller.Rating,
                    listing.Mrp.Amount,
                    listing.SellingPrice.Amount,
                    DiscountPercent(listing.Mrp.Amount, listing.SellingPrice.Amount),
                    listing.IsCodAllowed,
                    listing.MaxOrderQuantity,
                    candidate.DispatchSlaHours,
                    index == 0));
            }

            result[group.Key] = offers;
        }

        return result;
    }

    /// <summary>Assembles the whole product page.</summary>
    /// <param name="product">The published product.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<StorefrontProduct> ToPageAsync(Product product, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(product);

        var detail = await reader.ReadAsync(product.Id, cancellationToken).ConfigureAwait(false);

        // Only the sellable variants reach a shopper. A draft or archived variant is part of the
        // product's admin view and has no place in a swatch row.
        var sellable = detail!.Variants.Where(variant => variant.Status == VariantStatus.Active).ToList();

        var offers = await OffersAsync(sellable.ConvertAll(variant => variant.Id), cancellationToken)
            .ConfigureAwait(false);

        return new StorefrontProduct(
            detail.Id,
            detail.Name,
            detail.Slug,
            detail.CategoryId,
            detail.BrandId,
            detail.ShortDescription,
            detail.Description,
            detail.Specifications,
            detail.Attributes,
            detail.Media,
            [
                .. sellable.Select(variant =>
                {
                    var forVariant = offers.GetValueOrDefault(variant.Id) ?? [];

                    return new StorefrontVariant(
                        variant.Id,
                        variant.Sku,
                        variant.NameSuffix,
                        variant.NetQuantity,
                        variant.Mrp,
                        variant.IsDefault,
                        variant.Options,
                        variant.Media,
                        forVariant.Count > 0 ? forVariant[0] : null,
                        forVariant.Count);
                }),
            ],
            detail.HsnCode,
            detail.GstRate,
            detail.CountryOfOrigin,
            detail.Manufacturer,
            detail.Packer,
            detail.Importer,
            detail.IsReturnable,
            detail.ReturnWindowDays,
            detail.Warranty,
            detail.Seo,
            detail.RatingAverage,
            detail.RatingCount);
    }

    /// <summary>
    /// How far below MRP an offer is, as whole percent.
    /// </summary>
    /// <remarks>
    /// Rounded down rather than to nearest, so a 9.6% saving is advertised as 9% and never as 10%.
    /// Overstating a discount is exactly the practice the Consumer Protection (E-Commerce) Rules
    /// 2020 exist to prevent, and rounding is not a defensible reason to do it.
    /// </remarks>
    /// <param name="mrp">The declared MRP.</param>
    /// <param name="price">What is being asked.</param>
    internal static int DiscountPercent(decimal mrp, decimal price)
        => mrp <= 0m || price >= mrp ? 0 : (int)Math.Floor((mrp - price) / mrp * 100m);
}

/// <summary>Reads a product page.</summary>
/// <param name="service">Assembles the page and resolves its buy boxes.</param>
internal sealed class GetStorefrontProductQueryHandler(StorefrontCatalogService service)
    : IQueryHandler<GetStorefrontProductQuery, StorefrontProduct>
{
    public async Task<Result<StorefrontProduct>> HandleAsync(
        GetStorefrontProductQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var product = await service.FindPublishedAsync(query.Slug, cancellationToken).ConfigureAwait(false);

        return product is null
            ? CatalogErrors.NotFound("product")
            : Result.Success(await service.ToPageAsync(product, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Reads every seller's offer for one variant.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="service">Resolves the offers and their order.</param>
internal sealed class GetStorefrontOffersQueryHandler(CatalogDbContext context, StorefrontCatalogService service)
    : IQueryHandler<GetStorefrontOffersQuery, IReadOnlyList<StorefrontOffer>>
{
    public async Task<Result<IReadOnlyList<StorefrontOffer>>> HandleAsync(
        GetStorefrontOffersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var product = await service.FindPublishedAsync(query.Slug, cancellationToken).ConfigureAwait(false);

        if (product is null)
        {
            return CatalogErrors.NotFound("product");
        }

        var variants = await context.Variants
            .AsNoTracking()
            .Where(variant => variant.ProductId == product.Id && variant.Status == VariantStatus.Active)
            .OrderByDescending(variant => variant.IsDefault)
            .ThenBy(variant => variant.Position)
            .Select(variant => variant.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var variantId = query.VariantId ?? variants.FirstOrDefault();

        if (variantId == Guid.Empty || !variants.Contains(variantId))
        {
            return CatalogErrors.NotFound("variant");
        }

        var offers = await service.OffersAsync([variantId], cancellationToken).ConfigureAwait(false);

        IReadOnlyList<StorefrontOffer> forVariant = offers.GetValueOrDefault(variantId) ?? [];

        return Result.Success(forVariant);
    }
}
