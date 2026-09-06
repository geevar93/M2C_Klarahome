using System.Security.Cryptography;
using FluentValidation;
using KlaraHome.Contracts.Catalog;
using KlaraHome.Contracts.Media;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Reviews.Domain;
using KlaraHome.Modules.Reviews.Infrastructure;
using KlaraHome.Modules.Reviews.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Reviews.Application.Wishlists;

/// <summary>Reads a customer's list, resolved against today's catalogue.</summary>
/// <param name="CustomerId">The shopper.</param>
/// <param name="WishlistId">Which list, or null for their default.</param>
internal sealed record GetWishlistQuery(Guid CustomerId, Guid? WishlistId) : IQuery<WishlistResponse>;

/// <summary>Lists a customer's lists, without their contents.</summary>
/// <param name="CustomerId">The shopper.</param>
internal sealed record ListWishlistsQuery(Guid CustomerId) : IQuery<IReadOnlyList<WishlistResponse>>;

/// <summary>Saves something.</summary>
/// <param name="CustomerId">The shopper.</param>
/// <param name="VariantId">The sellable thing being saved.</param>
/// <param name="WishlistId">Which list, or null for their default.</param>
/// <param name="Note">What they wrote against it.</param>
/// <param name="Priority">Where it sits.</param>
internal sealed record AddToWishlistCommand(
    Guid CustomerId,
    Guid VariantId,
    Guid? WishlistId,
    string? Note,
    int Priority) : ICommand<WishlistResponse>;

/// <summary>Takes something off.</summary>
/// <param name="CustomerId">The shopper.</param>
/// <param name="VariantId">The sellable thing being removed.</param>
/// <param name="WishlistId">Which list, or null for their default.</param>
internal sealed record RemoveFromWishlistCommand(Guid CustomerId, Guid VariantId, Guid? WishlistId) : ICommand;

/// <summary>Opens a new named list.</summary>
/// <param name="CustomerId">The shopper.</param>
/// <param name="Name">What to call it.</param>
internal sealed record CreateWishlistCommand(Guid CustomerId, string? Name) : ICommand<WishlistResponse>;

/// <summary>Renames a list.</summary>
/// <param name="CustomerId">The shopper.</param>
/// <param name="WishlistId">The list.</param>
/// <param name="Name">The new name.</param>
internal sealed record RenameWishlistCommand(Guid CustomerId, Guid WishlistId, string? Name)
    : ICommand<WishlistResponse>;

/// <summary>Deletes a list. The default one cannot be deleted.</summary>
/// <param name="CustomerId">The shopper.</param>
/// <param name="WishlistId">The list.</param>
internal sealed record DeleteWishlistCommand(Guid CustomerId, Guid WishlistId) : ICommand;

/// <summary>Turns sharing on or off for a list.</summary>
/// <param name="CustomerId">The shopper.</param>
/// <param name="WishlistId">The list.</param>
/// <param name="Share">Whether it should be shareable.</param>
internal sealed record ShareWishlistCommand(Guid CustomerId, Guid WishlistId, bool Share)
    : ICommand<WishlistResponse>;

/// <summary>Reads a shared list by its token.</summary>
/// <param name="Token">The token from the link.</param>
internal sealed record GetSharedWishlistQuery(string? Token) : IQuery<SharedWishlistResponse>;

/// <summary>Rules a save has to satisfy.</summary>
internal sealed class AddToWishlistCommandValidator : AbstractValidator<AddToWishlistCommand>
{
    public AddToWishlistCommandValidator()
    {
        RuleFor(command => command.CustomerId).NotEmpty();
        RuleFor(command => command.VariantId).NotEmpty();
        RuleFor(command => command.Note).MaximumLength(WishlistItem.MaxNoteLength);
        RuleFor(command => command.Priority).GreaterThanOrEqualTo(0);
    }
}

