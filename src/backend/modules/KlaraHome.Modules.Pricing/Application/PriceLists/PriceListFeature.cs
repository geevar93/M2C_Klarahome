using FluentValidation;
using KlaraHome.Contracts.Catalog;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Pricing;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Pricing.Domain;
using KlaraHome.Modules.Pricing.Infrastructure;
using KlaraHome.Modules.Pricing.Infrastructure.Events;
using KlaraHome.Modules.Pricing.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Primitives;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Pricing.Application.PriceLists;

/// <summary>A price list, as the API states it.</summary>
/// <param name="Id">The list.</param>
/// <param name="VendorId">The seller it prices for, or null for a platform-wide list.</param>
/// <param name="Code">The short code an operator quotes.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Type">Why it exists.</param>
/// <param name="CurrencyCode">ISO 4217 code its items are priced in.</param>
/// <param name="Priority">Resolution order. Lower wins.</param>
/// <param name="StartsAt">When it starts applying.</param>
/// <param name="EndsAt">When it stops.</param>
/// <param name="IsActive">Whether it is considered at all.</param>
/// <param name="ItemCount">How many prices are in it.</param>
/// <param name="CreatedAt">When it was opened.</param>
internal sealed record PriceListResponse(
    Guid Id,
    Guid? VendorId,
    string Code,
    string Name,
    string Type,
    string CurrencyCode,
    int Priority,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    bool IsActive,
    int ItemCount,
    DateTimeOffset CreatedAt);

/// <summary>One price in a list, as the API states it.</summary>
/// <param name="Id">The item.</param>
/// <param name="PriceListId">The list it belongs to.</param>
/// <param name="ListingId">The offer it prices.</param>
/// <param name="Price">What one unit costs at this tier, inclusive of GST.</param>
/// <param name="MinQuantity">The smallest quantity this tier applies from.</param>
internal sealed record PriceListItemResponse(
    Guid Id,
    Guid PriceListId,
    Guid ListingId,
    decimal Price,
    int MinQuantity);

/// <summary>One price to set, as the caller sends it.</summary>
/// <param name="ListingId">The offer.</param>
/// <param name="Price">What one unit costs at this tier.</param>
/// <param name="MinQuantity">The smallest quantity it applies from. One for the ordinary case.</param>
internal sealed record PriceListItemPayload(Guid ListingId, decimal Price, int MinQuantity);

/// <summary>Lists price lists.</summary>
/// <param name="VendorId">Restrict to one seller's lists.</param>
/// <param name="Type">Restrict to one kind.</param>
/// <param name="ActiveOnly">Hide the ones that are switched off.</param>
/// <param name="Search">A fragment of the code or the name.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListPriceListsQuery(
    Guid? VendorId,
    string? Type,
    bool? ActiveOnly,
    string? Search,
    string? Cursor,
    int? Size) : IQuery<PagedResult<PriceListResponse>>;

/// <summary>Reads one price list.</summary>
/// <param name="PriceListId">The list.</param>
internal sealed record GetPriceListQuery(Guid PriceListId) : IQuery<PriceListResponse>;

/// <summary>Lists the prices in one list.</summary>
/// <param name="PriceListId">The list.</param>
/// <param name="ListingId">Restrict to one offer, to see all of its tiers.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListPriceListItemsQuery(Guid PriceListId, Guid? ListingId, string? Cursor, int? Size)
    : IQuery<PagedResult<PriceListItemResponse>>;

/// <summary>Opens a price list.</summary>
/// <param name="VendorId">The seller. Ignored for a vendor caller, who opens their own.</param>
/// <param name="Code">The short code an operator quotes.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Type">Why it exists.</param>
/// <param name="Priority">Resolution order. Lower wins.</param>
/// <param name="StartsAt">When it starts applying.</param>
/// <param name="EndsAt">When it stops.</param>
internal sealed record CreatePriceListCommand(
    Guid? VendorId,
    string Code,
    string Name,
    string Type,
    int Priority,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt) : ICommand<PriceListResponse>;

