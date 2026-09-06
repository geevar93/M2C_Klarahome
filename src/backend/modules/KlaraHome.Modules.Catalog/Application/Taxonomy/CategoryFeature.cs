using FluentValidation;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Catalog.Application.Validation;
using KlaraHome.Modules.Catalog.Domain;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Catalog.Application.Taxonomy;

/// <summary>Crawler metadata, as the API states and accepts it.</summary>
/// <param name="MetaTitle">The page title.</param>
/// <param name="MetaDescription">The meta description.</param>
/// <param name="MetaKeywords">Comma-separated keywords.</param>
/// <param name="CanonicalUrl">The canonical URL, where this page duplicates another.</param>
/// <param name="NoIndex">Whether crawlers are asked to skip it.</param>
internal sealed record SeoPayload(
    string? MetaTitle,
    string? MetaDescription,
    string? MetaKeywords,
    string? CanonicalUrl,
    bool NoIndex);

/// <summary>A category, as the admin surface reads it.</summary>
/// <param name="Id">The category.</param>
/// <param name="ParentId">Its parent, or null for a root.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Slug">Its URL segment.</param>
/// <param name="Description">Copy for the category page.</param>
/// <param name="Path">The materialised path.</param>
/// <param name="Level">Depth in the tree.</param>
/// <param name="Position">Sort order among siblings.</param>
/// <param name="IsActive">Whether shoppers see it.</param>
/// <param name="ImageFileId">The tile image.</param>
/// <param name="AttributeSetId">The attribute set its products are described with.</param>
/// <param name="Seo">Crawler metadata.</param>
/// <param name="ProductCount">How many live products browse under it, including descendants.</param>
internal sealed record CategoryResponse(
    Guid Id,
    Guid? ParentId,
    string Name,
    string Slug,
    string? Description,
    string Path,
    int Level,
    int Position,
    bool IsActive,
    Guid? ImageFileId,
    Guid? AttributeSetId,
    SeoPayload Seo,
    int ProductCount);

/// <summary>A node of the browse tree, with its children nested beneath it.</summary>
/// <param name="Id">The category.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Slug">Its URL segment.</param>
/// <param name="Level">Depth in the tree.</param>
/// <param name="Position">Sort order among siblings.</param>
/// <param name="IsActive">Whether shoppers see it.</param>
/// <param name="ImageFileId">The tile image.</param>
/// <param name="Children">Its children, in display order.</param>
internal sealed record CategoryNode(
    Guid Id,
    string Name,
    string Slug,
    int Level,
    int Position,
    bool IsActive,
    Guid? ImageFileId,
    IReadOnlyList<CategoryNode> Children);

/// <summary>Reads the browse tree, or a subtree of it.</summary>
/// <param name="ParentId">The node to start from, or null for the whole tree.</param>
/// <param name="Depth">How many levels below the start to return. Null for all of them.</param>
/// <param name="ActiveOnly">Whether to leave out the hidden categories.</param>
internal sealed record GetCategoryTreeQuery(Guid? ParentId, int? Depth, bool ActiveOnly)
    : IQuery<IReadOnlyList<CategoryNode>>;

/// <summary>Reads one category by id.</summary>
/// <param name="CategoryId">The category.</param>
internal sealed record GetCategoryQuery(Guid CategoryId) : IQuery<CategoryResponse>;

/// <summary>Reads one category by slug, for the storefront.</summary>
/// <param name="Slug">Its URL segment.</param>
internal sealed record GetCategoryBySlugQuery(string Slug) : IQuery<CategoryResponse>;

/// <summary>Creates a category.</summary>
/// <param name="ParentId">Its parent, or null for a root.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Slug">A slug to use, or null to derive one from the name.</param>
/// <param name="Description">Copy for the category page.</param>
/// <param name="ImageFileId">The tile image.</param>
/// <param name="AttributeSetId">The attribute set its products are described with.</param>
/// <param name="Position">Sort order among siblings.</param>
/// <param name="Seo">Crawler metadata.</param>
internal sealed record CreateCategoryCommand(
    Guid? ParentId,
    string Name,
    string? Slug,
    string? Description,
    Guid? ImageFileId,
    Guid? AttributeSetId,
    int Position,
    SeoPayload? Seo) : ICommand<CategoryResponse>;

