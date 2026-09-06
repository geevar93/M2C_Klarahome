using FluentValidation;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Catalog.Application.Taxonomy;
using KlaraHome.Modules.Catalog.Application.Validation;
using KlaraHome.Modules.Catalog.Domain;
using KlaraHome.Modules.Catalog.Infrastructure;
using KlaraHome.Modules.Catalog.Infrastructure.Events;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Primitives;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Catalog.Application.Listings;

/// <summary>A vendor's offer, as the API states it.</summary>
/// <param name="Id">The offer.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="VariantId">The variant being offered.</param>
/// <param name="ProductId">Its product.</param>
/// <param name="Sku">The variant's SKU.</param>
/// <param name="ProductName">The product's title, so a list is readable without a second call.</param>
/// <param name="Status">Where the offer is in its life.</param>
/// <param name="StatusReason">Why it was last paused.</param>
/// <param name="Mrp">The declared MRP.</param>
/// <param name="SellingPrice">What the seller is asking, inclusive of GST.</param>
/// <param name="VendorSku">The seller's own code.</param>
/// <param name="HandlingTimeHours">How long they need before dispatch.</param>
/// <param name="IsCodAllowed">Whether they accept cash on delivery.</param>
/// <param name="MaxOrderQuantity">The most units one order may take.</param>
/// <param name="PublishedAt">When it last went live.</param>
/// <param name="CreatedAt">When it was opened.</param>
internal sealed record ListingResponse(
    Guid Id,
    Guid VendorId,
    Guid VariantId,
    Guid ProductId,
    string Sku,
    string ProductName,
    ListingStatus Status,
    string? StatusReason,
    decimal Mrp,
    decimal SellingPrice,
    string? VendorSku,
    int HandlingTimeHours,
    bool IsCodAllowed,
    int? MaxOrderQuantity,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt);

/// <summary>Lists offers, newest first.</summary>
/// <param name="Status">Restrict to one life-cycle state.</param>
/// <param name="VendorId">Restrict to one seller. Ignored for a vendor caller, who has only their own.</param>
/// <param name="ProductId">Restrict to one product.</param>
/// <param name="VariantId">Restrict to one variant.</param>
/// <param name="Search">A fragment of a SKU or the product's name.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListListingsQuery(
    string? Status,
    Guid? VendorId,
    Guid? ProductId,
    Guid? VariantId,
    string? Search,
    string? Cursor,
    int? Size) : IQuery<PagedResult<ListingResponse>>;

/// <summary>Reads one offer.</summary>
/// <param name="ListingId">The offer.</param>
internal sealed record GetListingQuery(Guid ListingId) : IQuery<ListingResponse>;

/// <summary>Opens an offer against a variant.</summary>
/// <param name="VariantId">The variant.</param>
/// <param name="VendorId">The seller. Ignored for a vendor caller, who offers as themselves.</param>
/// <param name="Mrp">The declared MRP, or null to take the variant's.</param>
/// <param name="SellingPrice">What the seller is asking.</param>
/// <param name="VendorSku">The seller's own code.</param>
/// <param name="HandlingTimeHours">How long they need before dispatch.</param>
/// <param name="IsCodAllowed">Whether they accept cash on delivery.</param>
/// <param name="MaxOrderQuantity">The most units one order may take.</param>
internal sealed record CreateListingCommand(
    Guid VariantId,
    Guid? VendorId,
    decimal? Mrp,
    decimal SellingPrice,
    string? VendorSku,
    int HandlingTimeHours,
    bool IsCodAllowed,
    int? MaxOrderQuantity) : ICommand<ListingResponse>;

/// <summary>Changes an offer's commercial terms.</summary>
/// <param name="ListingId">The offer.</param>
/// <param name="Mrp">The declared MRP.</param>
/// <param name="SellingPrice">What the seller is asking.</param>
/// <param name="VendorSku">The seller's own code.</param>
/// <param name="HandlingTimeHours">How long they need before dispatch.</param>
/// <param name="IsCodAllowed">Whether they accept cash on delivery.</param>
/// <param name="MaxOrderQuantity">The most units one order may take.</param>
internal sealed record UpdateListingCommand(
    Guid ListingId,
    decimal Mrp,
    decimal SellingPrice,
    string? VendorSku,
    int HandlingTimeHours,
    bool IsCodAllowed,
    int? MaxOrderQuantity) : ICommand<ListingResponse>;

/// <summary>Moves an offer through its life cycle.</summary>
/// <param name="ListingId">The offer.</param>
/// <param name="Status">Where to take it.</param>
/// <param name="Reason">Why, for a pause.</param>
internal sealed record ChangeListingStatusCommand(Guid ListingId, ListingStatus Status, string? Reason)
    : ICommand<ListingResponse>;