/// <summary>Rules a new list has to satisfy.</summary>
internal sealed class CreateWishlistCommandValidator : AbstractValidator<CreateWishlistCommand>
{
    public CreateWishlistCommandValidator()
    {
        RuleFor(command => command.CustomerId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(Wishlist.MaxNameLength);
    }
}

/// <summary>
/// Finds or opens a customer's list, and turns one into a response.
/// </summary>
/// <remarks>
/// <para>
/// Shared by every handler in this file, because "get me this customer's list, making the default
/// one if they have none" is the first line of almost all of them and getting it subtly different in
/// six places is how a customer ends up with two default lists.
/// </para>
/// <para>
/// The read resolves every saved item against the catalogue at the moment it is read. A wishlist is
/// a live shopping surface: a page quoting the price at the moment of saving would be wrong for
/// almost every item on it, and the "add to basket" button beside it would be a surprise.
/// </para>
/// </remarks>
/// <param name="context">The Reviews data context.</param>
/// <param name="catalogue">Resolves the cards.</param>
/// <param name="media">Resolves their pictures.</param>
internal sealed class WishlistReader(
    ReviewsDbContext context,
    IProductProjectionSource catalogue,
    IMediaLibrary media)
{
    /// <summary>The named list, or the customer's default, creating it if they have none.</summary>
    /// <param name="customerId">The shopper.</param>
    /// <param name="wishlistId">Which list, or null for their default.</param>
    /// <param name="create">Whether to open the default list when they have none.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Wishlist?> FindAsync(
        Guid customerId,
        Guid? wishlistId,
        bool create,
        CancellationToken cancellationToken)
    {
        var lists = context.Wishlists.Include(list => list.Items);

        var list = wishlistId is { } id
            ? await lists
                .FirstOrDefaultAsync(row => row.Id == id && row.CustomerId == customerId, cancellationToken)
                .ConfigureAwait(false)
            : await lists
                .FirstOrDefaultAsync(row => row.CustomerId == customerId && row.IsDefault, cancellationToken)
                .ConfigureAwait(false);

        if (list is not null || !create || wishlistId is not null)
        {
            return list;
        }

        list = Wishlist.Open(customerId, name: null, isDefault: true);
        context.Wishlists.Add(list);

        return list;
    }

    /// <summary>Turns a list into a response, every card resolved against today's catalogue.</summary>
    /// <param name="list">The list.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<WishlistResponse> ToResponseAsync(Wishlist list, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(list);

        var items = await ResolveAsync(list.Items, cancellationToken).ConfigureAwait(false);

        return new WishlistResponse(list.Id, list.Name, list.IsDefault, list.ItemCount, list.ShareToken, items);
    }

    /// <summary>
    /// Resolves the saved variants into cards.
    /// </summary>
    /// <remarks>
    /// One call for the whole list rather than one per item, and the buy box is taken from the
    /// catalogue's own answer rather than picked here. A wishlist tile quoting a different seller
    /// from the product page it links to is exactly the disagreement <c>IsBuyBox</c> exists to
    /// prevent.
    /// </remarks>
    /// <param name="items">What is on the list.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<WishlistItemResponse>> ResolveAsync(
        IReadOnlyList<WishlistItem> items,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            return [];
        }

        var variantIds = items.Select(item => item.VariantId).Distinct().ToArray();

        var projections = await catalogue
            .FindByVariantsAsync(variantIds, cancellationToken)
            .ConfigureAwait(false);

        var winners = projections
            .Where(projection => projection.IsBuyBox)
            .GroupBy(projection => projection.VariantId)
            .ToDictionary(group => group.Key, group => group.First());

        var fileIds = winners.Values
            .Where(projection => projection.PrimaryImageFileId is not null)
            .Select(projection => projection.PrimaryImageFileId!.Value)
            .Distinct()
            .ToArray();

        var files = fileIds.Length == 0
            ? null
            : await media.GetManyAsync(fileIds, cancellationToken).ConfigureAwait(false);

        return
        [
            .. items
                .OrderBy(item => item.Priority)
                .ThenByDescending(item => item.CreatedAt)
                .Select(item =>
                {
                    winners.TryGetValue(item.VariantId, out var offer);

                    var imageId = offer?.PrimaryImageFileId;

                    return new WishlistItemResponse(
                        item.VariantId,
                        item.ProductId,
                        offer?.ListingId,
                        offer?.VariantName,
                        offer?.ProductSlug,
                        offer?.Sku,
                        imageId,
                        imageId is not null && files is not null && files.TryGetValue(imageId.Value, out var file)
                            ? file.Url
                            : null,
                        offer?.Mrp,
                        offer?.SellingPrice,
                        offer?.CurrencyCode,

                        // Nothing being sold is a legitimate state for a saved item, and the card says
                        // so rather than disappearing. A shopper whose saved thing has gone out of
                        // the catalogue should see that it has, not find their list silently shorter.
                        offer?.IsPurchasable ?? false,
                        offer?.RatingAverage,
                        offer?.RatingCount ?? 0,
                        item.Note,
                        item.Priority,
                        item.CreatedAt);
                }),
        ];
    }
}

