using FluentValidation;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Catalog.Application.Validation;
using KlaraHome.Modules.Catalog.Domain;
using KlaraHome.Modules.Catalog.Infrastructure;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Primitives;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Catalog.Application.Products;

/// <summary>One axis of a variant's defining combination, as a caller supplies it.</summary>
/// <param name="AttributeId">The axis.</param>
/// <param name="OptionId">The value on it.</param>
internal sealed record VariantOptionPayload(Guid AttributeId, Guid OptionId);

/// <summary>Creates a variant of a product.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Sku">A SKU to use, or null to take the next one from the sequence.</param>
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
internal sealed record CreateVariantCommand(
    Guid ProductId,
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
    IReadOnlyList<MediaPayload>? Media) : ICommand<VariantResponse>;

/// <summary>Updates a variant.</summary>
/// <param name="VariantId">The variant.</param>
/// <param name="Sku">Its SKU.</param>
/// <param name="Barcode">The barcode on the pack.</param>
/// <param name="NameSuffix">What distinguishes it.</param>
/// <param name="Mrp">Maximum retail price.</param>
/// <param name="NetQuantity">The declared net quantity.</param>
/// <param name="ShelfLifeDays">Shelf life in days.</param>
/// <param name="ExpiresOn">A fixed expiry date.</param>
/// <param name="WeightGrams">Dead weight.</param>
/// <param name="LengthMm">Packed length.</param>
/// <param name="WidthMm">Packed width.</param>
/// <param name="HeightMm">Packed height.</param>
/// <param name="Position">Sort order within the product.</param>
/// <param name="IsDefault">Whether the PDP should open on it.</param>
/// <param name="Options">Its defining combination, or null to leave it alone.</param>
/// <param name="Media">Its gallery, or null to leave it alone.</param>
internal sealed record UpdateVariantCommand(
    Guid VariantId,
    string Sku,
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
    IReadOnlyList<MediaPayload>? Media) : ICommand<VariantResponse>;

/// <summary>Moves a variant through its life cycle.</summary>
/// <param name="VariantId">The variant.</param>
/// <param name="Status">Where to take it.</param>
internal sealed record ChangeVariantStatusCommand(Guid VariantId, VariantStatus Status) : ICommand<VariantResponse>;

/// <summary>Retires a variant.</summary>
/// <param name="VariantId">The variant.</param>
internal sealed record DeleteVariantCommand(Guid VariantId) : ICommand;

/// <summary>Rejects a variant that could never be stored.</summary>
internal sealed class CreateVariantValidator : AbstractValidator<CreateVariantCommand>
{
    public CreateVariantValidator()
    {
        RuleFor(command => command.ProductId).NotEmpty();
        RuleFor(command => command.Mrp).GreaterThanOrEqualTo(0m);
        RuleFor(command => command.NetQuantity).MaximumLength(80);
        RuleFor(command => command.WeightGrams).InclusiveBetween(0, 1_000_000);
        RuleFor(command => command.Sku)
            .Must(sku => CatalogFormats.Sku().IsMatch(sku!.Trim().ToUpperInvariant()))
            .When(command => !string.IsNullOrWhiteSpace(command.Sku))
            .WithMessage("A SKU is upper-case letters, digits, hyphens and underscores.");
    }
}

/// <summary>The same rules, for a change.</summary>
internal sealed class UpdateVariantValidator : AbstractValidator<UpdateVariantCommand>
{
    public UpdateVariantValidator()
    {
        RuleFor(command => command.VariantId).NotEmpty();
        RuleFor(command => command.Mrp).GreaterThanOrEqualTo(0m);
        RuleFor(command => command.NetQuantity).MaximumLength(80);
        RuleFor(command => command.WeightGrams).InclusiveBetween(0, 1_000_000);
        RuleFor(command => command.Sku)
            .NotEmpty()
            .Must(sku => CatalogFormats.Sku().IsMatch(sku.Trim().ToUpperInvariant()))
            .WithMessage("A SKU is upper-case letters, digits, hyphens and underscores.");
    }
}

