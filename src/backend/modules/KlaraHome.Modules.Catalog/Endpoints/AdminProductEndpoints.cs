using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Errors;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Catalog.Application.Products;
using KlaraHome.Modules.Catalog.Application.Taxonomy;
using KlaraHome.Modules.Catalog.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Catalog.Endpoints;

/// <summary>Query-string filters for the product listing.</summary>
/// <param name="Status">Restrict to one life-cycle state.</param>
/// <param name="CategoryId">Restrict to one category and everything beneath it.</param>
/// <param name="BrandId">Restrict to one brand.</param>
/// <param name="VendorId">Restrict to one seller.</param>
/// <param name="Search">A fragment of the name or a SKU.</param>
/// <param name="Cursor">Opaque token from the previous page.</param>
/// <param name="Size">Page size.</param>
internal sealed record ProductListFilter(
    string? Status,
    Guid? CategoryId,
    Guid? BrandId,
    Guid? VendorId,
    string? Search,
    string? Cursor,
    int? Size);

/// <summary>The body of a product.</summary>
/// <param name="Name">Its title.</param>
/// <param name="Slug">Its URL segment, or null to derive one from the name.</param>
/// <param name="CategoryId">The category it browses under.</param>
/// <param name="BrandId">Its brand.</param>
/// <param name="VendorId">The seller it belongs to. Ignored on an update, and for a vendor caller.</param>
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
internal sealed record ProductBody(
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
    IReadOnlyList<AttributeValuePayload>? Attributes);

/// <summary>The body of a gallery replacement.</summary>
/// <param name="Media">The complete gallery the owner should end up with.</param>
internal sealed record MediaBody(IReadOnlyList<MediaPayload> Media);

/// <summary>The body of a variant.</summary>
/// <param name="Sku">Its SKU, or null on creation to take the next one from the sequence.</param>
/// <param name="Barcode">The barcode on the pack.</param>
/// <param name="NameSuffix">What distinguishes it.</param>
/// <param name="Mrp">Maximum retail price.</param>
/// <param name="NetQuantity">The declared net quantity.</param>
/// <param name="ShelfLifeDays">Shelf life in days, for a perishable.</param>
/// <param name="ExpiresOn">A fixed expiry date, where the goods carry one.</param>
/// <param name="WeightGrams">Dead weight.</param>
/// <param name="LengthMm">Packed length.</param>
/// <param name="WidthMm">Packed width.</param>
/// <param name="HeightMm">Packed height.</param>
/// <param name="Position">Sort order within the product.</param>
/// <param name="IsDefault">Whether the PDP should open on it.</param>
/// <param name="Options">Its defining combination.</param>
/// <param name="Media">Its own gallery.</param>
internal sealed record VariantBody(
    string? Sku,
    string? Barcode,
    string? NameSuffix,
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
    IReadOnlyList<VariantOptionPayload>? Options,
    IReadOnlyList<MediaPayload>? Media);

/// <summary>The body of a moderation decision.</summary>
/// <param name="Notes">The reviewer's note. Required for a rejection.</param>
internal sealed record ModerationBody(string? Notes);

/// <summary>Query-string filters for the moderation queue.</summary>
/// <param name="Status">Restrict to one outcome. Defaults to what is still pending.</param>
/// <param name="Cursor">Opaque token from the previous page.</param>
/// <param name="Size">Page size.</param>
internal sealed record ModerationListFilter(string? Status, string? Cursor, int? Size);

/// <summary>
/// The product surface: products, their variants, their galleries and the moderation queue
/// (docs/04-api-specification.md §4).
/// </summary>
/// <remarks>
/// A seller reaches every one of these routes, and the vendor scope — not a separate set of paths —
/// is what confines them. A vendor caller sees the platform's shared products and their own, may
/// write only to their own, and cannot approve anything at all: the life-cycle routes ask for
/// <see cref="CatalogPermissions.ProductModerate"/>, which no vendor role holds.
/// </remarks>
internal static class AdminProductEndpoints
{
    /// <summary>Maps the product surface beneath <c>/admin</c>.</summary>
    /// <param name="admin">The <c>/admin</c> group.</param>
    public static IEndpointRouteBuilder MapAdminProductEndpoints(this IEndpointRouteBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var products = admin.MapGroup("/products").WithTags("Catalog");

        MapProducts(products);
        MapVariants(products, admin.MapGroup("/variants").WithTags("Catalog"));
        MapLifecycle(products);
        MapModeration(admin.MapGroup("/product-moderation").WithTags("Catalog"));

        return admin;
    }