/// <summary>Renames a category or moves it.</summary>
/// <param name="CategoryId">The category.</param>
/// <param name="ParentId">Its new parent, or null to make it a root.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Slug">Its URL segment.</param>
/// <param name="Description">Copy for the category page.</param>
/// <param name="ImageFileId">The tile image.</param>
/// <param name="AttributeSetId">The attribute set its products are described with.</param>
/// <param name="Position">Sort order among siblings.</param>
/// <param name="IsActive">Whether shoppers see it.</param>
/// <param name="Seo">Crawler metadata.</param>
internal sealed record UpdateCategoryCommand(
    Guid CategoryId,
    Guid? ParentId,
    string Name,
    string? Slug,
    string? Description,
    Guid? ImageFileId,
    Guid? AttributeSetId,
    int Position,
    bool IsActive,
    SeoPayload? Seo) : ICommand<CategoryResponse>;

/// <summary>Retires a category.</summary>
/// <param name="CategoryId">The category.</param>
internal sealed record DeleteCategoryCommand(Guid CategoryId) : ICommand;

/// <summary>Rejects a category whose name or slug could never be stored.</summary>
internal sealed class CreateCategoryValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(160);
        RuleFor(command => command.Slug)
            .Matches(CatalogFormats.Slug())
            .MaximumLength(180)
            .When(command => !string.IsNullOrWhiteSpace(command.Slug))
            .WithMessage("A slug is lowercase letters, digits and single hyphens.");
        RuleFor(command => command.Position).GreaterThanOrEqualTo(0);
    }
}

/// <summary>The same rules, for a change.</summary>
internal sealed class UpdateCategoryValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryValidator()
    {
        RuleFor(command => command.CategoryId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(160);
        RuleFor(command => command.Slug)
            .Matches(CatalogFormats.Slug())
            .MaximumLength(180)
            .When(command => !string.IsNullOrWhiteSpace(command.Slug))
            .WithMessage("A slug is lowercase letters, digits and single hyphens.");
        RuleFor(command => command.Position).GreaterThanOrEqualTo(0);
    }
}

/// <summary>Turns categories into responses. One projection, so no two endpoints disagree.</summary>
internal static class CategoryProjection
{
    /// <summary>Reads a payload into the domain's own type.</summary>
    /// <param name="payload">What the caller sent, or null.</param>
    public static SeoMetadata ToSeo(SeoPayload? payload)
        => new()
        {
            MetaTitle = payload?.MetaTitle,
            MetaDescription = payload?.MetaDescription,
            MetaKeywords = payload?.MetaKeywords,
            CanonicalUrl = payload?.CanonicalUrl,
            NoIndex = payload?.NoIndex ?? false,
        };

    /// <summary>States the domain's own type as the API describes it.</summary>
    /// <param name="seo">The stored metadata.</param>
    public static SeoPayload ToPayload(SeoMetadata? seo)
        => new(seo?.MetaTitle, seo?.MetaDescription, seo?.MetaKeywords, seo?.CanonicalUrl, seo?.NoIndex ?? false);

    /// <summary>States a category.</summary>
    /// <param name="category">The category.</param>
    /// <param name="productCount">How many live products browse under it.</param>
    public static CategoryResponse ToResponse(Category category, int productCount = 0)
    {
        ArgumentNullException.ThrowIfNull(category);

        return new CategoryResponse(
            category.Id,
            category.ParentId,
            category.Name,
            category.Slug,
            category.Description,
            category.Path,
            category.Level,
            category.Position,
            category.IsActive,
            category.ImageFileId,
            category.AttributeSetId,
            ToPayload(category.Seo),
            productCount);
    }
}