/// <summary>Rejects an offer that could never be stored.</summary>
internal sealed class CreateListingValidator : AbstractValidator<CreateListingCommand>
{
    public CreateListingValidator()
    {
        RuleFor(command => command.VariantId).NotEmpty();
        RuleFor(command => command.SellingPrice).GreaterThan(0m);
        RuleFor(command => command.Mrp).GreaterThan(0m).When(command => command.Mrp is not null);
        RuleFor(command => command.VendorSku).MaximumLength(64);
        RuleFor(command => command.HandlingTimeHours).InclusiveBetween(1, Listing.MaxHandlingTimeHours);
        RuleFor(command => command.MaxOrderQuantity)
            .GreaterThan(0)
            .When(command => command.MaxOrderQuantity is not null);
    }
}

/// <summary>The same rules, for a change.</summary>
internal sealed class UpdateListingValidator : AbstractValidator<UpdateListingCommand>
{
    public UpdateListingValidator()
    {
        RuleFor(command => command.ListingId).NotEmpty();
        RuleFor(command => command.SellingPrice).GreaterThan(0m);
        RuleFor(command => command.Mrp).GreaterThan(0m);
        RuleFor(command => command.VendorSku).MaximumLength(64);
        RuleFor(command => command.HandlingTimeHours).InclusiveBetween(1, Listing.MaxHandlingTimeHours);
        RuleFor(command => command.MaxOrderQuantity)
            .GreaterThan(0)
            .When(command => command.MaxOrderQuantity is not null);
    }
}

