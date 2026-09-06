using FluentValidation;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Catalog.Application.Taxonomy;
using KlaraHome.Modules.Catalog.Application.Validation;
using KlaraHome.Modules.Catalog.Domain;
using KlaraHome.Modules.Catalog.Infrastructure;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Catalog.Application.Products;

/// <summary>Rejects a product that could never be stored.</summary>
internal sealed class CreateProductValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(300);
        RuleFor(command => command.CategoryId).NotEmpty();
        RuleFor(command => command.ShortDescription).MaximumLength(500);
        RuleFor(command => command.Warranty).MaximumLength(1000);
        RuleFor(command => command.GstRate).InclusiveBetween(0m, 100m);
        RuleFor(command => command.ReturnWindowDays)
            .InclusiveBetween(0, 365)
            .When(command => command.ReturnWindowDays is not null);

        RuleFor(command => command.Slug)
            .Matches(CatalogFormats.Slug())
            .MaximumLength(320)
            .When(command => !string.IsNullOrWhiteSpace(command.Slug))
            .WithMessage("A slug is lowercase letters, digits and single hyphens.");

        RuleFor(command => command.HsnCode)
            .Matches(CatalogFormats.HsnCode())
            .When(command => !string.IsNullOrWhiteSpace(command.HsnCode))
            .WithMessage("An HSN code is four, six or eight digits.");

        RuleFor(command => command.CountryOfOrigin)
            .Must(code => CatalogFormats.CountryCode().IsMatch(code!.Trim().ToUpperInvariant()))
            .When(command => !string.IsNullOrWhiteSpace(command.CountryOfOrigin))
            .WithMessage("A country of origin is a two-letter ISO 3166-1 code, for example IN.");
    }
}

/// <summary>The same rules, for a change.</summary>
internal sealed class UpdateProductValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductValidator()
    {
        RuleFor(command => command.ProductId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(300);
        RuleFor(command => command.CategoryId).NotEmpty();
        RuleFor(command => command.ShortDescription).MaximumLength(500);
        RuleFor(command => command.Warranty).MaximumLength(1000);
        RuleFor(command => command.GstRate).InclusiveBetween(0m, 100m);
        RuleFor(command => command.ReturnWindowDays)
            .InclusiveBetween(0, 365)
            .When(command => command.ReturnWindowDays is not null);

        RuleFor(command => command.Slug)
            .Matches(CatalogFormats.Slug())
            .MaximumLength(320)
            .When(command => !string.IsNullOrWhiteSpace(command.Slug))
            .WithMessage("A slug is lowercase letters, digits and single hyphens.");

        RuleFor(command => command.HsnCode)
            .Matches(CatalogFormats.HsnCode())
            .When(command => !string.IsNullOrWhiteSpace(command.HsnCode))
            .WithMessage("An HSN code is four, six or eight digits.");

        RuleFor(command => command.CountryOfOrigin)
            .Must(code => CatalogFormats.CountryCode().IsMatch(code!.Trim().ToUpperInvariant()))
            .When(command => !string.IsNullOrWhiteSpace(command.CountryOfOrigin))
            .WithMessage("A country of origin is a two-letter ISO 3166-1 code, for example IN.");
    }
}

