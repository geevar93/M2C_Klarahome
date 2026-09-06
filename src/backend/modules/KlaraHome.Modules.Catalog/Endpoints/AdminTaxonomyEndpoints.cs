using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Errors;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Catalog.Application.Taxonomy;
using KlaraHome.Modules.Catalog.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Catalog.Endpoints;

/// <summary>The body of a category.</summary>
/// <param name="ParentId">Its parent, or null for a root.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Slug">Its URL segment, or null to derive one from the name.</param>
/// <param name="Description">Copy for the category page.</param>
/// <param name="ImageFileId">The tile image.</param>
/// <param name="AttributeSetId">The attribute set its products are described with.</param>
/// <param name="Position">Sort order among siblings.</param>
/// <param name="IsActive">Whether shoppers see it.</param>
/// <param name="Seo">Crawler metadata.</param>
internal sealed record CategoryBody(
    Guid? ParentId,
    string Name,
    string? Slug,
    string? Description,
    Guid? ImageFileId,
    Guid? AttributeSetId,
    int Position,
    bool IsActive,
    SeoPayload? Seo);

/// <summary>The body of a brand.</summary>
/// <param name="Name">The brand name.</param>
/// <param name="Slug">Its URL segment, or null to derive one from the name.</param>
/// <param name="Description">The brand story.</param>
/// <param name="LogoFileId">Its logo.</param>
/// <param name="IsActive">Whether it is offered.</param>
/// <param name="Seo">Crawler metadata.</param>
internal sealed record BrandBody(
    string Name,
    string? Slug,
    string? Description,
    Guid? LogoFileId,
    bool IsActive,
    SeoPayload? Seo);

/// <summary>The body of an attribute.</summary>
/// <param name="Code">Its stable code, or null to derive one from the name.</param>
/// <param name="Name">The shopper-facing label.</param>
/// <param name="DataType">What kind of value it holds. Ignored on an update.</param>
/// <param name="Unit">The unit its numbers are in.</param>
/// <param name="IsVariantDefining">Whether it is a variant axis.</param>
/// <param name="IsFilterable">Whether it is offered as a filter.</param>
/// <param name="IsSearchable">Whether it feeds the search text.</param>
/// <param name="IsRequired">Whether a product must supply it before publishing.</param>
/// <param name="Position">Sort order.</param>
/// <param name="Options">Its permitted values, for a list attribute.</param>
internal sealed record AttributeBody(
    string? Code,
    string Name,
    AttributeDataType DataType,
    string? Unit,
    bool IsVariantDefining,
    bool IsFilterable,
    bool IsSearchable,
    bool IsRequired,
    int Position,
    IReadOnlyList<AttributeOptionPayload>? Options);

/// <summary>The body of an attribute set.</summary>
/// <param name="Code">Its stable code, or null to derive one from the name.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Description">What it is for.</param>
/// <param name="Attributes">What is in it.</param>
internal sealed record AttributeSetBody(
    string? Code,
    string Name,
    string? Description,
    IReadOnlyList<AttributeSetMemberPayload>? Attributes);

/// <summary>Query-string filters for the brand listing.</summary>
/// <param name="Search">A fragment of the name.</param>
/// <param name="ActiveOnly">Whether to leave out the retired ones.</param>
/// <param name="Cursor">Opaque token from the previous page.</param>
/// <param name="Size">Page size.</param>
internal sealed record BrandListFilter(string? Search, bool ActiveOnly, string? Cursor, int? Size);

/// <summary>Query-string filters for the attribute listing.</summary>
/// <param name="FilterableOnly">Only the attributes offered as filters.</param>
/// <param name="VariantDefiningOnly">Only the variant axes.</param>
internal sealed record AttributeListFilter(bool FilterableOnly, bool VariantDefiningOnly);

/// <summary>
/// The taxonomy surface: categories, brands, attributes and attribute sets
/// (docs/04-api-specification.md §4).
/// </summary>
/// <remarks>
/// Every route here is platform-staff-only on the write side. The taxonomy is shared by every
/// seller on the marketplace, so a seller reshaping the tree or renaming an attribute would reshape
/// everybody else's products — which is why <see cref="CatalogPermissions.TaxonomyManage"/> is not
/// in the vendor roles.
/// </remarks>
internal static class AdminTaxonomyEndpoints
{
    /// <summary>Maps the taxonomy surface beneath <c>/admin</c>.</summary>
    /// <param name="admin">The <c>/admin</c> group.</param>
    public static IEndpointRouteBuilder MapAdminTaxonomyEndpoints(this IEndpointRouteBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        MapCategories(admin.MapGroup("/categories").WithTags("Catalog"));
        MapBrands(admin.MapGroup("/brands").WithTags("Catalog"));
        MapAttributes(admin.MapGroup("/attributes").WithTags("Catalog"));
        MapAttributeSets(admin.MapGroup("/attribute-sets").WithTags("Catalog"));

        return admin;
    }