/// <summary>Lists offers.</summary>
/// <param name="context">The Catalog data context.</param>
internal sealed class ListListingsQueryHandler(CatalogDbContext context)
    : IQueryHandler<ListListingsQuery, PagedResult<ListingResponse>>
{
    public async Task<Result<PagedResult<ListingResponse>>> HandleAsync(
        ListListingsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);

        // The global vendor filter has already confined a vendor caller to their own offers.
        var rows =
            from listing in context.Listings.AsNoTracking()
            join variant in context.Variants.AsNoTracking() on listing.VariantId equals variant.Id
            join product in context.Products.AsNoTracking() on listing.ProductId equals product.Id
            select new { listing, variant.Sku, product.Name };

        if (Enum.TryParse<ListingStatus>(query.Status, ignoreCase: true, out var status))
        {
            rows = rows.Where(row => row.listing.Status == status);
        }

        if (query.VendorId is { } vendorId)
        {
            rows = rows.Where(row => row.listing.VendorId == vendorId);
        }

        if (query.ProductId is { } productId)
        {
            rows = rows.Where(row => row.listing.ProductId == productId);
        }

        if (query.VariantId is { } variantId)
        {
            rows = rows.Where(row => row.listing.VariantId == variantId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{CatalogQueries.EscapeLike(query.Search)}%";

            rows = rows.Where(row =>
                EF.Functions.ILike(row.Sku, pattern, "\\")
                || EF.Functions.ILike(row.Name, pattern, "\\")
                || (row.listing.VendorSku != null && EF.Functions.ILike(row.listing.VendorSku, pattern, "\\")));
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(row => row.listing.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(row => row.listing.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        return Result.Success(new PagedResult<ListingResponse>(
            [.. page.Select(row => ListingProjection.ToResponse(row.listing, row.Sku, row.Name))],
            new PageInfo(size, hasMore ? Cursor.Encode(page[^1].listing.Id.ToString()) : null)));
    }
}

/// <summary>Reads one offer.</summary>
/// <param name="context">The Catalog data context.</param>
internal sealed class GetListingQueryHandler(CatalogDbContext context)
    : IQueryHandler<GetListingQuery, ListingResponse>
{
    public async Task<Result<ListingResponse>> HandleAsync(
        GetListingQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var row = await (
            from listing in context.Listings.AsNoTracking()
            join variant in context.Variants.AsNoTracking() on listing.VariantId equals variant.Id
            join product in context.Products.AsNoTracking() on listing.ProductId equals product.Id
            where listing.Id == query.ListingId
            select new { listing, variant.Sku, product.Name }).FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null
            ? CatalogErrors.NotFound("listing")
            : Result.Success(ListingProjection.ToResponse(row.listing, row.Sku, row.Name));
    }
}

/// <summary>Opens an offer.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="scope">Decides which seller the offer belongs to.</param>
/// <param name="vendors">Checks that the seller may trade.</param>
/// <param name="options">Says whether an inactive seller may hold a listing at all.</param>
/// <param name="audit">Records the change.</param>
internal sealed class CreateListingCommandHandler(
    CatalogDbContext context,
    CatalogScope scope,
    IVendorDirectory vendors,
    IOptions<CatalogOptions> options,
    IAuditLogger audit) : ICommandHandler<CreateListingCommand, ListingResponse>
{
    /// <summary>The audited action for a new offer.</summary>
    public const string AuditAction = "catalog.listing.created";

    /// <summary>The entity type recorded against every listing action.</summary>
    public const string AuditEntityType = "Listing";

    public async Task<Result<ListingResponse>> HandleAsync(
        CreateListingCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (scope.OwnerFor(command.VendorId) is not { } vendorId || vendorId == Guid.Empty)
        {
            return Error.Validation(
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["vendorId"] = ["Platform staff must name the seller the offer belongs to."],
                });
        }

        // Across a module boundary and therefore over the contract, never a join
        // (docs/01-architecture.md §2.1).
        if (!options.Value.AllowListingsFromInactiveVendors
            && !await vendors.IsActiveAsync(vendorId, cancellationToken).ConfigureAwait(false))
        {
            return CatalogErrors.VendorInactive;
        }

        var variant = await context.Variants
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == command.VariantId, cancellationToken)
            .ConfigureAwait(false);

        if (variant is null)
        {
            return CatalogErrors.NotFound("variant");
        }

        if (await context.Listings
                .AnyAsync(
                    listing => listing.VendorId == vendorId && listing.VariantId == variant.Id,
                    cancellationToken)
                .ConfigureAwait(false))
        {
            return CatalogErrors.Duplicate("offer for that seller and variant");
        }

        // The variant's MRP is the default and the ceiling. A seller may declare a lower one — pack
        // MRPs do differ between production runs — but not a higher one, or the storefront would
        // show a "discount" against a number nobody printed.
        var mrp = Money.Rupees(command.Mrp ?? variant.Mrp.Amount);
        var price = Money.Rupees(command.SellingPrice);

        if (mrp.Amount <= 0m)
        {
            return CatalogErrors.NotPublishable("That variant has no MRP yet, so an offer has no ceiling.");
        }

        if (price > mrp)
        {
            return CatalogErrors.PriceAboveMrp;
        }

        var listing = Listing.Open(vendorId, variant.Id, variant.ProductId);

        listing.SetTerms(
            mrp,
            price,
            command.VendorSku,
            command.HandlingTimeHours,
            command.IsCodAllowed,
            command.MaxOrderQuantity);

        context.Listings.Add(listing);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AuditEntityType,
                EntityId = listing.Id.ToString(),
                After = new { listing.VendorId, listing.VariantId, SellingPrice = price.Amount, Mrp = mrp.Amount },
            },
            cancellationToken).ConfigureAwait(false);

        var name = await context.Products
            .AsNoTracking()
            .Where(product => product.Id == listing.ProductId)
            .Select(product => product.Name)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(ListingProjection.ToResponse(listing, variant.Sku, name));
    }
}

