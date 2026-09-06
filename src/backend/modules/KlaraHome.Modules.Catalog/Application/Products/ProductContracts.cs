using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Catalog.Application.Taxonomy;
using KlaraHome.Modules.Catalog.Domain;

namespace KlaraHome.Modules.Catalog.Application.Products;

/// <summary>A named party on a package, as the API states and accepts it.</summary>
/// <param name="Name">The company's name, as it appears on the pack.</param>
/// <param name="Address">Their full address, as one block.</param>
/// <param name="Contact">A consumer-care number or mailbox.</param>
internal sealed record PartyPayload(string? Name, string? Address, string? Contact);

/// <summary>One row of the specification table.</summary>
/// <param name="Label">What the row is called.</param>
/// <param name="Value">What it says.</param>
/// <param name="Group">An optional heading it files under.</param>
internal sealed record SpecificationPayload(string Label, string Value, string? Group);

/// <summary>A product-level attribute value, as a caller supplies it.</summary>
/// <param name="AttributeId">The attribute.</param>
/// <param name="Text">The raw value, for the text, number, boolean and date types.</param>
/// <param name="OptionId">The chosen option, for the list types.</param>
internal sealed record AttributeValuePayload(Guid AttributeId, string? Text, Guid? OptionId);

/// <summary>A gallery entry, as a caller supplies it.</summary>
/// <param name="FileId">The stored file.</param>
/// <param name="Kind">What kind of asset it is.</param>
/// <param name="AltText">The alt text.</param>
/// <param name="Position">Sort order; zero is primary.</param>
internal sealed record MediaPayload(Guid FileId, CatalogMediaKind Kind, string? AltText, int Position);

/// <summary>An attribute value, as the API states it.</summary>
/// <param name="AttributeId">The attribute.</param>
/// <param name="Code">Its stable code.</param>
/// <param name="Name">Its label.</param>
/// <param name="DataType">What kind of value it holds.</param>
/// <param name="Unit">The unit its numbers are in.</param>
/// <param name="Value">The value, rendered for display.</param>
/// <param name="OptionId">The chosen option, for the list types.</param>
internal sealed record AttributeValueResponse(
    Guid AttributeId,
    string Code,
    string Name,
    AttributeDataType DataType,
    string? Unit,
    string? Value,
    Guid? OptionId);

/// <summary>A gallery entry, as the API states it.</summary>
/// <param name="Id">The entry.</param>
/// <param name="FileId">The stored file.</param>
/// <param name="Kind">What kind of asset it is.</param>
/// <param name="AltText">The alt text.</param>
/// <param name="Position">Sort order.</param>
/// <param name="Url">Its public URL, resolved from the media library. Null if the file is gone.</param>
internal sealed record MediaResponse(
    Guid Id,
    Guid FileId,
    CatalogMediaKind Kind,
    string? AltText,
    int Position,
    string? Url);

/// <summary>A variant, as the admin surface reads it.</summary>
/// <param name="Id">The variant.</param>
/// <param name="ProductId">Its product.</param>
/// <param name="Sku">The stock-keeping unit.</param>
/// <param name="Barcode">The barcode on the pack.</param>
/// <param name="NameSuffix">What distinguishes it.</param>
/// <param name="Status">Where it is in its life.</param>
/// <param name="Mrp">Maximum retail price.</param>
/// <param name="NetQuantity">The declared net quantity.</param>
/// <param name="ShelfLifeDays">Shelf life in days, for a perishable.</param>
/// <param name="ExpiresOn">A fixed expiry date, where the goods carry one.</param>
/// <param name="WeightGrams">Dead weight.</param>
/// <param name="LengthMm">Packed length.</param>
/// <param name="WidthMm">Packed width.</param>
/// <param name="HeightMm">Packed height.</param>
/// <param name="Position">Sort order within the product.</param>
/// <param name="IsDefault">Whether the PDP opens on it.</param>
/// <param name="Options">Its defining combination.</param>
/// <param name="Media">Its own gallery.</param>
internal sealed record VariantResponse(
    Guid Id,
    Guid ProductId,
    string Sku,
    string? Barcode,
    string? NameSuffix,
    VariantStatus Status,
    decimal Mrp,
    string? NetQuantity,
    int? ShelfLifeDays,
    DateOnly? ExpiresOn,
    int WeightGrams,
    int LengthMm,
    int WidthMm,
    int HeightMm,
    int Position,
    bool IsDefault,
    IReadOnlyList<AttributeValueResponse> Options,
    IReadOnlyList<MediaResponse> Media);