/// <summary>Reads a customer's list.</summary>
/// <param name="context">The Reviews data context.</param>
/// <param name="reader">Finds the list and resolves its cards.</param>
internal sealed class GetWishlistQueryHandler(ReviewsDbContext context, WishlistReader reader)
    : IQueryHandler<GetWishlistQuery, WishlistResponse>
{
    public async Task<Result<WishlistResponse>> HandleAsync(
        GetWishlistQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var list = await reader
            .FindAsync(query.CustomerId, query.WishlistId, create: true, cancellationToken)
            .ConfigureAwait(false);

        if (list is null)
        {
            return Result.Failure<WishlistResponse>(ReviewErrors.NotFound("list"));
        }

        // A default list opened by this read is saved, so the customer has one from then on. It costs
        // one insert on a shopper's first visit to the page and removes an entire branch from every
        // write path afterwards.
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(await reader.ToResponseAsync(list, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Lists a customer's lists, without loading what is on them.</summary>
/// <remarks>
/// Deliberately without the items. This is the picker on "save to which list", and resolving every
/// card on every list to render a dropdown would be several catalogue reads for a menu.
/// </remarks>
/// <param name="context">The Reviews data context.</param>
internal sealed class ListWishlistsQueryHandler(ReviewsDbContext context)
    : IQueryHandler<ListWishlistsQuery, IReadOnlyList<WishlistResponse>>
{
    public async Task<Result<IReadOnlyList<WishlistResponse>>> HandleAsync(
        ListWishlistsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var lists = await context.Wishlists
            .AsNoTracking()
            .Where(list => list.CustomerId == query.CustomerId)
            .OrderByDescending(list => list.IsDefault)
            .ThenBy(list => list.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<WishlistResponse> responses =
        [
            .. lists.Select(list => new WishlistResponse(
                list.Id,
                list.Name,
                list.IsDefault,
                list.ItemCount,
                list.ShareToken,
                [])),
        ];

        return Result.Success(responses);
    }
}

/// <summary>
/// Saves something to a list.
/// </summary>
/// <remarks>
/// The variant is resolved against the catalogue first, both to confirm it exists and to get the
/// product it belongs to — this module holds no mapping between them and must not invent one. The
/// add itself is idempotent on the variant, so a retried request updates the note rather than
/// producing a second row.
/// </remarks>
/// <param name="context">The Reviews data context.</param>
/// <param name="reader">Finds the list and resolves its cards.</param>
/// <param name="catalogue">Confirms the variant and names its product.</param>
internal sealed class AddToWishlistCommandHandler(
    ReviewsDbContext context,
    WishlistReader reader,
    IProductProjectionSource catalogue) : ICommandHandler<AddToWishlistCommand, WishlistResponse>
{
    public async Task<Result<WishlistResponse>> HandleAsync(
        AddToWishlistCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var projections = await catalogue
            .FindByVariantsAsync([command.VariantId], cancellationToken)
            .ConfigureAwait(false);

        if (projections.Count == 0)
        {
            return Result.Failure<WishlistResponse>(ReviewErrors.UnknownVariant);
        }

        var list = await reader
            .FindAsync(command.CustomerId, command.WishlistId, create: true, cancellationToken)
            .ConfigureAwait(false);

        if (list is null)
        {
            return Result.Failure<WishlistResponse>(ReviewErrors.NotFound("list"));
        }

        // Checked before the add rather than after, so an item already on the list does not count
        // against the ceiling — re-saving something must never be the request that is refused.
        if (list.ItemCount >= Wishlist.MaxItems && list.Items.All(item => item.VariantId != command.VariantId))
        {
            return Result.Failure<WishlistResponse>(ReviewErrors.WishlistFull(Wishlist.MaxItems));
        }

        list.Add(command.VariantId, projections[0].ProductId, command.Note, command.Priority);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(await reader.ToResponseAsync(list, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Takes something off a list. Succeeds whether or not it was there.</summary>
/// <remarks>
/// Idempotent, because the heart icon is a toggle and a double tap on a slow connection must not
/// answer 404. There is nothing a shopper could do differently if told.
/// </remarks>
/// <param name="context">The Reviews data context.</param>
/// <param name="reader">Finds the list.</param>
internal sealed class RemoveFromWishlistCommandHandler(ReviewsDbContext context, WishlistReader reader)
    : ICommandHandler<RemoveFromWishlistCommand>
{
    public async Task<Result> HandleAsync(RemoveFromWishlistCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var list = await reader
            .FindAsync(command.CustomerId, command.WishlistId, create: false, cancellationToken)
            .ConfigureAwait(false);

        if (list is null || !list.Remove(command.VariantId))
        {
            return Result.Success();
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Opens a new named list.</summary>
/// <param name="context">The Reviews data context.</param>
/// <param name="reader">Turns the new list into a response.</param>
internal sealed class CreateWishlistCommandHandler(ReviewsDbContext context, WishlistReader reader)
    : ICommandHandler<CreateWishlistCommand, WishlistResponse>
{
    public async Task<Result<WishlistResponse>> HandleAsync(
        CreateWishlistCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var hasDefault = await context.Wishlists
            .AnyAsync(list => list.CustomerId == command.CustomerId && list.IsDefault, cancellationToken)
            .ConfigureAwait(false);

        // The first list a customer creates by hand becomes their default if they somehow have none.
        // The database enforces that there is at most one; this is what stops there being none.
        var list = Wishlist.Open(command.CustomerId, command.Name, isDefault: !hasDefault);

        context.Wishlists.Add(list);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(await reader.ToResponseAsync(list, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Renames a list.</summary>
/// <param name="context">The Reviews data context.</param>
/// <param name="reader">Finds the list and turns it into a response.</param>
internal sealed class RenameWishlistCommandHandler(ReviewsDbContext context, WishlistReader reader)
    : ICommandHandler<RenameWishlistCommand, WishlistResponse>
{
    public async Task<Result<WishlistResponse>> HandleAsync(
        RenameWishlistCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var list = await reader
            .FindAsync(command.CustomerId, command.WishlistId, create: false, cancellationToken)
            .ConfigureAwait(false);

        if (list is null)
        {
            return Result.Failure<WishlistResponse>(ReviewErrors.NotFound("list"));
        }

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Result.Failure<WishlistResponse>(
                Error.Validation("WISHLIST_NAME_REQUIRED", "Give the list a name."));
        }

        list.Rename(command.Name);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(await reader.ToResponseAsync(list, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Deletes a list, unless it is the one every customer must have.</summary>
/// <param name="context">The Reviews data context.</param>
/// <param name="reader">Finds the list.</param>
internal sealed class DeleteWishlistCommandHandler(ReviewsDbContext context, WishlistReader reader)
    : ICommandHandler<DeleteWishlistCommand>
{
    public async Task<Result> HandleAsync(DeleteWishlistCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var list = await reader
            .FindAsync(command.CustomerId, command.WishlistId, create: false, cancellationToken)
            .ConfigureAwait(false);

        if (list is null)
        {
            return Result.Failure(ReviewErrors.NotFound("list"));
        }

        if (list.IsDefault)
        {
            return Result.Failure(ReviewErrors.CannotDeleteDefaultList);
        }

        // A hard delete, cascading to the items. A saved item somebody unsaved is not a record
        // anybody has ever wanted to audit.
        context.Wishlists.Remove(list);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>
/// Turns sharing on or off.
/// </summary>
/// <remarks>
/// The token is 32 bytes from the cryptographic generator, hex encoded, and is minted afresh every
/// time sharing is turned on. Reissuing rather than reusing is what makes "stop sharing" mean
/// something: a link somebody has already been sent stops working, which is the only behaviour a
/// person turning sharing off expects.
/// </remarks>
/// <param name="context">The Reviews data context.</param>
/// <param name="reader">Finds the list and turns it into a response.</param>
internal sealed class ShareWishlistCommandHandler(ReviewsDbContext context, WishlistReader reader)
    : ICommandHandler<ShareWishlistCommand, WishlistResponse>
{
    public async Task<Result<WishlistResponse>> HandleAsync(
        ShareWishlistCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var list = await reader
            .FindAsync(command.CustomerId, command.WishlistId, create: false, cancellationToken)
            .ConfigureAwait(false);

        if (list is null)
        {
            return Result.Failure<WishlistResponse>(ReviewErrors.NotFound("list"));
        }

        if (command.Share)
        {
            list.Share(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());
        }
        else
        {
            list.Unshare();
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(await reader.ToResponseAsync(list, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>
/// Reads a shared list by its token.
/// </summary>
/// <remarks>
/// Anonymous, and it returns a narrower shape than the owner sees: no share token, so a viewer
/// cannot re-share what they were shown, and no notes, because a note on a gift list is usually
/// written to the buyer rather than to the recipient. An unknown or revoked token answers not-found,
/// which is also what a token belonging to a deleted list answers.
/// </remarks>
/// <param name="context">The Reviews data context.</param>
/// <param name="reader">Resolves the cards.</param>
/// <param name="options">Whether a share link has a lifetime at all.</param>
internal sealed class GetSharedWishlistQueryHandler(
    ReviewsDbContext context,
    WishlistReader reader,
    IOptionsMonitor<ReviewsOptions> options)
    : IQueryHandler<GetSharedWishlistQuery, SharedWishlistResponse>
{
    public async Task<Result<SharedWishlistResponse>> HandleAsync(
        GetSharedWishlistQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query.Token))
        {
            return Result.Failure<SharedWishlistResponse>(ReviewErrors.UnknownShareToken);
        }

        var token = query.Token.Trim().ToLowerInvariant();

        var list = await context.Wishlists
            .AsNoTracking()
            .Include(row => row.Items)
            .FirstOrDefaultAsync(row => row.ShareToken == token, cancellationToken)
            .ConfigureAwait(false);

        if (list is null)
        {
            return Result.Failure<SharedWishlistResponse>(ReviewErrors.UnknownShareToken);
        }

        var lifetime = options.CurrentValue.ShareLinkDays;

        // Zero means forever, which is the default: a gift list that stopped working the week before
        // a birthday would be worse than one that stays live, and the owner can revoke it at any time.
        if (lifetime > 0 && list.UpdatedAt is { } updated && updated.AddDays(lifetime) < DateTimeOffset.UtcNow)
        {
            return Result.Failure<SharedWishlistResponse>(ReviewErrors.UnknownShareToken);
        }

        var items = await reader.ResolveAsync(list.Items, cancellationToken).ConfigureAwait(false);

        return Result.Success(new SharedWishlistResponse(
            list.Name,
            list.ItemCount,
            [.. items.Select(item => item with { Note = null })]));
    }
}
