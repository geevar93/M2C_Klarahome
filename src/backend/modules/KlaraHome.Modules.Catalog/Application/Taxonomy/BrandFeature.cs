using FluentValidation;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Catalog.Application.Validation;
using KlaraHome.Modules.Catalog.Domain;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Catalog.Application.Taxonomy;

/// <summary>A brand, as the API states it.</summary>
/// <param name="Id">The brand.</param>
/// <param name="Name">The brand name.</param>
/// <param name="Slug">Its URL segment.</param>
/// <param name="Description">The brand story.</param>
/// <param name="LogoFileId">Its logo.</param>
/// <param name="IsActive">Whether it is offered.</param>
/// <param name="Seo">Crawler metadata.</param>
internal sealed record BrandResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    Guid? LogoFileId,
    bool IsActive,
    SeoPayload Seo);

/// <summary>Lists brands alphabetically.</summary>
/// <param name="Search">A fragment of the name.</param>
/// <param name="ActiveOnly">Whether to leave out the retired ones.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListBrandsQuery(string? Search, bool ActiveOnly, string? Cursor, int? Size)
    : IQuery<PagedResult<BrandResponse>>;

/// <summary>Reads one brand.</summary>
/// <param name="BrandId">The brand.</param>
internal sealed record GetBrandQuery(Guid BrandId) : IQuery<BrandResponse>;

/// <summary>Creates a brand.</summary>
/// <param name="Name">The brand name.</param>
/// <param name="Slug">A slug to use, or null to derive one from the name.</param>
/// <param name="Description">The brand story.</param>
/// <param name="LogoFileId">Its logo.</param>
/// <param name="Seo">Crawler metadata.</param>
internal sealed record CreateBrandCommand(
    string Name,
    string? Slug,
    string? Description,
    Guid? LogoFileId,
    SeoPayload? Seo) : ICommand<BrandResponse>;

/// <summary>Updates a brand.</summary>
/// <param name="BrandId">The brand.</param>
/// <param name="Name">The brand name.</param>
/// <param name="Slug">Its URL segment.</param>
/// <param name="Description">The brand story.</param>
/// <param name="LogoFileId">Its logo.</param>
/// <param name="IsActive">Whether it is offered.</param>
/// <param name="Seo">Crawler metadata.</param>
internal sealed record UpdateBrandCommand(
    Guid BrandId,
    string Name,
    string? Slug,
    string? Description,
    Guid? LogoFileId,
    bool IsActive,
    SeoPayload? Seo) : ICommand<BrandResponse>;

/// <summary>Removes a brand no product uses.</summary>
/// <param name="BrandId">The brand.</param>
internal sealed record DeleteBrandCommand(Guid BrandId) : ICommand;

/// <summary>Rejects a brand whose name or slug could never be stored.</summary>
internal sealed class CreateBrandValidator : AbstractValidator<CreateBrandCommand>
{
    public CreateBrandValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(160);
        RuleFor(command => command.Slug)
            .Matches(CatalogFormats.Slug())
            .MaximumLength(180)
            .When(command => !string.IsNullOrWhiteSpace(command.Slug))
            .WithMessage("A slug is lowercase letters, digits and single hyphens.");
    }
}

/// <summary>The same rules, for a change.</summary>
internal sealed class UpdateBrandValidator : AbstractValidator<UpdateBrandCommand>
{
    public UpdateBrandValidator()
    {
        RuleFor(command => command.BrandId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(160);
        RuleFor(command => command.Slug)
            .Matches(CatalogFormats.Slug())
            .MaximumLength(180)
            .When(command => !string.IsNullOrWhiteSpace(command.Slug))
            .WithMessage("A slug is lowercase letters, digits and single hyphens.");
    }
}

/// <summary>Turns brands into responses.</summary>
internal static class BrandProjection
{
    /// <summary>States a brand.</summary>
    /// <param name="brand">The brand.</param>
    public static BrandResponse ToResponse(Brand brand)
    {
        ArgumentNullException.ThrowIfNull(brand);

        return new BrandResponse(
            brand.Id,
            brand.Name,
            brand.Slug,
            brand.Description,
            brand.LogoFileId,
            brand.IsActive,
            CategoryProjection.ToPayload(brand.Seo));
    }
}

/// <summary>Lists brands.</summary>
/// <param name="context">The Catalog data context.</param>
internal sealed class ListBrandsQueryHandler(CatalogDbContext context)
    : IQueryHandler<ListBrandsQuery, PagedResult<BrandResponse>>
{
    public async Task<Result<PagedResult<BrandResponse>>> HandleAsync(
        ListBrandsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);
        var brands = context.Brands.AsNoTracking().AsQueryable();

        if (query.ActiveOnly)
        {
            brands = brands.Where(brand => brand.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{CatalogQueries.EscapeLike(query.Search)}%";
            brands = brands.Where(brand => EF.Functions.ILike(brand.Name, pattern, "\\"));
        }

        // Alphabetical rather than newest-first, because that is how a brand list is read. The name
        // is unique per tenant, so it is a stable keyset cursor on its own.
        if (Cursor.TryDecode(query.Cursor, out var after))
        {
            brands = brands.Where(brand => string.Compare(brand.Name, after) > 0);
        }

        var page = await brands
            .OrderBy(brand => brand.Name)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        return Result.Success(new PagedResult<BrandResponse>(
            [.. page.Select(BrandProjection.ToResponse)],
            new PageInfo(size, hasMore ? Cursor.Encode(page[^1].Name) : null)));
    }
}

/// <summary>Reads one brand.</summary>
/// <param name="context">The Catalog data context.</param>
internal sealed class GetBrandQueryHandler(CatalogDbContext context) : IQueryHandler<GetBrandQuery, BrandResponse>
{
    public async Task<Result<BrandResponse>> HandleAsync(GetBrandQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var brand = await context.Brands
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == query.BrandId, cancellationToken)
            .ConfigureAwait(false);