/// <summary>Creates a variant.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="scope">Refuses a seller editing somebody else's product, and mints the SKU.</param>
/// <param name="writer">Applies the defining combination and the gallery.</param>
/// <param name="reader">States the result.</param>
/// <param name="audit">Records the change.</param>
internal sealed class CreateVariantCommandHandler(
    CatalogDbContext context,
    CatalogScope scope,
    VariantWriter writer,
    ProductReader reader,
    IAuditLogger audit) : ICommandHandler<CreateVariantCommand, VariantResponse>
{
    /// <summary>The audited action for a new variant.</summary>
    public const string AuditAction = "catalog.variant.created";

    /// <summary>The entity type recorded against every variant action.</summary>
    public const string AuditEntityType = "Variant";

    public async Task<Result<VariantResponse>> HandleAsync(
        CreateVariantCommand command,
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

        var sku = string.IsNullOrWhiteSpace(command.Sku)
            ? await scope.NextSkuAsync(cancellationToken).ConfigureAwait(false)
            : command.Sku.Trim().ToUpperInvariant();

        if (await context.Variants.AnyAsync(v => v.Sku == sku, cancellationToken).ConfigureAwait(false))
        {
            return CatalogErrors.Duplicate("SKU");
        }

        var variant = Variant.Create(product.Id, sku);
        variant.Describe(sku, command.Barcode, command.NameSuffix, command.Position);
        variant.SetDimensions(command.WeightGrams, command.LengthMm, command.WidthMm, command.HeightMm);
        variant.DeclarePack(
            Money.Rupees(command.Mrp),
            command.NetQuantity,
            command.ShelfLifeDays,
            command.ExpiresOn);

        context.Variants.Add(variant);

        var combination = await writer
            .ApplyCombinationAsync(variant, [], command.Options ?? [], cancellationToken)
            .ConfigureAwait(false);

        if (combination.IsFailure)
        {
            return combination.Error;
        }

        await writer.ApplyMediaAsync(variant, product.Id, [], command.Media, cancellationToken)
            .ConfigureAwait(false);

        // The first variant of a product is its default whatever the caller said, because a product
        // whose PDP has no variant to open on renders nothing at all.
        var isFirst = !await context.Variants
            .AnyAsync(v => v.ProductId == product.Id && v.Id != variant.Id, cancellationToken)
            .ConfigureAwait(false);

        if (command.IsDefault || isFirst)
        {
            await writer.MakeDefaultAsync(variant, cancellationToken).ConfigureAwait(false);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AuditEntityType,
                EntityId = variant.Id.ToString(),
                After = new { variant.Sku, variant.ProductId, Mrp = variant.Mrp.Amount },
            },
            cancellationToken).ConfigureAwait(false);

        return await StateAsync(reader, product.Id, variant.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>States one variant by re-reading its product, which is where the assembly lives.</summary>
    /// <param name="reader">Assembles the full picture.</param>
    /// <param name="productId">The product.</param>
    /// <param name="variantId">The variant to pick out of it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task<Result<VariantResponse>> StateAsync(
        ProductReader reader,
        Guid productId,
        Guid variantId,
        CancellationToken cancellationToken)
    {
        var product = await reader.ReadAsync(productId, cancellationToken).ConfigureAwait(false);
        var variant = product?.Variants.FirstOrDefault(candidate => candidate.Id == variantId);

        return variant is null ? CatalogErrors.NotFound("variant") : Result.Success(variant);
    }
}