/// <summary>Restates a price list.</summary>
/// <param name="PriceListId">The list.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Type">Why it exists.</param>
/// <param name="Priority">Resolution order.</param>
/// <param name="StartsAt">When it starts applying.</param>
/// <param name="EndsAt">When it stops.</param>
internal sealed record UpdatePriceListCommand(
    Guid PriceListId,
    string Name,
    string Type,
    int Priority,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt) : ICommand<PriceListResponse>;

/// <summary>Turns a price list on or off.</summary>
/// <param name="PriceListId">The list.</param>
/// <param name="IsActive">Whether it is considered.</param>
internal sealed record SetPriceListActiveCommand(Guid PriceListId, bool IsActive) : ICommand<PriceListResponse>;

/// <summary>Removes a price list and every price in it.</summary>
/// <param name="PriceListId">The list.</param>
internal sealed record DeletePriceListCommand(Guid PriceListId) : ICommand;

/// <summary>Adds or restates a batch of prices.</summary>
/// <param name="PriceListId">The list.</param>
/// <param name="Items">The prices, one row per offer per quantity tier.</param>
internal sealed record UpsertPriceListItemsCommand(Guid PriceListId, IReadOnlyList<PriceListItemPayload> Items)
    : ICommand<IReadOnlyList<PriceListItemResponse>>;

/// <summary>Removes one price.</summary>
/// <param name="PriceListId">The list.</param>
/// <param name="ItemId">The item.</param>
internal sealed record DeletePriceListItemCommand(Guid PriceListId, Guid ItemId) : ICommand;

/// <summary>Explains what an offer costs and which list decided it.</summary>
/// <param name="ListingId">The offer.</param>
/// <param name="Quantity">How many units, for the quantity tier.</param>
internal sealed record ResolvePriceQuery(Guid ListingId, int? Quantity) : IQuery<EffectivePrice>;

/// <summary>Rejects a price list that could never be stored.</summary>
internal sealed class CreatePriceListValidator : AbstractValidator<CreatePriceListCommand>
{
    public CreatePriceListValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(48).Matches("^[A-Za-z0-9][A-Za-z0-9-]*$");
        RuleFor(command => command.Name).NotEmpty().MaximumLength(160);
        RuleFor(command => command.Type).NotEmpty().Must(PriceListTypes.IsKnown).WithMessage(PriceListTypes.Message);
        RuleFor(command => command.Priority).InclusiveBetween(0, PriceList.MaxPriority);

        RuleFor(command => command.EndsAt)
            .GreaterThan(command => command.StartsAt!.Value)
            .When(command => command.StartsAt is not null && command.EndsAt is not null)
            .WithMessage("A price list cannot close before it opens.");
    }
}

/// <summary>The same rules, for a change.</summary>
internal sealed class UpdatePriceListValidator : AbstractValidator<UpdatePriceListCommand>
{
    public UpdatePriceListValidator()
    {
        RuleFor(command => command.PriceListId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(160);
        RuleFor(command => command.Type).NotEmpty().Must(PriceListTypes.IsKnown).WithMessage(PriceListTypes.Message);
        RuleFor(command => command.Priority).InclusiveBetween(0, PriceList.MaxPriority);

        RuleFor(command => command.EndsAt)
            .GreaterThan(command => command.StartsAt!.Value)
            .When(command => command.StartsAt is not null && command.EndsAt is not null)
            .WithMessage("A price list cannot close before it opens.");
    }
}

/// <summary>Rejects a batch of prices that could never be stored.</summary>
internal sealed class UpsertPriceListItemsValidator : AbstractValidator<UpsertPriceListItemsCommand>
{
    public UpsertPriceListItemsValidator()
    {
        RuleFor(command => command.PriceListId).NotEmpty();
        RuleFor(command => command.Items).NotEmpty();

        RuleForEach(command => command.Items).ChildRules(item =>
        {
            item.RuleFor(payload => payload.ListingId).NotEmpty();
            item.RuleFor(payload => payload.Price).GreaterThanOrEqualTo(0m);
            item.RuleFor(payload => payload.MinQuantity).GreaterThanOrEqualTo(1);
        });
    }
}

/// <summary>The price-list types the API accepts, and the message when a caller sends another.</summary>
internal static class PriceListTypes
{
    /// <summary>Whether a name is one this platform understands.</summary>
    /// <param name="value">What the caller sent.</param>
    public static bool IsKnown(string? value) => Enum.TryParse<PriceListType>(value, ignoreCase: true, out _);

