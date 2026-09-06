using FluentValidation;
using KlaraHome.Contracts.Catalog;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Pricing;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Carts.Domain;
using KlaraHome.Modules.Carts.Infrastructure;
using KlaraHome.Modules.Carts.Infrastructure.Carts;
using KlaraHome.Modules.Carts.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Carts.Application.Carts;

/// <summary>Reads the caller's own basket, priced and validated.</summary>
internal sealed record GetCartQuery : IQuery<CartResponse>;

/// <summary>Reads only the itemised price of the caller's own basket.</summary>
/// <remarks>
/// The same calculation the cart read returns, exposed on its own because a checkout summary
/// re-renders the totals far more often than it re-renders the lines.
/// </remarks>
internal sealed record GetCartQuoteQuery : IQuery<QuoteResult>;

/// <summary>Puts units of an offer into the basket.</summary>
/// <param name="ListingId">The offer.</param>
/// <param name="Quantity">How many units to add.</param>
internal sealed record AddCartItemCommand(Guid ListingId, int Quantity) : ICommand<CartResponse>;

/// <summary>Changes a line's quantity, or sets it aside.</summary>
/// <param name="LineId">The line.</param>
/// <param name="Quantity">The new quantity, or null to leave it.</param>
/// <param name="SavedForLater">Whether it is set aside, or null to leave it.</param>
internal sealed record UpdateCartItemCommand(Guid LineId, int? Quantity, bool? SavedForLater)
    : ICommand<CartResponse>;

/// <summary>Takes a line out of the basket.</summary>
/// <param name="LineId">The line.</param>
internal sealed record RemoveCartItemCommand(Guid LineId) : ICommand<CartResponse>;

/// <summary>Empties the basket.</summary>
internal sealed record ClearCartCommand : ICommand<CartResponse>;

/// <summary>
/// Folds the browser's basket into the signed-in shopper's.
/// </summary>
/// <remarks>
/// The merge also happens on its own, on the first read after signing in, so this endpoint exists
/// for a storefront that wants to do it explicitly and get the merged basket back in one call. It
/// is idempotent: a second call finds no guest cookie and returns the same basket.
/// </remarks>
internal sealed record MergeCartCommand : ICommand<CartResponse>;

/// <summary>Records a coupon code against the basket.</summary>
/// <param name="Code">The code the shopper typed.</param>
internal sealed record ApplyCouponCommand(string Code) : ICommand<CartResponse>;

/// <summary>Takes the coupon off the basket.</summary>
internal sealed record RemoveCouponCommand : ICommand<CartResponse>;

/// <summary>Rejects an add that could never be stored.</summary>
internal sealed class AddCartItemValidator : AbstractValidator<AddCartItemCommand>
{
    public AddCartItemValidator()
    {
        RuleFor(command => command.ListingId).NotEmpty();
        RuleFor(command => command.Quantity).GreaterThan(0);
    }
}

/// <summary>Rejects a change that could never be stored.</summary>
internal sealed class UpdateCartItemValidator : AbstractValidator<UpdateCartItemCommand>
{
    public UpdateCartItemValidator()
    {
        RuleFor(command => command.LineId).NotEmpty();
        RuleFor(command => command.Quantity).GreaterThan(0).When(command => command.Quantity is not null);
    }
}

/// <summary>Rejects a coupon code that could never be one.</summary>
internal sealed class ApplyCouponValidator : AbstractValidator<ApplyCouponCommand>
{
    public ApplyCouponValidator() => RuleFor(command => command.Code).NotEmpty().MaximumLength(48);
}

/// <summary>
/// Everything the cart handlers share: find the basket, price it, and hand it back.
/// </summary>
/// <remarks>
/// Eight handlers do the same three things around one small change each. Putting the three here
/// means the change is the only thing each handler contains, and — more importantly — that no
/// handler can accidentally skip the revalidation, which is the step that stops a stale basket
/// reaching a payment screen.
/// </remarks>
/// <param name="context">The Cart data context.</param>
/// <param name="resolver">Finds or opens the caller's basket.</param>
/// <param name="renderer">Prices and validates it.</param>
internal sealed class CartWorkflow(CartsDbContext context, CartResolver resolver, CartRenderer renderer)
{
    /// <summary>The caller's basket, or null when they have none.</summary>
    /// <param name="create">Whether to open one. A read does not; a write does.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<Cart?> FindAsync(bool create, CancellationToken cancellationToken)
        => resolver.ResolveAsync(create, cancellationToken);