/// <summary>A product in a list.</summary>
/// <param name="Id">The product.</param>
/// <param name="Name">Its title.</param>
/// <param name="Slug">Its URL segment.</param>
/// <param name="Status">Where it is in its life.</param>
/// <param name="CategoryId">The category it browses under.</param>
/// <param name="BrandId">Its brand.</param>
/// <param name="VendorId">The seller who owns it, or null for a platform-owned product.</param>
/// <param name="VariantCount">How many variants it has.</param>
/// <param name="ListingCount">How many live offers there are across all of them.</param>
/// <param name="PrimaryImageFileId">The image a card renders.</param>
/// <param name="RatingAverage">Its average review score.</param>
/// <param name="RatingCount">How many reviews that is over.</param>
/// <param name="PublishedAt">When it first went live.</param>
/// <param name="CreatedAt">When it was drafted.</param>
internal sealed record ProductListItem(
    Guid Id,
    string Name,
    string Slug,
    ProductStatus Status,
    Guid CategoryId,
    Guid? BrandId,
    Guid? VendorId,
    int VariantCount,
    int ListingCount,
    Guid? PrimaryImageFileId,
    decimal? RatingAverage,
    int RatingCount,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt);

/// <summary>Everything an administrator or the owning seller sees about a product.</summary>
/// <param name="Id">The product.</param>
/// <param name="Name">Its title.</param>
/// <param name="Slug">Its URL segment.</param>
/// <param name="Status">Where it is in its life.</param>
/// <param name="CategoryId">The category it browses under.</param>
/// <param name="BrandId">Its brand.</param>
/// <param name="VendorId">The seller who owns it, or null for a platform-owned product.</param>
/// <param name="ShortDescription">The one-line summary.</param>
/// <param name="Description">The long description.</param>
/// <param name="HsnCode">The HSN code.</param>
/// <param name="GstRate">The GST percentage.</param>
/// <param name="CountryOfOrigin">ISO 3166-1 alpha-2 origin.</param>
/// <param name="Manufacturer">Who made it.</param>
/// <param name="Packer">Who packed it.</param>
/// <param name="Importer">Who imported it.</param>
/// <param name="IsReturnable">Whether it may be returned.</param>
/// <param name="ReturnWindowDays">Its own return window.</param>
/// <param name="Warranty">The warranty statement.</param>
/// <param name="Specifications">The specification table.</param>
/// <param name="Seo">Crawler metadata.</param>
/// <param name="Attributes">Its described properties.</param>
/// <param name="Media">Its gallery.</param>
/// <param name="Variants">Its variants.</param>
/// <param name="RatingAverage">Its average review score.</param>
/// <param name="RatingCount">How many reviews that is over.</param>
/// <param name="ComplianceGaps">What still stands between it and publication. Empty when ready.</param>
/// <param name="PublishedAt">When it first went live.</param>
/// <param name="CreatedAt">When it was drafted.</param>
internal sealed record ProductResponse(
    Guid Id,
    string Name,
    string Slug,
    ProductStatus Status,
    Guid CategoryId,
    Guid? BrandId,
    Guid? VendorId,
    string? ShortDescription,
    string? Description,
    string? HsnCode,
    decimal GstRate,
    string? CountryOfOrigin,
    PartyPayload Manufacturer,
    PartyPayload Packer,
    PartyPayload Importer,
    bool IsReturnable,
    int? ReturnWindowDays,
    string? Warranty,
    IReadOnlyList<SpecificationPayload> Specifications,
    SeoPayload Seo,
    IReadOnlyList<AttributeValueResponse> Attributes,
    IReadOnlyList<MediaResponse> Media,
    IReadOnlyList<VariantResponse> Variants,
    decimal? RatingAverage,
    int RatingCount,
    IReadOnlyList<string> ComplianceGaps,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt);

/// <summary>Lists products, newest first.</summary>
/// <param name="Status">Restrict to one life-cycle state.</param>
/// <param name="CategoryId">Restrict to one category and everything beneath it.</param>
/// <param name="BrandId">Restrict to one brand.</param>
/// <param name="VendorId">Restrict to one seller. Ignored for a vendor caller, who has only their own.</param>
/// <param name="Search">A fragment of the name or a SKU.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListProductsQuery(
    string? Status,
    Guid? CategoryId,
    Guid? BrandId,
    Guid? VendorId,
    string? Search,
    string? Cursor,
    int? Size) : IQuery<PagedResult<ProductListItem>>;