/// <summary>Lists products.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="reader">Resolves the card image for each row.</param>
internal sealed class ListProductsQueryHandler(CatalogDbContext context, ProductReader reader)
    : IQueryHandler<ListProductsQuery, PagedResult<ProductListItem>>
{
    public async Task<Result<PagedResult<ProductListItem>>> HandleAsync(
        ListProductsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);

        // The global vendor filter has already confined a vendor caller to their own rows and the
        // platform's shared ones, so nothing here needs to repeat that.
        var products = context.Products.AsNoTracking().AsQueryable();

        if (Enum.TryParse<ProductStatus>(query.Status, ignoreCase: true, out var status))
        {
            products = products.Where(product => product.Status == status);
        }

        if (query.CategoryId is { } categoryId)
        {
            var category = await context.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Id == categoryId, cancellationToken)
                .ConfigureAwait(false);

            if (category is null)
            {
                return CatalogErrors.NotFound("category");
            }

            // The subtree, not just the node: "show me everything in Home & Kitchen" must include
            // the cushion covers filed three levels down.
            var descendants = context.Categories
                .AsNoTracking()
                .Where(candidate => candidate.Path.StartsWith(category.Path))
                .Select(candidate => candidate.Id);

            products = products.Where(product => descendants.Contains(product.CategoryId));
        }

        if (query.BrandId is { } brandId)
        {
            products = products.Where(product => product.BrandId == brandId);
        }

        if (query.VendorId is { } vendorId)
        {
            products = products.Where(product => product.VendorId == vendorId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{CatalogQueries.EscapeLike(query.Search)}%";

            // The name or a SKU. A merchandiser looking for a product usually has the SKU in front
            // of them, from a pick list or a support ticket, and not the marketing title.
            var matchingSku = context.Variants
                .AsNoTracking()
                .Where(variant => EF.Functions.ILike(variant.Sku, pattern, "\\"))
                .Select(variant => variant.ProductId);

            products = products.Where(product =>
                EF.Functions.ILike(product.Name, pattern, "\\") || matchingSku.Contains(product.Id));
        }

        // UUIDv7 ids are time-ordered, so the id alone is a stable keyset cursor for a newest-first
        // list: no second sort column, and no row skipped or repeated when a product is drafted
        // mid-page.
        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            products = products.Where(product => product.Id.CompareTo(after) < 0);
        }

        var page = await products
            .OrderByDescending(product => product.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        var ids = page.ConvertAll(product => product.Id);

        var variantCounts = await context.Variants
            .AsNoTracking()
            .Where(variant => ids.Contains(variant.ProductId))
            .GroupBy(variant => variant.ProductId)
            .Select(group => new { ProductId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.ProductId, row => row.Count, cancellationToken)
            .ConfigureAwait(false);

        var listingCounts = await context.Listings
            .AsNoTracking()
            .Where(listing => ids.Contains(listing.ProductId) && listing.Status == ListingStatus.Active)
            .GroupBy(listing => listing.ProductId)
            .Select(group => new { ProductId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.ProductId, row => row.Count, cancellationToken)
            .ConfigureAwait(false);

        var images = await reader.PrimaryImagesAsync(ids, cancellationToken).ConfigureAwait(false);

        return Result.Success(new PagedResult<ProductListItem>(
            [
                .. page.Select(product => new ProductListItem(
                    product.Id,
                    product.Name,
                    product.Slug,
                    product.Status,
                    product.CategoryId,
                    product.BrandId,
                    product.VendorId,
                    variantCounts.GetValueOrDefault(product.Id),
                    listingCounts.GetValueOrDefault(product.Id),
                    images.TryGetValue(product.Id, out var file) ? file : null,
                    product.RatingAverage,
                    product.RatingCount,
                    product.PublishedAt,
                    product.CreatedAt)),
            ],
            new PageInfo(size, hasMore ? Cursor.Encode(page[^1].Id.ToString()) : null)));
    }
}

/// <summary>Reads one product.</summary>
/// <param name="reader">Assembles the full picture.</param>
internal sealed class GetProductQueryHandler(ProductReader reader) : IQueryHandler<GetProductQuery, ProductResponse>
{
    public async Task<Result<ProductResponse>> HandleAsync(
        GetProductQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var product = await reader.ReadAsync(query.ProductId, cancellationToken).ConfigureAwait(false);

        return product is null ? CatalogErrors.NotFound("product") : Result.Success(product);
    }
}

/// <summary>Drafts a product.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="scope">Decides which seller the product belongs to.</param>
/// <param name="writer">Applies the attribute values.</param>
/// <param name="reader">States the result.</param>
/// <param name="audit">Records the change.</param>
internal sealed class CreateProductCommandHandler(
    CatalogDbContext context,
    CatalogScope scope,
    ProductWriter writer,
    ProductReader reader,
    IAuditLogger audit) : ICommandHandler<CreateProductCommand, ProductResponse>
{
    /// <summary>The audited action for a new product.</summary>
    public const string AuditAction = "catalog.product.created";

    /// <summary>The entity type recorded against every product action.</summary>
    public const string AuditEntityType = "Product";

    public async Task<Result<ProductResponse>> HandleAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await context.Categories
                .AnyAsync(category => category.Id == command.CategoryId, cancellationToken)
                .ConfigureAwait(false))
        {
            return CatalogErrors.NotFound("category");
        }

        if (command.BrandId is { } brandId
            && !await context.Brands.AnyAsync(brand => brand.Id == brandId, cancellationToken).ConfigureAwait(false))
        {
            return CatalogErrors.NotFound("brand");
        }

        var slug = string.IsNullOrWhiteSpace(command.Slug)
            ? CatalogFormats.ToSlug(command.Name, 320)
            : command.Slug.Trim().ToLowerInvariant();

        if (slug.Length == 0)
        {
            return Error.Validation(
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["slug"] = ["That name produces an empty slug. Supply one explicitly."],
                });
        }

        if (await context.Products.AnyAsync(p => p.Slug == slug, cancellationToken).ConfigureAwait(false))
        {
            return CatalogErrors.Duplicate("slug");
        }

        var product = Product.Draft(command.Name.Trim(), slug, command.CategoryId, scope.OwnerFor(command.VendorId));

        ProductWriter.Describe(product, command.Name, slug, command.CategoryId, command.BrandId,
            command.ShortDescription, command.Description, command.Specifications, command.Seo);

        ProductWriter.DeclareCompliance(product, command.HsnCode, command.GstRate, command.CountryOfOrigin,
            command.Manufacturer, command.Packer, command.Importer);

        product.SetAfterSalesTerms(command.IsReturnable, command.ReturnWindowDays, command.Warranty);

        context.Products.Add(product);

        var applied = await writer
            .ApplyAttributesAsync(product.Id, [], command.Attributes, cancellationToken)
            .ConfigureAwait(false);

        if (applied.IsFailure)
        {
            return applied.Error;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AuditEntityType,
                EntityId = product.Id.ToString(),
                After = new { product.Name, product.Slug, product.CategoryId, product.VendorId },
            },
            cancellationToken).ConfigureAwait(false);

        var response = await reader.ReadAsync(product.Id, cancellationToken).ConfigureAwait(false);

        return Result.Success(response!);
    }
}