    /// <summary>How long an untouched basket lives.</summary>
    public TimeSpan Lifetime => resolver.Lifetime;

    /// <summary>Saves, then prices and validates.</summary>
    /// <param name="cart">The basket that was just changed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<CartResponse>> CommitAsync(Cart cart, CancellationToken cancellationToken)
    {
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(
            await renderer.RenderAsync(cart, new CartRenderContext(), cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Prices and validates without saving.</summary>
    /// <param name="cart">The basket.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<CartResponse> RenderAsync(Cart cart, CancellationToken cancellationToken)
        => renderer.RenderAsync(cart, new CartRenderContext(), cancellationToken);

    /// <summary>
    /// What a caller with no basket at all is shown.
    /// </summary>
    /// <remarks>
    /// An empty basket rather than a 404, and no row is written for it. The storefront always has a
    /// cart to render, a shopper who has added nothing has an empty one, and opening a database row
    /// for every visitor who loads a page would fill the table with baskets nobody ever used.
    /// </remarks>
    /// <param name="currencyCode">ISO 4217 code the store trades in.</param>
    /// <param name="expiresAt">A nominal expiry, so the shape matches a real basket.</param>
    public static CartResponse Empty(string currencyCode, DateTimeOffset expiresAt)
        => new(
            Guid.Empty,
            CustomerId: null,
            nameof(CartStatus.Active),
            currencyCode,
            CouponCode: null,
            LineCount: 0,
            expiresAt,
            [],
            [],
            [],
            Quote: null,
            [],
            IsReadyForCheckout: false);
}

/// <summary>Reads the caller's own basket.</summary>
/// <param name="workflow">The shared cart steps.</param>
/// <param name="context">Saves the merge a read may have performed.</param>
/// <param name="settings">Supplies the store currency for an empty basket.</param>
/// <param name="options">Supplies the nominal expiry for an empty basket.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class GetCartQueryHandler(
    CartWorkflow workflow,
    CartsDbContext context,
    IStoreSettings settings,
    IOptions<CartsOptions> options,
    IClock clock) : IQueryHandler<GetCartQuery, CartResponse>
{
    public async Task<Result<CartResponse>> HandleAsync(GetCartQuery query, CancellationToken cancellationToken)
    {
        var cart = await workflow.FindAsync(create: false, cancellationToken).ConfigureAwait(false);

        if (cart is null)
        {
            var localization = await settings.GetAsync<LocalizationSettings>(cancellationToken).ConfigureAwait(false);

            return Result.Success(CartWorkflow.Empty(
                localization.CurrencyCode,
                clock.UtcNow.AddDays(options.Value.CartLifetimeDays)));
        }

        // A read that merged a guest basket has changes to save. It is the one write a GET makes,
        // and it is deliberate: a shopper who signs in and reloads must find their basket already
        // whole, whether or not the storefront remembered to call the merge endpoint.
        if (context.ChangeTracker.HasChanges())
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result.Success(await workflow.RenderAsync(cart, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Reads only the itemised price of the caller's own basket.</summary>
/// <param name="workflow">The shared cart steps.</param>
internal sealed class GetCartQuoteQueryHandler(CartWorkflow workflow) : IQueryHandler<GetCartQuoteQuery, QuoteResult>
{
    public async Task<Result<QuoteResult>> HandleAsync(GetCartQuoteQuery query, CancellationToken cancellationToken)
    {
        var cart = await workflow.FindAsync(create: false, cancellationToken).ConfigureAwait(false);

        if (cart is null)
        {
            return CartsErrors.CartEmpty;
        }

        var rendered = await workflow.RenderAsync(cart, cancellationToken).ConfigureAwait(false);

        return rendered.Quote is { } quote ? Result.Success(quote) : CartsErrors.CartEmpty;
    }
}

/// <summary>Puts units of an offer into the basket.</summary>
/// <param name="workflow">The shared cart steps.</param>
/// <param name="catalog">Checks the offer is real and purchasable, and reads its price.</param>
/// <param name="settings">Supplies the per-line quantity ceiling.</param>
/// <param name="options">Supplies the basket's line ceiling.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class AddCartItemCommandHandler(
    CartWorkflow workflow,
    IProductCatalog catalog,
    IStoreSettings settings,
    IOptions<CartsOptions> options,
    IClock clock) : ICommandHandler<AddCartItemCommand, CartResponse>
{
    public async Task<Result<CartResponse>> HandleAsync(
        AddCartItemCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Across a module boundary and therefore over the contract, never a join
        // (docs/01-architecture.md §2.1).
        var listing = await catalog.FindListingAsync(command.ListingId, cancellationToken).ConfigureAwait(false);

        if (listing is null || !listing.IsPurchasable)
        {
            return CartsErrors.UnknownListing;
        }

        var cart = await workflow.FindAsync(create: true, cancellationToken).ConfigureAwait(false);

        if (cart is null || !cart.IsOpen)
        {
            return CartsErrors.CartClosed;
        }

        // The ceiling counts distinct offers, and adding more of something already in the basket
        // never trips it: a shopper who has reached the limit must still be able to buy two of what
        // they already chose.
        var alreadyPresent = cart.Lines.Any(line => line.ListingId == command.ListingId);

        if (!alreadyPresent && cart.Lines.Count >= options.Value.MaxLines)
        {
            return CartsErrors.TooManyLines(options.Value.MaxLines);
        }

        var commerce = await settings.GetAsync<CommerceSettings>(cancellationToken).ConfigureAwait(false);

        // The tighter of the store's own limit and the seller's, because a seller who says "two per
        // customer" means it and the store's ceiling is a backstop rather than a permission.
        var ceiling = Math.Min(commerce.MaxQuantityPerLine, listing.MaxOrderQuantity ?? int.MaxValue);

        cart.Add(
            listing.ListingId,
            listing.VendorId,
            command.Quantity,
            listing.SellingPrice,
            ceiling,
            clock.UtcNow,
            workflow.Lifetime);

        return await workflow.CommitAsync(cart, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Changes a line's quantity, or sets it aside.</summary>
/// <param name="workflow">The shared cart steps.</param>
/// <param name="catalog">Supplies the seller's own quantity cap.</param>
/// <param name="settings">Supplies the store's per-line ceiling.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class UpdateCartItemCommandHandler(
    CartWorkflow workflow,
    IProductCatalog catalog,
    IStoreSettings settings,
    IClock clock) : ICommandHandler<UpdateCartItemCommand, CartResponse>
{
    public async Task<Result<CartResponse>> HandleAsync(
        UpdateCartItemCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var cart = await workflow.FindAsync(create: false, cancellationToken).ConfigureAwait(false);

        if (cart is null)
        {
            return CartsErrors.NotFound("basket");
        }

        if (!cart.IsOpen)
        {
            return CartsErrors.CartClosed;
        }

        if (cart.FindLine(command.LineId) is not { } line)
        {
            return CartsErrors.NotFound("basket line");
        }

        if (command.Quantity is { } quantity)
        {
            var commerce = await settings.GetAsync<CommerceSettings>(cancellationToken).ConfigureAwait(false);
            var listing = await catalog.FindListingAsync(line.ListingId, cancellationToken).ConfigureAwait(false);
            var ceiling = Math.Min(commerce.MaxQuantityPerLine, listing?.MaxOrderQuantity ?? int.MaxValue);

            line.SetQuantity(Math.Min(quantity, ceiling));
        }

        if (command.SavedForLater is { } saved)
        {
            line.SetSavedForLater(saved);
        }

        cart.Touch(clock.UtcNow, workflow.Lifetime);

        return await workflow.CommitAsync(cart, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Takes a line out of the basket.</summary>
/// <param name="workflow">The shared cart steps.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class RemoveCartItemCommandHandler(CartWorkflow workflow, IClock clock)
    : ICommandHandler<RemoveCartItemCommand, CartResponse>
{
    public async Task<Result<CartResponse>> HandleAsync(
        RemoveCartItemCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var cart = await workflow.FindAsync(create: false, cancellationToken).ConfigureAwait(false);

        if (cart is null)
        {
            return CartsErrors.NotFound("basket");
        }

        if (!cart.IsOpen)
        {
            return CartsErrors.CartClosed;
        }

        if (cart.FindLine(command.LineId) is not { } line)
        {
            return CartsErrors.NotFound("basket line");
        }

        cart.Remove(line, clock.UtcNow, workflow.Lifetime);

        return await workflow.CommitAsync(cart, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Empties the basket.</summary>
/// <param name="workflow">The shared cart steps.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class ClearCartCommandHandler(CartWorkflow workflow, IClock clock)
    : ICommandHandler<ClearCartCommand, CartResponse>
{
    public async Task<Result<CartResponse>> HandleAsync(ClearCartCommand command, CancellationToken cancellationToken)
    {
        var cart = await workflow.FindAsync(create: false, cancellationToken).ConfigureAwait(false);

        if (cart is null)
        {
            return CartsErrors.NotFound("basket");
        }

        if (!cart.IsOpen)
        {
            return CartsErrors.CartClosed;
        }

        cart.Clear(clock.UtcNow, workflow.Lifetime);

        return await workflow.CommitAsync(cart, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Folds the browser's basket into the signed-in shopper's.</summary>
/// <param name="workflow">The shared cart steps, which perform the merge on resolution.</param>
/// <param name="scope">Checks there is somebody to merge into.</param>
internal sealed class MergeCartCommandHandler(CartWorkflow workflow, CartsScope scope)
    : ICommandHandler<MergeCartCommand, CartResponse>
{
    public async Task<Result<CartResponse>> HandleAsync(MergeCartCommand command, CancellationToken cancellationToken)
    {
        if (!scope.IsSignedIn)
        {
            return CartsErrors.SignInRequired;
        }

        // The merge itself is the resolver's, because it must also happen on a plain read. This
        // handler exists so a storefront can ask for it explicitly and get the merged basket back
        // in one call rather than two.
        var cart = await workflow.FindAsync(create: true, cancellationToken).ConfigureAwait(false);

        return cart is null
            ? CartsErrors.NotFound("basket")
            : await workflow.CommitAsync(cart, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Records a coupon code against the basket.</summary>
/// <param name="workflow">The shared cart steps.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class ApplyCouponCommandHandler(CartWorkflow workflow, IClock clock)
    : ICommandHandler<ApplyCouponCommand, CartResponse>
{
    public async Task<Result<CartResponse>> HandleAsync(
        ApplyCouponCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var cart = await workflow.FindAsync(create: false, cancellationToken).ConfigureAwait(false);

        if (cart is null)
        {
            return CartsErrors.CartEmpty;
        }

        if (!cart.IsOpen)
        {
            return CartsErrors.CartClosed;
        }

        // The code is recorded, not verified. Whether it applies is the quote engine's answer, it
        // is re-asked on every render, and a code that was refused comes back on the response as a
        // reason the shopper can read rather than as an error that loses their basket.
        cart.SetCoupon(command.Code, clock.UtcNow, workflow.Lifetime);

        return await workflow.CommitAsync(cart, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Takes the coupon off the basket.</summary>
/// <param name="workflow">The shared cart steps.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class RemoveCouponCommandHandler(CartWorkflow workflow, IClock clock)
    : ICommandHandler<RemoveCouponCommand, CartResponse>
{
    public async Task<Result<CartResponse>> HandleAsync(
        RemoveCouponCommand command,
        CancellationToken cancellationToken)
    {
        var cart = await workflow.FindAsync(create: false, cancellationToken).ConfigureAwait(false);

        if (cart is null)
        {
            return CartsErrors.NotFound("basket");
        }

        if (!cart.IsOpen)
        {
            return CartsErrors.CartClosed;
        }

        cart.SetCoupon(null, clock.UtcNow, workflow.Lifetime);

        return await workflow.CommitAsync(cart, cancellationToken).ConfigureAwait(false);
    }
}