    private static void MapCategories(IEndpointRouteBuilder categories)
    {
        categories.MapGet("/", async (
                Guid? parentId,
                int? depth,
                bool? activeOnly,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new GetCategoryTreeQuery(parentId, depth, activeOnly ?? false);
                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCategoriesTree")
            .WithSummary("Returns the browse tree, or the subtree beneath one node.")
            .RequirePermission(CatalogPermissions.TaxonomyRead)
            .Produces<IReadOnlyList<CategoryNode>>();

        categories.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetCategoryQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCategoryGet")
            .WithSummary("Reads one category, with the number of live products beneath it.")
            .RequirePermission(CatalogPermissions.TaxonomyRead)
            .Produces<CategoryResponse>();

        categories.MapPost("/", async (CategoryBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var command = new CreateCategoryCommand(
                    body.ParentId,
                    body.Name,
                    body.Slug,
                    body.Description,
                    body.ImageFileId,
                    body.AttributeSetId,
                    body.Position,
                    body.Seo);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.Match(
                    category => Results.Created($"{context.Request.Path}/{category.Id}", category),
                    error => error.ToProblemResult(context));
            })
            .WithName("adminCategoryCreate")
            .WithSummary("Creates a category under an optional parent.")
            .RequirePermission(CatalogPermissions.TaxonomyManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<CategoryResponse>(StatusCodes.Status201Created);

        categories.MapPut("/{id:guid}", async (
                Guid id,
                CategoryBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdateCategoryCommand(
                    id,
                    body.ParentId,
                    body.Name,
                    body.Slug,
                    body.Description,
                    body.ImageFileId,
                    body.AttributeSetId,
                    body.Position,
                    body.IsActive,
                    body.Seo);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);
                return result.ToOk(context);
            })
            .WithName("adminCategoryUpdate")
            .WithSummary("Renames a category, or moves it and its whole subtree.")
            .RequirePermission(CatalogPermissions.TaxonomyManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<CategoryResponse>();

        categories.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new DeleteCategoryCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("adminCategoryDelete")
            .WithSummary("Retires a category. Refused while it has children or products.")
            .RequirePermission(CatalogPermissions.TaxonomyManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite);
    }

    private static void MapBrands(IEndpointRouteBuilder brands)
    {
        brands.MapGet("/", async (
                [AsParameters] BrandListFilter filter,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new ListBrandsQuery(filter.Search, filter.ActiveOnly, filter.Cursor, filter.Size);
                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminBrandsList")
            .WithSummary("Lists brands alphabetically.")
            .RequirePermission(CatalogPermissions.TaxonomyRead)
            .Produces<PagedResult<BrandResponse>>();

        brands.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetBrandQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminBrandGet")
            .WithSummary("Reads one brand.")
            .RequirePermission(CatalogPermissions.TaxonomyRead)
            .Produces<BrandResponse>();

        brands.MapPost("/", async (BrandBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var command = new CreateBrandCommand(body.Name, body.Slug, body.Description, body.LogoFileId, body.Seo);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.Match(
                    brand => Results.Created($"{context.Request.Path}/{brand.Id}", brand),
                    error => error.ToProblemResult(context));
            })
            .WithName("adminBrandCreate")
            .WithSummary("Registers a brand.")
            .RequirePermission(CatalogPermissions.TaxonomyManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<BrandResponse>(StatusCodes.Status201Created);

        brands.MapPut("/{id:guid}", async (Guid id, BrandBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var command = new UpdateBrandCommand(
                    id,
                    body.Name,
                    body.Slug,
                    body.Description,
                    body.LogoFileId,
                    body.IsActive,
                    body.Seo);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);
                return result.ToOk(context);
            })
            .WithName("adminBrandUpdate")
            .WithSummary("Updates a brand.")
            .RequirePermission(CatalogPermissions.TaxonomyManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<BrandResponse>();

        brands.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new DeleteBrandCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("adminBrandDelete")
            .WithSummary("Removes a brand. Refused while products still use it.")
            .RequirePermission(CatalogPermissions.TaxonomyManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite);
    }

    private static void MapAttributes(IEndpointRouteBuilder attributes)
    {
        attributes.MapGet("/", async (
                [AsParameters] AttributeListFilter filter,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new ListAttributesQuery(filter.FilterableOnly, filter.VariantDefiningOnly);
                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminAttributesList")
            .WithSummary("Lists attributes with their permitted values.")
            .RequirePermission(CatalogPermissions.TaxonomyRead)
            .Produces<IReadOnlyList<AttributeResponse>>();

        attributes.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetAttributeQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminAttributeGet")
            .WithSummary("Reads one attribute.")
            .RequirePermission(CatalogPermissions.TaxonomyRead)
            .Produces<AttributeResponse>();

        attributes.MapPost("/", async (AttributeBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var command = new CreateAttributeCommand(
                    body.Code,
                    body.Name,
                    body.DataType,
                    body.Unit,
                    body.IsVariantDefining,
                    body.IsFilterable,
                    body.IsSearchable,
                    body.IsRequired,
                    body.Position,
                    body.Options);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.Match(
                    attribute => Results.Created($"{context.Request.Path}/{attribute.Id}", attribute),
                    error => error.ToProblemResult(context));
            })
            .WithName("adminAttributeCreate")
            .WithSummary("Declares an attribute. Only select and multiselect can be variant axes.")
            .RequirePermission(CatalogPermissions.TaxonomyManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<AttributeResponse>(StatusCodes.Status201Created);

        attributes.MapPut("/{id:guid}", async (
                Guid id,
                AttributeBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdateAttributeCommand(
                    id,
                    body.Name,
                    body.Unit,
                    body.IsVariantDefining,
                    body.IsFilterable,
                    body.IsSearchable,
                    body.IsRequired,
                    body.Position,
                    body.Options);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);
                return result.ToOk(context);
            })
            .WithName("adminAttributeUpdate")
            .WithSummary("Updates an attribute and reconciles its options. The data type cannot change.")
            .RequirePermission(CatalogPermissions.TaxonomyManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<AttributeResponse>();

        attributes.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new DeleteAttributeCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("adminAttributeDelete")
            .WithSummary("Removes an attribute. Refused while anything still uses it.")
            .RequirePermission(CatalogPermissions.TaxonomyManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite);
    }

    private static void MapAttributeSets(IEndpointRouteBuilder sets)
    {
        sets.MapGet("/", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListAttributeSetsQuery(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminAttributeSetsList")
            .WithSummary("Lists attribute sets with their membership.")
            .RequirePermission(CatalogPermissions.TaxonomyRead)
            .Produces<IReadOnlyList<AttributeSetResponse>>();

        sets.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetAttributeSetQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminAttributeSetGet")
            .WithSummary("Reads one attribute set.")
            .RequirePermission(CatalogPermissions.TaxonomyRead)
            .Produces<AttributeSetResponse>();

        sets.MapPost("/", async (AttributeSetBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var command = new CreateAttributeSetCommand(body.Code, body.Name, body.Description, body.Attributes);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.Match(
                    set => Results.Created($"{context.Request.Path}/{set.Id}", set),
                    error => error.ToProblemResult(context));
            })
            .WithName("adminAttributeSetCreate")
            .WithSummary("Declares an attribute set — the form a category's products are described with.")
            .RequirePermission(CatalogPermissions.TaxonomyManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<AttributeSetResponse>(StatusCodes.Status201Created);

        sets.MapPut("/{id:guid}", async (
                Guid id,
                AttributeSetBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdateAttributeSetCommand(id, body.Name, body.Description, body.Attributes);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminAttributeSetUpdate")
            .WithSummary("Updates an attribute set and reconciles its membership.")
            .RequirePermission(CatalogPermissions.TaxonomyManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<AttributeSetResponse>();

        sets.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new DeleteAttributeSetCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("adminAttributeSetDelete")
            .WithSummary("Removes an attribute set. Refused while a category still uses it.")
            .RequirePermission(CatalogPermissions.TaxonomyManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite);
    }
}