    /// <summary>Parses a name, defaulting to a base list.</summary>
    /// <param name="value">What the caller sent.</param>
    public static PriceListType Parse(string? value)
        => Enum.TryParse<PriceListType>(value, ignoreCase: true, out var parsed) ? parsed : PriceListType.Base;

    /// <summary>The validation message, listing what is accepted.</summary>
    public static string Message { get; } =
        "Type must be one of: " + string.Join(", ", Enum.GetNames<PriceListType>());
}

/// <summary>Lists price lists.</summary>
/// <param name="context">The Pricing data context.</param>
internal sealed class ListPriceListsQueryHandler(PricingDbContext context)
    : IQueryHandler<ListPriceListsQuery, PagedResult<PriceListResponse>>
{
    public async Task<Result<PagedResult<PriceListResponse>>> HandleAsync(
        ListPriceListsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);
        var rows = context.PriceLists.AsNoTracking();

        if (query.VendorId is { } vendorId)
        {
            rows = rows.Where(list => list.VendorId == vendorId);
        }

        if (PriceListTypes.IsKnown(query.Type))
        {
            var type = PriceListTypes.Parse(query.Type);
            rows = rows.Where(list => list.Type == type);
        }

        if (query.ActiveOnly == true)
        {
            rows = rows.Where(list => list.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{PricingQueries.EscapeLike(query.Search)}%";

            rows = rows.Where(list =>
                EF.Functions.ILike(list.Code, pattern, "\\")
                || EF.Functions.ILike(list.Name, pattern, "\\"));
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(list => list.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(list => list.Id)
            .Take(size + 1)
            .Select(list => new PriceListResponse(
                list.Id,
                list.VendorId,
                list.Code,
                list.Name,
                list.Type.ToString(),
                list.CurrencyCode,
                list.Priority,
                list.StartsAt,
                list.EndsAt,
                list.IsActive,
                list.Items.Count,
                list.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        return Result.Success(new PagedResult<PriceListResponse>(
            page,
            new PageInfo(size, hasMore ? Cursor.Encode(page[^1].Id.ToString()) : null)));
    }
}

/// <summary>Reads one price list.</summary>
/// <param name="context">The Pricing data context.</param>
internal sealed class GetPriceListQueryHandler(PricingDbContext context)
    : IQueryHandler<GetPriceListQuery, PriceListResponse>
{
    public async Task<Result<PriceListResponse>> HandleAsync(
        GetPriceListQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var list = await context.PriceLists
            .AsNoTracking()
            .Include(candidate => candidate.Items)
            .FirstOrDefaultAsync(candidate => candidate.Id == query.PriceListId, cancellationToken)
            .ConfigureAwait(false);

        return list is null
            ? PricingErrors.NotFound("price list")
            : Result.Success(PriceListProjection.ToResponse(list));
    }
}

/// <summary>Lists the prices in one list.</summary>
/// <param name="context">The Pricing data context.</param>
internal sealed class ListPriceListItemsQueryHandler(PricingDbContext context)
    : IQueryHandler<ListPriceListItemsQuery, PagedResult<PriceListItemResponse>>
{
    public async Task<Result<PagedResult<PriceListItemResponse>>> HandleAsync(
        ListPriceListItemsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Through the list, so the vendor query filter on the list decides visibility. Querying the
        // items directly would read a table that carries no seller of its own.
        var visible = await context.PriceLists
            .AsNoTracking()
            .AnyAsync(list => list.Id == query.PriceListId, cancellationToken)
            .ConfigureAwait(false);

        if (!visible)
        {
            return PricingErrors.NotFound("price list");
        }

        var size = Cursor.NormalizeSize(query.Size);
        var rows = context.PriceListItems.AsNoTracking().Where(item => item.PriceListId == query.PriceListId);

        if (query.ListingId is { } listingId)
        {
            rows = rows.Where(item => item.ListingId == listingId);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(item => item.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(item => item.Id)
            .Take(size + 1)
            .Select(item => new PriceListItemResponse(
                item.Id,
                item.PriceListId,
                item.ListingId,
                item.Price.Amount,
                item.MinQuantity))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        return Result.Success(new PagedResult<PriceListItemResponse>(
            page,
            new PageInfo(size, hasMore ? Cursor.Encode(page[^1].Id.ToString()) : null)));
    }
}

/// <summary>Opens a price list.</summary>
/// <param name="context">The Pricing data context.</param>
/// <param name="scope">Decides whose list it is.</param>
/// <param name="settings">Supplies the store's currency.</param>
/// <param name="audit">Records the addition.</param>
internal sealed class CreatePriceListCommandHandler(
    PricingDbContext context,
    PricingScope scope,
    IStoreSettings settings,
    IAuditLogger audit) : ICommandHandler<CreatePriceListCommand, PriceListResponse>
{
    /// <summary>The audited action for a new price list.</summary>
    public const string AuditAction = "pricing.price-list.created";

    /// <summary>The entity type recorded against every price-list action.</summary>
    public const string AuditEntityType = "PriceList";

    public async Task<Result<PriceListResponse>> HandleAsync(
        CreatePriceListCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var code = command.Code.Trim().ToUpperInvariant();

        if (await context.PriceLists
                .AnyAsync(candidate => candidate.Code == code, cancellationToken)
                .ConfigureAwait(false))
        {
            return PricingErrors.Duplicate("price list code");
        }

        var localization = await settings.GetAsync<LocalizationSettings>(cancellationToken).ConfigureAwait(false);

        var list = PriceList.Create(
            scope.OwnerFor(command.VendorId),
            code,
            command.Name,
            PriceListTypes.Parse(command.Type),
            string.IsNullOrWhiteSpace(localization.CurrencyCode) ? Money.Inr : localization.CurrencyCode);

        list.Update(
            command.Name,
            PriceListTypes.Parse(command.Type),
            command.Priority,
            command.StartsAt,
            command.EndsAt);

        context.PriceLists.Add(list);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AuditEntityType,
                EntityId = list.Id.ToString(),
                After = new { list.Code, list.Name, Type = list.Type.ToString(), list.Priority },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(PriceListProjection.ToResponse(list));
    }
}

/// <summary>Restates a price list.</summary>
/// <param name="context">The Pricing data context.</param>
/// <param name="scope">Refuses a seller editing somebody else's list.</param>
/// <param name="audit">Records the change.</param>
internal sealed class UpdatePriceListCommandHandler(
    PricingDbContext context,
    PricingScope scope,
    IAuditLogger audit) : ICommandHandler<UpdatePriceListCommand, PriceListResponse>
{
    /// <summary>The audited action for a change to a price list.</summary>
    public const string AuditAction = "pricing.price-list.updated";

    public async Task<Result<PriceListResponse>> HandleAsync(
        UpdatePriceListCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var list = await context.PriceLists
            .Include(candidate => candidate.Items)
            .FirstOrDefaultAsync(candidate => candidate.Id == command.PriceListId, cancellationToken)
            .ConfigureAwait(false);

        if (list is null)
        {
            return PricingErrors.NotFound("price list");
        }

        if (!scope.CanWrite(list.VendorId))
        {
            return PricingErrors.OutOfScope;
        }

        var before = new { list.Name, Type = list.Type.ToString(), list.Priority, list.StartsAt, list.EndsAt };

        list.Update(
            command.Name,
            PriceListTypes.Parse(command.Type),
            command.Priority,
            command.StartsAt,
            command.EndsAt);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreatePriceListCommandHandler.AuditEntityType,
                EntityId = list.Id.ToString(),
                Before = before,
                After = new { list.Name, Type = list.Type.ToString(), list.Priority, list.StartsAt, list.EndsAt },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(PriceListProjection.ToResponse(list));
    }
}

/// <summary>
/// Turns a price list on or off.
/// </summary>
/// <remarks>
/// Its own command rather than a field on the update, because switching a sale on is the one
/// price-list action an operator takes under time pressure and it must not require restating the
/// window and the rank to do it.
/// </remarks>
/// <param name="context">The Pricing data context.</param>
/// <param name="scope">Refuses a seller switching somebody else's list.</param>
/// <param name="events">Announces the price change to Search and the alert subscribers.</param>
/// <param name="catalog">Resolves each affected offer's seller, which the event carries.</param>
/// <param name="audit">Records the switch.</param>
internal sealed class SetPriceListActiveCommandHandler(
    PricingDbContext context,
    PricingScope scope,
    PricingEventPublisher events,
    IProductCatalog catalog,
    IAuditLogger audit) : ICommandHandler<SetPriceListActiveCommand, PriceListResponse>
{
    /// <summary>The audited action for switching a price list.</summary>
    public const string AuditAction = "pricing.price-list.activation-changed";

    public async Task<Result<PriceListResponse>> HandleAsync(
        SetPriceListActiveCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var list = await context.PriceLists
            .Include(candidate => candidate.Items)
            .FirstOrDefaultAsync(candidate => candidate.Id == command.PriceListId, cancellationToken)
            .ConfigureAwait(false);

        if (list is null)
        {
            return PricingErrors.NotFound("price list");
        }

        if (!scope.CanWrite(list.VendorId))
        {
            return PricingErrors.OutOfScope;
        }

        if (list.IsActive == command.IsActive)
        {
            return Result.Success(PriceListProjection.ToResponse(list));
        }

        list.SetActive(command.IsActive);

        // Switching a list changes the price of everything in it, and Search and the price-drop
        // subscribers have no other way to find out. The previous price is not stated: it depended
        // on which other list was winning per offer, and guessing at it would be worse than saying
        // nothing.
        await PriceListEvents
            .AnnounceAsync(list, events, catalog, cancellationToken)
            .ConfigureAwait(false);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreatePriceListCommandHandler.AuditEntityType,
                EntityId = list.Id.ToString(),
                Before = new { IsActive = !command.IsActive },
                After = new { command.IsActive },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(PriceListProjection.ToResponse(list));
    }
}

/// <summary>Removes a price list and every price in it.</summary>
/// <param name="context">The Pricing data context.</param>
/// <param name="scope">Refuses a seller removing somebody else's list.</param>
/// <param name="audit">Records the removal.</param>
internal sealed class DeletePriceListCommandHandler(
    PricingDbContext context,
    PricingScope scope,
    IAuditLogger audit) : ICommandHandler<DeletePriceListCommand>
{
    /// <summary>The audited action for a removed price list.</summary>
    public const string AuditAction = "pricing.price-list.deleted";

    public async Task<Result> HandleAsync(DeletePriceListCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var list = await context.PriceLists
            .FirstOrDefaultAsync(candidate => candidate.Id == command.PriceListId, cancellationToken)
            .ConfigureAwait(false);

        if (list is null)
        {
            return Result.Failure(PricingErrors.NotFound("price list"));
        }

        if (!scope.CanWrite(list.VendorId))
        {
            return Result.Failure(PricingErrors.OutOfScope);
        }

        // Hard delete, cascading to the items. A price list holds no history worth keeping — what
        // an order was actually charged is snapshotted onto the order line, not read back from
        // here — so soft-deleting one would leave rows nothing will ever read.
        context.PriceLists.Remove(list);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreatePriceListCommandHandler.AuditEntityType,
                EntityId = list.Id.ToString(),
                Before = new { list.Code, list.Name },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Adds or restates a batch of prices.</summary>
/// <remarks>
/// A batch rather than a row at a time, because prices arrive from a spreadsheet. Every row is
/// validated against the catalogue before any of them is written, so a file with one bad SKU in it
/// fails as a file rather than half-applying.
/// </remarks>
/// <param name="context">The Pricing data context.</param>
/// <param name="scope">Refuses a seller pricing into somebody else's list.</param>
/// <param name="catalog">Validates the offers and supplies their sellers for the event.</param>
/// <param name="events">Announces each changed price.</param>
/// <param name="options">Supplies the batch ceiling.</param>
/// <param name="audit">Records the change.</param>
internal sealed class UpsertPriceListItemsCommandHandler(
    PricingDbContext context,
    PricingScope scope,
    IProductCatalog catalog,
    PricingEventPublisher events,
    IOptions<PricingOptions> options,
    IAuditLogger audit) : ICommandHandler<UpsertPriceListItemsCommand, IReadOnlyList<PriceListItemResponse>>
{
    /// <summary>The audited action for a batch of prices.</summary>
    public const string AuditAction = "pricing.price-list.items-changed";

    public async Task<Result<IReadOnlyList<PriceListItemResponse>>> HandleAsync(
        UpsertPriceListItemsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var limits = options.Value;

        if (command.Items.Count > limits.MaxPriceListItemsPerRequest)
        {
            return PricingErrors.TooManyItems(limits.MaxPriceListItemsPerRequest);
        }

        var list = await context.PriceLists
            .Include(candidate => candidate.Items)
            .FirstOrDefaultAsync(candidate => candidate.Id == command.PriceListId, cancellationToken)
            .ConfigureAwait(false);

        if (list is null)
        {
            return PricingErrors.NotFound("price list");
        }

        if (!scope.CanWrite(list.VendorId))
        {
            return PricingErrors.OutOfScope;
        }

        var listingIds = command.Items.Select(item => item.ListingId).Distinct().ToList();

        var listings = await catalog.FindListingsAsync(listingIds, cancellationToken).ConfigureAwait(false);

        if (limits.RequireKnownListing
            && listingIds.Find(id => !listings.ContainsKey(id)) is var missing
            && missing != Guid.Empty)
        {
            return PricingErrors.UnknownListingItem(missing);
        }

        // A seller's own list may not price another seller's offer. The vendor filter cannot say
        // this: the item carries no seller of its own, and the offer it points at lives in another
        // schema entirely.
        if (list.VendorId is { } owner
            && listings.Values.Any(listing => listing.VendorId != owner))
        {
            return PricingErrors.OutOfScope;
        }

        var written = new List<PriceListItemResponse>(command.Items.Count);

        foreach (var payload in command.Items)
        {
            var previous = list.Items
                .FirstOrDefault(item => item.ListingId == payload.ListingId && item.MinQuantity == payload.MinQuantity)
                ?.Price.Amount;

            var item = list.SetPrice(
                payload.ListingId,
                new Money(payload.Price, list.CurrencyCode),
                payload.MinQuantity);

            written.Add(new PriceListItemResponse(
                item.Id,
                item.PriceListId,
                item.ListingId,
                item.Price.Amount,
                item.MinQuantity));

            // Only the base tier moves the price a shopper sees, so only that one is announced. A
            // bulk-buy tier changing is not a price drop, and telling every alert subscriber it was
            // would train them to ignore the alert that matters.
            if (payload.MinQuantity == 1 && previous != payload.Price
                && listings.TryGetValue(payload.ListingId, out var listing))
            {
                events.PriceChanged(
                    payload.ListingId,
                    listing.VendorId,
                    previous,
                    payload.Price,
                    list.CurrencyCode,
                    list.Id);
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreatePriceListCommandHandler.AuditEntityType,
                EntityId = list.Id.ToString(),
                After = new { list.Code, Rows = written.Count },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success<IReadOnlyList<PriceListItemResponse>>(written);
    }
}

/// <summary>Removes one price.</summary>
/// <param name="context">The Pricing data context.</param>
/// <param name="scope">Refuses a seller editing somebody else's list.</param>
internal sealed class DeletePriceListItemCommandHandler(PricingDbContext context, PricingScope scope)
    : ICommandHandler<DeletePriceListItemCommand>
{
    public async Task<Result> HandleAsync(DeletePriceListItemCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var list = await context.PriceLists
            .Include(candidate => candidate.Items)
            .FirstOrDefaultAsync(candidate => candidate.Id == command.PriceListId, cancellationToken)
            .ConfigureAwait(false);

        if (list is null)
        {
            return Result.Failure(PricingErrors.NotFound("price list"));
        }

        if (!scope.CanWrite(list.VendorId))
        {
            return Result.Failure(PricingErrors.OutOfScope);
        }

        if (!list.RemoveItem(command.ItemId))
        {
            return Result.Failure(PricingErrors.NotFound("price"));
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Explains what an offer costs and which list decided it.</summary>
/// <param name="prices">The price-list walk.</param>
internal sealed class ResolvePriceQueryHandler(IPriceCatalog prices) : IQueryHandler<ResolvePriceQuery, EffectivePrice>
{
    public async Task<Result<EffectivePrice>> HandleAsync(
        ResolvePriceQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var price = await prices
            .FindAsync(query.ListingId, Math.Max(1, query.Quantity ?? 1), cancellationToken)
            .ConfigureAwait(false);

        return price is null ? PricingErrors.UnknownListing : Result.Success(price);
    }
}

/// <summary>Announces that every price in a list has taken effect, or stopped.</summary>
internal static class PriceListEvents
{
    /// <summary>Raises one <c>PriceChanged</c> per offer in the list.</summary>
    /// <remarks>
    /// The new price is the item's own, which is what the offer now costs if this list wins the
    /// walk and an upper bound on the change if it does not. Consumers treat it as a hint to
    /// reproject rather than as the final figure — the final figure is always a quote.
    /// </remarks>
    /// <param name="list">The list that was switched.</param>
    /// <param name="events">The publisher.</param>
    /// <param name="catalog">Resolves each offer's seller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task AnnounceAsync(
        PriceList list,
        PricingEventPublisher events,
        IProductCatalog catalog,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(catalog);

        var baseTier = list.Items.Where(item => item.MinQuantity == 1).ToList();

        if (baseTier.Count == 0)
        {
            return;
        }

        var listings = await catalog
            .FindListingsAsync([.. baseTier.Select(item => item.ListingId).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        foreach (var item in baseTier)
        {
            if (listings.TryGetValue(item.ListingId, out var listing))
            {
                events.PriceChanged(
                    item.ListingId,
                    listing.VendorId,
                    previousPrice: null,
                    list.IsActive ? item.Price.Amount : listing.SellingPrice,
                    list.CurrencyCode,
                    list.Id);
            }
        }
    }
}

/// <summary>Turns price lists into responses.</summary>
internal static class PriceListProjection
{
    /// <summary>States a price list.</summary>
    /// <param name="list">The list.</param>
    public static PriceListResponse ToResponse(PriceList list)
    {
        ArgumentNullException.ThrowIfNull(list);

        return new PriceListResponse(
            list.Id,
            list.VendorId,
            list.Code,
            list.Name,
            list.Type.ToString(),
            list.CurrencyCode,
            list.Priority,
            list.StartsAt,
            list.EndsAt,
            list.IsActive,
            list.Items.Count,
            list.CreatedAt);
    }
}