/// <summary>Reads the browse tree.</summary>
/// <remarks>
/// One query, then the tree is assembled in memory. The whole taxonomy of a marketplace this size
/// is a few hundred rows; a recursive query per level would be several round trips for a result
/// that fits in a cache line's worth of objects.
/// </remarks>
/// <param name="context">The Catalog data context.</param>
internal sealed class GetCategoryTreeQueryHandler(CatalogDbContext context)
    : IQueryHandler<GetCategoryTreeQuery, IReadOnlyList<CategoryNode>>
{
    public async Task<Result<IReadOnlyList<CategoryNode>>> HandleAsync(
        GetCategoryTreeQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var categories = context.Categories.AsNoTracking().AsQueryable();

        if (query.ActiveOnly)
        {
            categories = categories.Where(category => category.IsActive);
        }

        var rootLevel = 0;

        if (query.ParentId is { } parentId)
        {
            var parent = await context.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(category => category.Id == parentId, cancellationToken)
                .ConfigureAwait(false);

            if (parent is null)
            {
                return CatalogErrors.NotFound("category");
            }

            rootLevel = parent.Level + 1;

            // The reason the path exists: one range scan instead of a recursive walk.
            categories = categories.Where(category =>
                category.Id != parentId && category.Path.StartsWith(parent.Path));
        }

        if (query.Depth is { } depth and > 0)
        {
            var deepest = rootLevel + depth - 1;
            categories = categories.Where(category => category.Level <= deepest);
        }

        var rows = await categories
            .OrderBy(category => category.Level)
            .ThenBy(category => category.Position)
            .ThenBy(category => category.Name)
            .Select(category => new
            {
                category.Id,
                category.ParentId,
                category.Name,
                category.Slug,
                category.Level,
                category.Position,
                category.IsActive,
                category.ImageFileId,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var childrenOf = rows
            .Where(row => row.Level > rootLevel)
            .GroupBy(row => row.ParentId)
            .ToDictionary(group => group.Key!.Value, group => group.ToList());

        List<CategoryNode> Build(Guid id) =>
        [
            .. childrenOf.TryGetValue(id, out var children)
                ? children.Select(child => new CategoryNode(
                    child.Id,
                    child.Name,
                    child.Slug,
                    child.Level,
                    child.Position,
                    child.IsActive,
                    child.ImageFileId,
                    Build(child.Id)))
                : [],
        ];

        IReadOnlyList<CategoryNode> tree =
        [
            .. rows
                .Where(row => row.Level == rootLevel)
                .Select(row => new CategoryNode(
                    row.Id,
                    row.Name,
                    row.Slug,
                    row.Level,
                    row.Position,
                    row.IsActive,
                    row.ImageFileId,
                    Build(row.Id))),
        ];

        return Result.Success(tree);
    }
}

/// <summary>Reads one category by id.</summary>
/// <param name="context">The Catalog data context.</param>
internal sealed class GetCategoryQueryHandler(CatalogDbContext context)
    : IQueryHandler<GetCategoryQuery, CategoryResponse>
{
    public async Task<Result<CategoryResponse>> HandleAsync(
        GetCategoryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var category = await context.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == query.CategoryId, cancellationToken)
            .ConfigureAwait(false);

        return category is null
            ? CatalogErrors.NotFound("category")
            : Result.Success(CategoryProjection.ToResponse(
                category,
                await CountProductsAsync(context, category, cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>Counts the live products under a node and everything below it.</summary>
    /// <param name="context">The Catalog data context.</param>
    /// <param name="category">The node.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task<int> CountProductsAsync(
        CatalogDbContext context,
        Category category,
        CancellationToken cancellationToken)
    {
        var descendants = context.Categories
            .AsNoTracking()
            .Where(candidate => candidate.Path.StartsWith(category.Path))
            .Select(candidate => candidate.Id);

        return await context.Products
            .AsNoTracking()
            .Where(product => product.Status == ProductStatus.Active && descendants.Contains(product.CategoryId))
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Reads one category by slug.</summary>
/// <param name="context">The Catalog data context.</param>
internal sealed class GetCategoryBySlugQueryHandler(CatalogDbContext context)
    : IQueryHandler<GetCategoryBySlugQuery, CategoryResponse>
{
    public async Task<Result<CategoryResponse>> HandleAsync(
        GetCategoryBySlugQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var slug = query.Slug.Trim().ToLowerInvariant();

        var category = await context.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Slug == slug, cancellationToken)
            .ConfigureAwait(false);

        // A hidden category answers 404 rather than an empty page: the storefront must not publish
        // the existence of a taxonomy the merchandiser has not finished building.
        return category is null || !category.IsActive
            ? CatalogErrors.NotFound("category")
            : Result.Success(CategoryProjection.ToResponse(
                category,
                await GetCategoryQueryHandler
                    .CountProductsAsync(context, category, cancellationToken)
                    .ConfigureAwait(false)));
    }
}

/// <summary>Creates a category.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="audit">Records the change.</param>
internal sealed class CreateCategoryCommandHandler(CatalogDbContext context, IAuditLogger audit)
    : ICommandHandler<CreateCategoryCommand, CategoryResponse>
{
    /// <summary>The audited action for a new category.</summary>
    public const string AuditAction = "catalog.category.created";

    /// <summary>The entity type recorded against every category action.</summary>
    public const string AuditEntityType = "Category";

    public async Task<Result<CategoryResponse>> HandleAsync(
        CreateCategoryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Category? parent = null;

        if (command.ParentId is { } parentId)
        {
            parent = await context.Categories
                .FirstOrDefaultAsync(category => category.Id == parentId, cancellationToken)
                .ConfigureAwait(false);

            if (parent is null)
            {
                return CatalogErrors.NotFound("parent category");
            }

            if (parent.Level + 1 >= Category.MaxDepth)
            {
                return CatalogErrors.CategoryTooDeep(Category.MaxDepth);
            }
        }

        var slug = string.IsNullOrWhiteSpace(command.Slug)
            ? CatalogFormats.ToSlug(command.Name)
            : command.Slug.Trim().ToLowerInvariant();

        if (slug.Length == 0)
        {
            return Error.Validation(
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["slug"] = ["That name produces an empty slug. Supply one explicitly."],
                });
        }

        if (await context.Categories.AnyAsync(c => c.Slug == slug, cancellationToken).ConfigureAwait(false))
        {
            return CatalogErrors.Duplicate("slug");
        }

        var category = Category.Create(command.Name.Trim(), slug, parent);

        category.Describe(
            command.Name.Trim(),
            slug,
            command.Description,
            command.ImageFileId,
            command.AttributeSetId,
            CategoryProjection.ToSeo(command.Seo));

        category.MoveTo(command.Position);

        context.Categories.Add(category);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AuditEntityType,
                EntityId = category.Id.ToString(),
                After = new { category.Name, category.Slug, category.ParentId, category.Path },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(CategoryProjection.ToResponse(category));
    }
}

/// <summary>Renames a category or moves it.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="audit">Records the change.</param>
internal sealed class UpdateCategoryCommandHandler(CatalogDbContext context, IAuditLogger audit)
    : ICommandHandler<UpdateCategoryCommand, CategoryResponse>
{
    /// <summary>The audited action for a change to a category.</summary>
    public const string AuditAction = "catalog.category.updated";

    public async Task<Result<CategoryResponse>> HandleAsync(
        UpdateCategoryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var category = await context.Categories
            .FirstOrDefaultAsync(candidate => candidate.Id == command.CategoryId, cancellationToken)
            .ConfigureAwait(false);

        if (category is null)
        {
            return CatalogErrors.NotFound("category");
        }

        var before = new { category.Name, category.Slug, category.ParentId, category.Path };

        var slug = string.IsNullOrWhiteSpace(command.Slug)
            ? category.Slug
            : command.Slug.Trim().ToLowerInvariant();

        if (!string.Equals(slug, category.Slug, StringComparison.Ordinal)
            && await context.Categories
                .AnyAsync(c => c.Slug == slug && c.Id != category.Id, cancellationToken)
                .ConfigureAwait(false))
        {
            return CatalogErrors.Duplicate("slug");
        }

        if (command.ParentId != category.ParentId)
        {
            var moved = await MoveAsync(category, command.ParentId, cancellationToken).ConfigureAwait(false);

            if (moved.IsFailure)
            {
                return moved.Error;
            }
        }

        category.Describe(
            command.Name.Trim(),
            slug,
            command.Description,
            command.ImageFileId,
            command.AttributeSetId,
            CategoryProjection.ToSeo(command.Seo));

        category.MoveTo(command.Position);
        category.SetActive(command.IsActive);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreateCategoryCommandHandler.AuditEntityType,
                EntityId = category.Id.ToString(),
                Before = before,
                After = new { category.Name, category.Slug, category.ParentId, category.Path },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(CategoryProjection.ToResponse(category));
    }

    /// <summary>
    /// Moves a subtree, rewriting every descendant's path.
    /// </summary>
    /// <remarks>
    /// The descendants are loaded and rewritten here rather than in the entity, which cannot see
    /// them. It is the one operation in this module whose cost is proportional to the size of a
    /// subtree, and it is rare enough — a taxonomy is reorganised a handful of times a year — to be
    /// worth the read that makes every browse query a range scan.
    /// </remarks>
    private async Task<Result> MoveAsync(Category category, Guid? newParentId, CancellationToken cancellationToken)
    {
        Category? parent = null;

        if (newParentId is { } parentId)
        {
            parent = await context.Categories
                .FirstOrDefaultAsync(candidate => candidate.Id == parentId, cancellationToken)
                .ConfigureAwait(false);

            if (parent is null)
            {
                return Result.Failure(CatalogErrors.NotFound("parent category"));
            }

            if (category.WouldCycleUnder(parent))
            {
                return Result.Failure(CatalogErrors.CategoryCycle);
            }
        }

        var oldPath = category.Path;
        var oldLevel = category.Level;

        var descendants = await context.Categories
            .Where(candidate => candidate.Id != category.Id && candidate.Path.StartsWith(oldPath))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var deepest = descendants.Count == 0 ? oldLevel : descendants.Max(candidate => candidate.Level);
        var newLevel = parent is null ? 0 : parent.Level + 1;

        if (newLevel + (deepest - oldLevel) >= Category.MaxDepth)
        {
            return Result.Failure(CatalogErrors.CategoryTooDeep(Category.MaxDepth));
        }

        category.Reparent(parent);

        foreach (var descendant in descendants)
        {
            descendant.RebaseUnder(oldPath, category.Path, newLevel - oldLevel);
        }

        return Result.Success();
    }
}

/// <summary>Retires a category.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="audit">Records the change.</param>
internal sealed class DeleteCategoryCommandHandler(CatalogDbContext context, IClock clock, IAuditLogger audit)
    : ICommandHandler<DeleteCategoryCommand>
{
    /// <summary>The audited action for a retired category.</summary>
    public const string AuditAction = "catalog.category.deleted";

    public async Task<Result> HandleAsync(DeleteCategoryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var category = await context.Categories
            .FirstOrDefaultAsync(candidate => candidate.Id == command.CategoryId, cancellationToken)
            .ConfigureAwait(false);

        if (category is null)
        {
            return Result.Failure(CatalogErrors.NotFound("category"));
        }

        // Refused rather than cascaded, both times. A category with children whose parent quietly
        // vanished leaves orphans no screen can reach, and a category whose products silently
        // became uncategorised is a catalogue nobody can browse — and neither is undoable.
        if (await context.Categories
                .AnyAsync(child => child.ParentId == category.Id, cancellationToken)
                .ConfigureAwait(false))
        {
            return Result.Failure(CatalogErrors.StillInUse("child categories"));
        }

        if (await context.Products
                .AnyAsync(product => product.CategoryId == category.Id, cancellationToken)
                .ConfigureAwait(false))
        {
            return Result.Failure(CatalogErrors.StillInUse("products"));
        }

        category.Delete(clock.UtcNow);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreateCategoryCommandHandler.AuditEntityType,
                EntityId = category.Id.ToString(),
                Before = new { category.Name, category.Slug },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