    private static void MapProducts(IEndpointRouteBuilder products)
    {
        products.MapGet("/", async (
                [AsParameters] ProductListFilter filter,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new ListProductsQuery(
                    filter.Status,
                    filter.CategoryId,
                    filter.BrandId,
                    filter.VendorId,
                    filter.Search,
                    filter.Cursor,
                    filter.Size);

                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminProductsList")
            .WithSummary("Lists products, newest first. A vendor caller sees their own and the platform's.")
            .RequirePermission(CatalogPermissions.ProductRead)
            .Produces<PagedResult<ProductListItem>>();

        products.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetProductQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminProductGet")
            .WithSummary("Reads one product with its variants, gallery, attributes and compliance gaps.")
            .RequirePermission(CatalogPermissions.ProductRead)
            .Produces<ProductResponse>();

        products.MapPost("/", async (ProductBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var command = new CreateProductCommand(
                    body.Name,
                    body.Slug,
                    body.CategoryId,
                    body.BrandId,
                    body.VendorId,
                    body.ShortDescription,
                    body.Description,
                    body.HsnCode,
                    body.GstRate,
                    body.CountryOfOrigin,
                    body.Manufacturer,
                    body.Packer,
                    body.Importer,
                    body.IsReturnable,
                    body.ReturnWindowDays,
                    body.Warranty,
                    body.Specifications,
                    body.Seo,
                    body.Attributes);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.Match(
                    product => Results.Created($"{context.Request.Path}/{product.Id}", product),
                    error => error.ToProblemResult(context));
            })
            .WithName("adminProductCreate")
            .WithSummary("Drafts a product. It is not sellable until it has a variant and is published.")
            .RequirePermission(CatalogPermissions.ProductManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ProductResponse>(StatusCodes.Status201Created);

        products.MapPut("/{id:guid}", async (
                Guid id,
                ProductBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdateProductCommand(
                    id,
                    body.Name,
                    body.Slug,
                    body.CategoryId,
                    body.BrandId,
                    body.ShortDescription,
                    body.Description,
                    body.HsnCode,
                    body.GstRate,
                    body.CountryOfOrigin,
                    body.Manufacturer,
                    body.Packer,
                    body.Importer,
                    body.IsReturnable,
                    body.ReturnWindowDays,
                    body.Warranty,
                    body.Specifications,
                    body.Seo,
                    body.Attributes);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);
                return result.ToOk(context);
            })
            .WithName("adminProductUpdate")
            .WithSummary("Updates a product's copy, taxonomy and compliance declarations.")
            .RequirePermission(CatalogPermissions.ProductManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ProductResponse>();

        products.MapPut("/{id:guid}/media", async (
                Guid id,
                MediaBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new SetProductMediaCommand(id, body.Media), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminProductSetMedia")
            .WithSummary("Replaces the product's gallery with the supplied, ordered list.")
            .RequirePermission(CatalogPermissions.ProductManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ProductResponse>();

        products.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new DeleteProductCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("adminProductDelete")
            .WithSummary("Retires a product, its variants and every offer against them.")
            .RequirePermission(CatalogPermissions.ProductManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite);
    }

    private static void MapVariants(IEndpointRouteBuilder products, IEndpointRouteBuilder variants)
    {
        products.MapPost("/{id:guid}/variants", async (
                Guid id,
                VariantBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new CreateVariantCommand(
                    id,
                    body.Sku,
                    body.Barcode,
                    body.NameSuffix,
                    body.Mrp,
                    body.NetQuantity,
                    body.ShelfLifeDays,
                    body.ExpiresOn,
                    body.WeightGrams,
                    body.LengthMm,
                    body.WidthMm,
                    body.HeightMm,
                    body.Position,
                    body.IsDefault,
                    body.Options,
                    body.Media);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.Match(
                    variant => Results.Created($"/variants/{variant.Id}", variant),
                    error => error.ToProblemResult(context));
            })
            .WithName("adminVariantCreate")
            .WithSummary("Adds a variant. Its combination of options must be unique within the product.")
            .RequirePermission(CatalogPermissions.ProductManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<VariantResponse>(StatusCodes.Status201Created);

        variants.MapPut("/{id:guid}", async (
                Guid id,
                VariantBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdateVariantCommand(
                    id,
                    body.Sku ?? string.Empty,
                    body.Barcode,
                    body.NameSuffix,
                    body.Mrp,
                    body.NetQuantity,
                    body.ShelfLifeDays,
                    body.ExpiresOn,
                    body.WeightGrams,
                    body.LengthMm,
                    body.WidthMm,
                    body.HeightMm,
                    body.Position,
                    body.IsDefault,
                    body.Options,
                    body.Media);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);
                return result.ToOk(context);
            })
            .WithName("adminVariantUpdate")
            .WithSummary("Updates a variant, its pack declarations and its defining combination.")
            .RequirePermission(CatalogPermissions.ProductManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<VariantResponse>();

        MapVariantStatus(variants, "activate", VariantStatus.Active, "adminVariantActivate",
            "Makes the variant sellable. Refused while its mandatory disclosures are incomplete.");

        MapVariantStatus(variants, "deactivate", VariantStatus.Inactive, "adminVariantDeactivate",
            "Withdraws the variant and pauses every offer against it.");

        variants.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new DeleteVariantCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("adminVariantDelete")
            .WithSummary("Retires a variant. Refused if it is the last active one of a published product.")
            .RequirePermission(CatalogPermissions.ProductManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite);
    }

    private static void MapVariantStatus(
        IEndpointRouteBuilder variants,
        string segment,
        VariantStatus status,
        string name,
        string summary)
        => variants.MapPost($"/{{id:guid}}/{segment}", async (
                Guid id,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new ChangeVariantStatusCommand(id, status), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName(name)
            .WithSummary(summary)
            .RequirePermission(CatalogPermissions.ProductManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<VariantResponse>();

    /// <summary>
    /// The product life cycle, one route per transition
    /// (Draft → PendingApproval → Active ⇄ Inactive → Archived).
    /// </summary>
    private static void MapLifecycle(IEndpointRouteBuilder products)
    {
        products.MapPost("/{id:guid}/submit", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new SubmitProductCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminProductSubmit")
            .WithSummary("Sends a product for moderation. Platform staff publish it outright.")
            .RequirePermission(CatalogPermissions.ProductManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ProductResponse>();

        products.MapPost("/{id:guid}/approve", async (
                Guid id,
                ModerationBody? body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new ApproveProductCommand(id, body?.Notes), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminProductApprove")
            .WithSummary("Approves a submitted product and publishes it.")
            .RequirePermission(CatalogPermissions.ProductModerate)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ProductResponse>();

        products.MapPost("/{id:guid}/reject", async (
                Guid id,
                ModerationBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new RejectProductCommand(id, body.Notes ?? string.Empty), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminProductReject")
            .WithSummary("Rejects a submitted product and returns it to draft. A reason is required.")
            .RequirePermission(CatalogPermissions.ProductModerate)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ProductResponse>();

        MapProductStatus(products, "publish", ProductStatus.Active, "adminProductPublish",
            "Puts a product on the storefront. Refused while its mandatory disclosures are incomplete.");

        MapProductStatus(products, "unpublish", ProductStatus.Inactive, "adminProductUnpublish",
            "Takes a product off the storefront and pauses every offer against it.");

        MapProductStatus(products, "archive", ProductStatus.Archived, "adminProductArchive",
            "Retires a product for good. Terminal.");
    }

    private static void MapProductStatus(
        IEndpointRouteBuilder products,
        string segment,
        ProductStatus status,
        string name,
        string summary)
        => products.MapPost($"/{{id:guid}}/{segment}", async (
                Guid id,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new ChangeProductStatusCommand(id, status), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName(name)
            .WithSummary(summary)
            .RequirePermission(CatalogPermissions.ProductModerate)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ProductResponse>();

    private static void MapModeration(IEndpointRouteBuilder moderation)
    {
        moderation.MapGet("/", async (
                [AsParameters] ModerationListFilter filter,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new ListModerationQueueQuery(filter.Status, filter.Cursor, filter.Size);
                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminProductModerationQueue")
            .WithSummary("The moderation queue, oldest submission first.")
            .RequirePermission(CatalogPermissions.ProductRead)
            .Produces<PagedResult<ModerationResponse>>();
    }
}