/// <summary>Updates a product.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="scope">Refuses a seller editing somebody else's product.</param>
/// <param name="writer">Applies the attribute values.</param>
/// <param name="reader">States the result.</param>
/// <param name="audit">Records the change.</param>
internal sealed class UpdateProductCommandHandler(
    CatalogDbContext context,
    CatalogScope scope,
    ProductWriter writer,
    ProductReader reader,
    IAuditLogger audit) : ICommandHandler<UpdateProductCommand, ProductResponse>
{
    /// <summary>The audited action for a change to a product.</summary>
    public const string AuditAction = "catalog.product.updated";

    public async Task<Result<ProductResponse>> HandleAsync(
        UpdateProductCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var product = await context.Products
            .FirstOrDefaultAsync(candidate => candidate.Id == command.ProductId, cancellationToken)
            .ConfigureAwait(false);

        if (product is null)
        {
            return CatalogErrors.NotFound("product");
        }

        // A seller can *see* a platform-owned product — that is what makes the catalogue shared —
        // but must not edit one. The query filter cannot express that, so it is checked here.
        if (!scope.CanWrite(product.VendorId))
        {
            return CatalogErrors.OutOfScope;
        }

        if (!await context.Categories
                .AnyAsync(category => category.Id == command.CategoryId, cancellationToken)
                .ConfigureAwait(false))
        {
            return CatalogErrors.NotFound("category");
        }

        if (command.BrandId is { } brandId
            && !await context.Brands.AnyAsync(brand => brand.Id == brandId, cancellationToken).ConfigureAwait(false))
        {
            return CatalogErrors.NotFound("brand");
        }

        var before = new { product.Name, product.Slug, product.CategoryId, product.HsnCode, product.GstRate };

        var slug = string.IsNullOrWhiteSpace(command.Slug)
            ? product.Slug
            : command.Slug.Trim().ToLowerInvariant();

        if (!string.Equals(slug, product.Slug, StringComparison.Ordinal)
            && await context.Products
                .AnyAsync(p => p.Slug == slug && p.Id != product.Id, cancellationToken)
                .ConfigureAwait(false))
        {
            return CatalogErrors.Duplicate("slug");
        }

        ProductWriter.Describe(product, command.Name, slug, command.CategoryId, command.BrandId,
            command.ShortDescription, command.Description, command.Specifications, command.Seo);

        ProductWriter.DeclareCompliance(product, command.HsnCode, command.GstRate, command.CountryOfOrigin,
            command.Manufacturer, command.Packer, command.Importer);

        product.SetAfterSalesTerms(command.IsReturnable, command.ReturnWindowDays, command.Warranty);

        var existing = await context.ProductAttributeValues
            .Where(value => value.ProductId == product.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var applied = await writer
            .ApplyAttributesAsync(product.Id, existing, command.Attributes, cancellationToken)
            .ConfigureAwait(false);

        if (applied.IsFailure)
        {
            return applied.Error;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreateProductCommandHandler.AuditEntityType,
                EntityId = product.Id.ToString(),
                Before = before,
                After = new { product.Name, product.Slug, product.CategoryId, product.HsnCode, product.GstRate },
            },
            cancellationToken).ConfigureAwait(false);

        var response = await reader.ReadAsync(product.Id, cancellationToken).ConfigureAwait(false);

        return Result.Success(response!);
    }
}