/// <summary>Updates a variant.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="scope">Refuses a seller editing somebody else's product.</param>
/// <param name="writer">Applies the defining combination and the gallery.</param>
/// <param name="reader">States the result.</param>
/// <param name="audit">Records the change.</param>
internal sealed class UpdateVariantCommandHandler(
    CatalogDbContext context,
    CatalogScope scope,
    VariantWriter writer,
    ProductReader reader,
    IAuditLogger audit) : ICommandHandler<UpdateVariantCommand, VariantResponse>
{
    /// <summary>The audited action for a change to a variant.</summary>
    public const string AuditAction = "catalog.variant.updated";

    public async Task<Result<VariantResponse>> HandleAsync(
        UpdateVariantCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var variant = await context.Variants
            .FirstOrDefaultAsync(candidate => candidate.Id == command.VariantId, cancellationToken)
            .ConfigureAwait(false);

        if (variant is null)
        {
            return CatalogErrors.NotFound("variant");
        }

        var product = await context.Products
            .FirstOrDefaultAsync(candidate => candidate.Id == variant.ProductId, cancellationToken)
            .ConfigureAwait(false);

        if (product is null || !scope.CanWrite(product.VendorId))
        {
            return CatalogErrors.OutOfScope;
        }

        var sku = command.Sku.Trim().ToUpperInvariant();

        if (!string.Equals(sku, variant.Sku, StringComparison.Ordinal)
            && await context.Variants
                .AnyAsync(v => v.Sku == sku && v.Id != variant.Id, cancellationToken)
                .ConfigureAwait(false))
        {
            return CatalogErrors.Duplicate("SKU");
        }

        var before = new { variant.Sku, Mrp = variant.Mrp.Amount, variant.NetQuantity };

        variant.Describe(sku, command.Barcode, command.NameSuffix, command.Position);
        variant.SetDimensions(command.WeightGrams, command.LengthMm, command.WidthMm, command.HeightMm);
        variant.DeclarePack(
            Money.Rupees(command.Mrp),
            command.NetQuantity,
            command.ShelfLifeDays,
            command.ExpiresOn);

        if (command.Options is not null)
        {
            var existing = await context.VariantAttributeValues
                .Where(value => value.VariantId == variant.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var combination = await writer
                .ApplyCombinationAsync(variant, existing, command.Options, cancellationToken)
                .ConfigureAwait(false);

            if (combination.IsFailure)
            {
                return combination.Error;
            }
        }

        if (command.Media is not null)
        {
            var existing = await context.MediaAssets
                .Where(asset => asset.VariantId == variant.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            await writer.ApplyMediaAsync(variant, product.Id, existing, command.Media, cancellationToken)
                .ConfigureAwait(false);
        }

        if (command.IsDefault && !variant.IsDefault)
        {
            await writer.MakeDefaultAsync(variant, cancellationToken).ConfigureAwait(false);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreateVariantCommandHandler.AuditEntityType,
                EntityId = variant.Id.ToString(),
                Before = before,
                After = new { variant.Sku, Mrp = variant.Mrp.Amount, variant.NetQuantity },
            },
            cancellationToken).ConfigureAwait(false);

        return await CreateVariantCommandHandler
            .StateAsync(reader, product.Id, variant.Id, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Moves a variant through its life cycle.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="scope">Refuses a seller editing somebody else's product.</param>
/// <param name="publisher">Announces the offers a deactivation withdraws.</param>
/// <param name="reader">States the result.</param>
/// <param name="options">Says whether compliance is enforced before publication.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class ChangeVariantStatusCommandHandler(
    CatalogDbContext context,
    CatalogScope scope,
    Infrastructure.Events.CatalogEventPublisher publisher,
    ProductReader reader,
    Microsoft.Extensions.Options.IOptions<CatalogOptions> options,
    IClock clock) : ICommandHandler<ChangeVariantStatusCommand, VariantResponse>
{
    public async Task<Result<VariantResponse>> HandleAsync(
        ChangeVariantStatusCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var variant = await context.Variants
            .FirstOrDefaultAsync(candidate => candidate.Id == command.VariantId, cancellationToken)
            .ConfigureAwait(false);

        if (variant is null)
        {
            return CatalogErrors.NotFound("variant");
        }

        var product = await context.Products
            .FirstOrDefaultAsync(candidate => candidate.Id == variant.ProductId, cancellationToken)
            .ConfigureAwait(false);

        if (product is null || !scope.CanWrite(product.VendorId))
        {
            return CatalogErrors.OutOfScope;
        }

        if (command.Status == VariantStatus.Active && options.Value.EnforceComplianceOnPublish)
        {
            var gaps = variant.ComplianceGaps();

            if (gaps.Count > 0)
            {
                return CatalogErrors.ComplianceIncomplete(gaps);
            }
        }

        var from = variant.Status;

        if (!variant.TransitionTo(command.Status))
        {
            return CatalogErrors.InvalidTransition(from, command.Status);
        }

        // A variant that stops being sellable takes its offers with it. Leaving them Active would
        // leave purchasable listings against something the merchandiser has just withdrawn.
        if (command.Status is VariantStatus.Inactive or VariantStatus.Archived)
        {
            var listings = await context.Listings
                .Where(listing => listing.VariantId == variant.Id && listing.Status == ListingStatus.Active)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var listing in listings)
            {
                if (listing.TransitionTo(ListingStatus.Inactive, clock.UtcNow, "The variant was withdrawn."))
                {
                    publisher.Deactivated(listing, "The variant was withdrawn.");
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await CreateVariantCommandHandler
            .StateAsync(reader, product.Id, variant.Id, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Retires a variant.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="scope">Refuses a seller editing somebody else's product.</param>
/// <param name="publisher">Announces the offers the retirement withdraws.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="audit">Records the change.</param>
internal sealed class DeleteVariantCommandHandler(
    CatalogDbContext context,
    CatalogScope scope,
    Infrastructure.Events.CatalogEventPublisher publisher,
    IClock clock,
    IAuditLogger audit) : ICommandHandler<DeleteVariantCommand>
{
    /// <summary>The audited action for a retired variant.</summary>
    public const string AuditAction = "catalog.variant.deleted";

    public async Task<Result> HandleAsync(DeleteVariantCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var variant = await context.Variants
            .FirstOrDefaultAsync(candidate => candidate.Id == command.VariantId, cancellationToken)
            .ConfigureAwait(false);

        if (variant is null)
        {
            return Result.Failure(CatalogErrors.NotFound("variant"));
        }

        var product = await context.Products
            .FirstOrDefaultAsync(candidate => candidate.Id == variant.ProductId, cancellationToken)
            .ConfigureAwait(false);

        if (product is null || !scope.CanWrite(product.VendorId))
        {
            return Result.Failure(CatalogErrors.OutOfScope);
        }

        // "A Product cannot be Active with zero Active Variants" (docs/02-domain-model.md §4.1).
        // Refused rather than silently unpublishing the product, because the operator deleting one
        // variant of four has not asked for the whole product to disappear.
        var siblings = await context.Variants
            .CountAsync(
                candidate => candidate.ProductId == product.Id
                    && candidate.Id != variant.Id
                    && candidate.Status == VariantStatus.Active,
                cancellationToken)
            .ConfigureAwait(false);

        if (product.Status == ProductStatus.Active && siblings == 0)
        {
            return Result.Failure(CatalogErrors.NotPublishable(
                "That is the last active variant of a published product. Unpublish the product first."));
        }

        var now = clock.UtcNow;

        var listings = await context.Listings
            .Where(listing => listing.VariantId == variant.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var listing in listings.Where(listing => listing.Status == ListingStatus.Active))
        {
            publisher.Deactivated(listing, "The variant was retired.");
        }

        foreach (var listing in listings)
        {
            listing.Delete(now);
        }

        variant.Delete(now);

        // A retired variant that was the default leaves the PDP with nothing to open on, so the
        // next active sibling takes over. Nothing is promoted when there is no sibling: the product
        // is on its way out too.
        if (variant.IsDefault)
        {
            var successor = await context.Variants
                .Where(candidate => candidate.ProductId == product.Id && candidate.Id != variant.Id)
                .OrderBy(candidate => candidate.Position)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            variant.SetDefault(false);
            successor?.SetDefault(true);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreateVariantCommandHandler.AuditEntityType,
                EntityId = variant.Id.ToString(),
                Before = new { variant.Sku, variant.ProductId, Listings = listings.Count },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