/// <summary>Reads one product with its variants, gallery and attribute values.</summary>
/// <param name="ProductId">The product.</param>
internal sealed record GetProductQuery(Guid ProductId) : IQuery<ProductResponse>;

/// <summary>Drafts a product.</summary>
/// <param name="Name">Its title.</param>
/// <param name="Slug">A slug to use, or null to derive one from the name.</param>
/// <param name="CategoryId">The category it browses under.</param>
/// <param name="BrandId">Its brand.</param>
/// <param name="VendorId">The seller it belongs to. Ignored for a vendor caller, who gets their own.</param>
/// <param name="ShortDescription">The one-line summary.</param>
/// <param name="Description">The long description.</param>
/// <param name="HsnCode">The HSN code.</param>
/// <param name="GstRate">The GST percentage.</param>
/// <param name="CountryOfOrigin">ISO 3166-1 alpha-2 origin.</param>
/// <param name="Manufacturer">Who made it.</param>
/// <param name="Packer">Who packed it.</param>
/// <param name="Importer">Who imported it.</param>
/// <param name="IsReturnable">Whether it may be returned.</param>
/// <param name="ReturnWindowDays">Its own return window.</param>
/// <param name="Warranty">The warranty statement.</param>
/// <param name="Specifications">The specification table.</param>
/// <param name="Seo">Crawler metadata.</param>
/// <param name="Attributes">Its described properties.</param>
internal sealed record CreateProductCommand(
    string Name,
    string? Slug,
    Guid CategoryId,
    Guid? BrandId,
    Guid? VendorId,
    string? ShortDescription,
    string? Description,
    string? HsnCode,
    decimal GstRate,
    string? CountryOfOrigin,
    PartyPayload? Manufacturer,
    PartyPayload? Packer,
    PartyPayload? Importer,
    bool IsReturnable,
    int? ReturnWindowDays,
    string? Warranty,
    IReadOnlyList<SpecificationPayload>? Specifications,
    SeoPayload? Seo,
    IReadOnlyList<AttributeValuePayload>? Attributes) : ICommand<ProductResponse>;

/// <summary>Updates a product.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Name">Its title.</param>
/// <param name="Slug">Its URL segment.</param>
/// <param name="CategoryId">The category it browses under.</param>
/// <param name="BrandId">Its brand.</param>
/// <param name="ShortDescription">The one-line summary.</param>
/// <param name="Description">The long description.</param>
/// <param name="HsnCode">The HSN code.</param>
/// <param name="GstRate">The GST percentage.</param>
/// <param name="CountryOfOrigin">ISO 3166-1 alpha-2 origin.</param>
/// <param name="Manufacturer">Who made it.</param>
/// <param name="Packer">Who packed it.</param>
/// <param name="Importer">Who imported it.</param>
/// <param name="IsReturnable">Whether it may be returned.</param>
/// <param name="ReturnWindowDays">Its own return window.</param>
/// <param name="Warranty">The warranty statement.</param>
/// <param name="Specifications">The specification table.</param>
/// <param name="Seo">Crawler metadata.</param>
/// <param name="Attributes">Its described properties, or null to leave them alone.</param>
internal sealed record UpdateProductCommand(
    Guid ProductId,
    string Name,
    string? Slug,
    Guid CategoryId,
    Guid? BrandId,
    string? ShortDescription,
    string? Description,
    string? HsnCode,
    decimal GstRate,
    string? CountryOfOrigin,
    PartyPayload? Manufacturer,
    PartyPayload? Packer,
    PartyPayload? Importer,
    bool IsReturnable,
    int? ReturnWindowDays,
    string? Warranty,
    IReadOnlyList<SpecificationPayload>? Specifications,
    SeoPayload? Seo,
    IReadOnlyList<AttributeValuePayload>? Attributes) : ICommand<ProductResponse>;

/// <summary>Replaces a product's gallery.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Media">The complete gallery it should end up with.</param>
internal sealed record SetProductMediaCommand(Guid ProductId, IReadOnlyList<MediaPayload> Media)
    : ICommand<ProductResponse>;

/// <summary>Retires a product.</summary>
/// <param name="ProductId">The product.</param>
internal sealed record DeleteProductCommand(Guid ProductId) : ICommand;