/// <summary>Replaces a product's gallery.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="scope">Refuses a seller editing somebody else's product.</param>
/// <param name="reader">States the result.</param>
internal sealed class SetProductMediaCommandHandler(
    CatalogDbContext context,
    CatalogScope scope,
    ProductReader reader) : ICommandHandler<SetProductMediaCommand, ProductResponse>
{
    public async Task<Result<ProductResponse>> HandleAsync(
        SetProductMediaCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var product = await context.Products
            .FirstOrDefaultAsync(candidate => candidate.Id == command.ProductId, cancellationToken)
            .ConfigureAwait(false);

        if (product is null)
        {
            return CatalogErrors.NotFound("product");
        }

        if (!scope.CanWrite(product.VendorId))
        {
            return CatalogErrors.OutOfScope;
        }

        var existing = await context.MediaAssets
            .Where(asset => asset.ProductId == product.Id && asset.VariantId == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Whole-list replacement rather than add and remove endpoints: a gallery is edited as an
        // ordered whole in the UI — drag to reorder, delete one, add two — and reconstructing that
        // from a stream of individual operations is how positions end up duplicated.
        var kept = new List<CatalogMediaAsset>(command.Media.Count);
        var byFile = existing.ToDictionary(asset => asset.FileId);

        foreach (var payload in command.Media.DistinctBy(media => media.FileId))
        {
            if (byFile.TryGetValue(payload.FileId, out var asset))
            {
                asset.Describe(payload.AltText, payload.Position);
                kept.Add(asset);
                continue;
            }

            var created = CatalogMediaAsset.ForProduct(
                product.Id,
                payload.FileId,
                payload.Kind,
                payload.AltText,
                payload.Position);

            context.MediaAssets.Add(created);
            kept.Add(created);
        }

        context.MediaAssets.RemoveRange(existing.Where(asset => !kept.Contains(asset)));

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var response = await reader.ReadAsync(product.Id, cancellationToken).ConfigureAwait(false);

        return Result.Success(response!);
    }
}

/// <summary>Retires a product and everything under it.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="scope">Refuses a seller retiring somebody else's product.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="audit">Records the change.</param>
internal sealed class DeleteProductCommandHandler(
    CatalogDbContext context,
    CatalogScope scope,
    IClock clock,
    IAuditLogger audit) : ICommandHandler<DeleteProductCommand>
{
    /// <summary>The audited action for a retired product.</summary>
    public const string AuditAction = "catalog.product.deleted";

    public async Task<Result> HandleAsync(DeleteProductCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var product = await context.Products
            .FirstOrDefaultAsync(candidate => candidate.Id == command.ProductId, cancellationToken)
            .ConfigureAwait(false);

        if (product is null)
        {
            return Result.Failure(CatalogErrors.NotFound("product"));
        }

        if (!scope.CanWrite(product.VendorId))
        {
            return Result.Failure(CatalogErrors.OutOfScope);
        }

        var now = clock.UtcNow;

        // A soft delete that cascades, because the alternative is worse than either: a retired
        // product whose variants are still Active leaves purchasable SKUs behind a page nobody can
        // reach. Order lines hold their own frozen snapshot, so nothing historical is lost.
        var variants = await context.Variants
            .Where(variant => variant.ProductId == product.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var listings = await context.Listings
            .Where(listing => listing.ProductId == product.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var variant in variants)
        {
            variant.Delete(now);
        }

        foreach (var listing in listings)
        {
            listing.Delete(now);
        }

        product.Delete(now);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreateProductCommandHandler.AuditEntityType,
                EntityId = product.Id.ToString(),
                Before = new { product.Name, product.Slug, Variants = variants.Count, Listings = listings.Count },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