/// <summary>Changes an offer's commercial terms.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="scope">Refuses a seller editing somebody else's offer.</param>
/// <param name="publisher">Announces the change to a live offer.</param>
/// <param name="audit">Records the change.</param>
internal sealed class UpdateListingCommandHandler(
    CatalogDbContext context,
    CatalogScope scope,
    CatalogEventPublisher publisher,
    IAuditLogger audit) : ICommandHandler<UpdateListingCommand, ListingResponse>
{
    /// <summary>The audited action for a change to an offer.</summary>
    public const string AuditAction = "catalog.listing.updated";

    public async Task<Result<ListingResponse>> HandleAsync(
        UpdateListingCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var listing = await context.Listings
            .FirstOrDefaultAsync(candidate => candidate.Id == command.ListingId, cancellationToken)
            .ConfigureAwait(false);

        if (listing is null)
        {
            return CatalogErrors.NotFound("listing");
        }

        if (!scope.CanWrite(listing.VendorId))
        {
            return CatalogErrors.OutOfScope;
        }

        var mrp = Money.Rupees(command.Mrp);
        var price = Money.Rupees(command.SellingPrice);

        if (price > mrp)
        {
            return CatalogErrors.PriceAboveMrp;
        }

        var before = new { Mrp = listing.Mrp.Amount, SellingPrice = listing.SellingPrice.Amount };

        listing.SetTerms(
            mrp,
            price,
            command.VendorSku,
            command.HandlingTimeHours,
            command.IsCodAllowed,
            command.MaxOrderQuantity);

        publisher.Updated(listing);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreateListingCommandHandler.AuditEntityType,
                EntityId = listing.Id.ToString(),
                Before = before,
                After = new { Mrp = mrp.Amount, SellingPrice = price.Amount },
            },
            cancellationToken).ConfigureAwait(false);

        return await ListingProjection.StateAsync(context, listing, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Moves an offer through its life cycle.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="scope">Refuses a seller acting on somebody else's offer.</param>
/// <param name="vendors">Checks that the seller may trade before an offer goes live.</param>
/// <param name="publisher">Announces the change downstream.</param>
/// <param name="options">Says whether an inactive seller may hold a live listing.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class ChangeListingStatusCommandHandler(
    CatalogDbContext context,
    CatalogScope scope,
    IVendorDirectory vendors,
    CatalogEventPublisher publisher,
    IOptions<CatalogOptions> options,
    IClock clock) : ICommandHandler<ChangeListingStatusCommand, ListingResponse>
{
    public async Task<Result<ListingResponse>> HandleAsync(
        ChangeListingStatusCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var listing = await context.Listings
            .FirstOrDefaultAsync(candidate => candidate.Id == command.ListingId, cancellationToken)
            .ConfigureAwait(false);

        if (listing is null)
        {
            return CatalogErrors.NotFound("listing");
        }

        if (!scope.CanWrite(listing.VendorId))
        {
            return CatalogErrors.OutOfScope;
        }

        var variant = await context.Variants
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == listing.VariantId, cancellationToken)
            .ConfigureAwait(false);

        var product = await context.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == listing.ProductId, cancellationToken)
            .ConfigureAwait(false);

        if (variant is null || product is null)
        {
            return CatalogErrors.NotFound("variant");
        }

        if (command.Status == ListingStatus.Active)
        {
            // Three checks, and each one is an invariant something else owns: the seller may trade
            // (Vendors), the variant is sellable, and the product is published. An offer that
            // passes none of them is a purchasable line item nobody can fulfil.
            if (!options.Value.AllowListingsFromInactiveVendors
                && !await vendors.IsActiveAsync(listing.VendorId!.Value, cancellationToken).ConfigureAwait(false))
            {
                return CatalogErrors.VendorInactive;
            }

            if (!variant.IsSellable)
            {
                return CatalogErrors.NotPublishable("That variant is not active, so it cannot be offered.");
            }

            if (!product.IsPublished)
            {
                return CatalogErrors.NotPublishable("That product is not published, so it cannot be offered.");
            }

            if (listing.SellingPrice > listing.Mrp)
            {
                return CatalogErrors.PriceAboveMrp;
            }
        }

        var from = listing.Status;

        if (!listing.TransitionTo(command.Status, clock.UtcNow, command.Reason))
        {
            return CatalogErrors.InvalidTransition(from, command.Status);
        }

        if (command.Status == ListingStatus.Active)
        {
            publisher.Published(listing, variant.Sku);
        }
        else
        {
            publisher.Deactivated(listing, command.Reason);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ListingProjection.ToResponse(listing, variant.Sku, product.Name));
    }
}

/// <summary>Turns offers into responses.</summary>
internal static class ListingProjection
{
    /// <summary>States an offer.</summary>
    /// <param name="listing">The offer.</param>
    /// <param name="sku">Its variant's SKU.</param>
    /// <param name="productName">Its product's title.</param>
    public static ListingResponse ToResponse(Listing listing, string sku, string productName)
    {
        ArgumentNullException.ThrowIfNull(listing);

        return new ListingResponse(
            listing.Id,
            listing.VendorId!.Value,
            listing.VariantId,
            listing.ProductId,
            sku,
            productName,
            listing.Status,
            listing.StatusReason,
            listing.Mrp.Amount,
            listing.SellingPrice.Amount,
            listing.VendorSku,
            listing.HandlingTimeHours,
            listing.IsCodAllowed,
            listing.MaxOrderQuantity,
            listing.PublishedAt,
            listing.CreatedAt);
    }

    /// <summary>States an offer, fetching the SKU and product title it does not hold.</summary>
    /// <param name="context">The Catalog data context.</param>
    /// <param name="listing">The offer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<Result<ListingResponse>> StateAsync(
        CatalogDbContext context,
        Listing listing,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(listing);

        var sku = await context.Variants
            .AsNoTracking()
            .Where(variant => variant.Id == listing.VariantId)
            .Select(variant => variant.Sku)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var name = await context.Products
            .AsNoTracking()
            .Where(product => product.Id == listing.ProductId)
            .Select(product => product.Name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(ToResponse(listing, sku ?? string.Empty, name ?? string.Empty));
    }
}