        return brand is null
            ? CatalogErrors.NotFound("brand")
            : Result.Success(BrandProjection.ToResponse(brand));
    }
}

/// <summary>Creates a brand.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="audit">Records the change.</param>
internal sealed class CreateBrandCommandHandler(CatalogDbContext context, IAuditLogger audit)
    : ICommandHandler<CreateBrandCommand, BrandResponse>
{
    /// <summary>The audited action for a new brand.</summary>
    public const string AuditAction = "catalog.brand.created";

    /// <summary>The entity type recorded against every brand action.</summary>
    public const string AuditEntityType = "Brand";

    public async Task<Result<BrandResponse>> HandleAsync(
        CreateBrandCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var name = command.Name.Trim();

        var slug = string.IsNullOrWhiteSpace(command.Slug)
            ? CatalogFormats.ToSlug(name)
            : command.Slug.Trim().ToLowerInvariant();

        if (slug.Length == 0)
        {
            return Error.Validation(
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["slug"] = ["That name produces an empty slug. Supply one explicitly."],
                });
        }

        var clash = await context.Brands
            .AnyAsync(brand => brand.Slug == slug || brand.Name == name, cancellationToken)
            .ConfigureAwait(false);

        if (clash)
        {
            return CatalogErrors.Duplicate("brand name or slug");
        }

        var created = Brand.Create(name, slug);
        created.Describe(name, slug, command.Description, command.LogoFileId, CategoryProjection.ToSeo(command.Seo));

        context.Brands.Add(created);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AuditEntityType,
                EntityId = created.Id.ToString(),
                After = new { created.Name, created.Slug },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(BrandProjection.ToResponse(created));
    }
}

/// <summary>Updates a brand.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="audit">Records the change.</param>
internal sealed class UpdateBrandCommandHandler(CatalogDbContext context, IAuditLogger audit)
    : ICommandHandler<UpdateBrandCommand, BrandResponse>
{
    /// <summary>The audited action for a change to a brand.</summary>
    public const string AuditAction = "catalog.brand.updated";

    public async Task<Result<BrandResponse>> HandleAsync(
        UpdateBrandCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var brand = await context.Brands
            .FirstOrDefaultAsync(candidate => candidate.Id == command.BrandId, cancellationToken)
            .ConfigureAwait(false);

        if (brand is null)
        {
            return CatalogErrors.NotFound("brand");
        }

        var before = new { brand.Name, brand.Slug, brand.IsActive };
        var name = command.Name.Trim();

        var slug = string.IsNullOrWhiteSpace(command.Slug)
            ? brand.Slug
            : command.Slug.Trim().ToLowerInvariant();

        var clash = await context.Brands
            .AnyAsync(
                candidate => candidate.Id != brand.Id && (candidate.Slug == slug || candidate.Name == name),
                cancellationToken)
            .ConfigureAwait(false);

        if (clash)
        {
            return CatalogErrors.Duplicate("brand name or slug");
        }

        brand.Describe(name, slug, command.Description, command.LogoFileId, CategoryProjection.ToSeo(command.Seo));
        brand.SetActive(command.IsActive);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreateBrandCommandHandler.AuditEntityType,
                EntityId = brand.Id.ToString(),
                Before = before,
                After = new { brand.Name, brand.Slug, brand.IsActive },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(BrandProjection.ToResponse(brand));
    }
}

/// <summary>Removes a brand.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="audit">Records the change.</param>
internal sealed class DeleteBrandCommandHandler(CatalogDbContext context, IAuditLogger audit)
    : ICommandHandler<DeleteBrandCommand>
{
    /// <summary>The audited action for a removed brand.</summary>
    public const string AuditAction = "catalog.brand.deleted";

    public async Task<Result> HandleAsync(DeleteBrandCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var brand = await context.Brands
            .FirstOrDefaultAsync(candidate => candidate.Id == command.BrandId, cancellationToken)
            .ConfigureAwait(false);

        if (brand is null)
        {
            return Result.Failure(CatalogErrors.NotFound("brand"));
        }

        // A hard delete, and therefore a refusal rather than a cascade. Brands are not on the
        // soft-delete list in docs/03-database-design.md §1 — nothing historical points at one, so
        // the row genuinely goes — which is exactly why a product must not be left pointing at it.
        if (await context.Products
                .AnyAsync(product => product.BrandId == brand.Id, cancellationToken)
                .ConfigureAwait(false))
        {
            return Result.Failure(CatalogErrors.StillInUse("products"));
        }

        context.Brands.Remove(brand);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreateBrandCommandHandler.AuditEntityType,
                EntityId = brand.Id.ToString(),
                Before = new { brand.Name, brand.Slug },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Query helpers shared by this module's list endpoints.</summary>
internal static class CatalogQueries
{
    /// <summary>
    /// Escapes a caller's search term for <c>ILIKE</c>.
    /// </summary>
    /// <remarks>
    /// Without it, a search for "50%" matches every row, and a search for "_" matches every row of
    /// length one. The backslash is escaped first, or escaping the wildcards would corrupt it.
    /// </remarks>
    /// <param name="term">What the caller typed.</param>
    public static string EscapeLike(string term)
    {
        ArgumentNullException.ThrowIfNull(term);

        return term.Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }
}
