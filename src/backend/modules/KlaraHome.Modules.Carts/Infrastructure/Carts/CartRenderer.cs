using KlaraHome.Contracts.Catalog;
using KlaraHome.Contracts.Inventory;
using KlaraHome.Contracts.Pricing;
using KlaraHome.Contracts.Shipping;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Modules.Carts.Application.Carts;
using KlaraHome.Modules.Carts.Domain;

namespace KlaraHome.Modules.Carts.Infrastructure.Carts;

/// <summary>
/// What the caller knows that changes how a basket renders.
/// </summary>
/// <remarks>
/// A cart page knows none of it and passes the defaults; a checkout review knows all of it. The
/// same code renders both, which is what stops the review screen quietly disagreeing with the
/// basket the shopper just looked at.
/// </remarks>
/// <param name="PlaceOfSupplyStateId">The destination state, once an address has been chosen.</param>
/// <param name="Pincode">The destination PIN code, for the serviceability check.</param>
/// <param name="PaymentMethod">How it would be paid for.</param>
/// <param name="ShippingByVendor">What delivery costs per seller, once a service has been chosen.</param>
/// <param name="IsFirstOrder">Whether this would be the shopper's first order.</param>
/// <param name="WalletRedeemRequested">How much store credit to try to apply.</param>
internal sealed record CartRenderContext(
    Guid? PlaceOfSupplyStateId = null,
    string? Pincode = null,
    QuotePaymentMethod PaymentMethod = QuotePaymentMethod.Prepaid,
    IReadOnlyDictionary<Guid, decimal>? ShippingByVendor = null,
    bool IsFirstOrder = false,
    decimal WalletRedeemRequested = 0m);

/// <summary>
/// Turns a stored basket into the thing the storefront renders: priced, grouped by seller, and
/// carrying one plain sentence per problem.
/// </summary>
/// <remarks>
/// <para>
/// This is the single place cart validation lives, and it deliberately answers <em>every</em>
/// question at once rather than failing on the first: a shopper whose basket has three problems
/// should be told all three, not made to fix them one request at a time.
/// </para>
/// <para>
/// It computes no money. Every figure comes from <see cref="IPriceQuoteEngine"/>, which is the only
/// thing on this platform permitted to price anything; what this class adds is which lines the
/// quote should have covered and why one of them did not.
/// </para>
/// <para>
/// It writes nothing, and that matters: it is called from a <c>GET</c>. A line whose stock has run
/// out is <em>reported</em> here and reduced by the handler the shopper next calls, because
/// silently editing a basket during a page load is how a shopper ends up paying for a quantity they
/// never chose.
/// </para>
/// </remarks>
/// <param name="catalog">What the offers are.</param>
/// <param name="vendors">Who sells them, and where they deliver.</param>
/// <param name="stock">How many there are.</param>
/// <param name="engine">The one calculation engine.</param>
/// <param name="deliveries">Whether the store delivers to the chosen address at all.</param>
internal sealed class CartRenderer(
    IProductCatalog catalog,
    IVendorDirectory vendors,
    IStockAvailability stock,
    IPriceQuoteEngine engine,
    IShippingOptions deliveries)
{
    /// <summary>Prices and validates a basket.</summary>
    /// <param name="cart">The basket.</param>
    /// <param name="context">What the caller knows.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CartResponse> RenderAsync(
        Cart cart,
        CartRenderContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cart);
        ArgumentNullException.ThrowIfNull(context);

        var active = cart.Lines.Where(line => !line.SavedForLater).OrderBy(line => line.AddedAt).ToList();
        var setAside = cart.Lines.Where(line => line.SavedForLater).OrderBy(line => line.AddedAt).ToList();

        var listingIds = cart.Lines.Select(line => line.ListingId).Distinct().ToArray();

        // Three contract calls for the whole basket rather than three per line. Every one of them
        // is a batch read for exactly this reason.
        var listings = listingIds.Length == 0
            ? new Dictionary<Guid, ListingSummary>()
            : await catalog.FindListingsAsync(listingIds, cancellationToken).ConfigureAwait(false);

        var vendorIds = listings.Values.Select(listing => listing.VendorId)
            .Concat(cart.Lines.Select(line => line.VendorId))
            .Distinct()
            .ToArray();

        var sellers = vendorIds.Length == 0
            ? new Dictionary<Guid, VendorSummary>()
            : await vendors.FindManyAsync(vendorIds, cancellationToken).ConfigureAwait(false);

        var availability = listingIds.Length == 0
            ? new Dictionary<Guid, StockAvailability>()
            : await stock.FindManyAsync(listingIds, cancellationToken).ConfigureAwait(false);

        var serviceable = await ServiceabilityAsync(sellers.Keys, context, cancellationToken).ConfigureAwait(false);

        var quote = await QuoteAsync(cart, active, listings, context, cancellationToken).ConfigureAwait(false);
        var quoted = quote?.Lines.ToDictionary(line => line.LineId) ?? new Dictionary<Guid, QuoteLine>();

        var lines = active
            .ConvertAll(line => Project(line, listings, sellers, availability, serviceable, quoted));

        var saved = setAside
            .ConvertAll(line => Project(line, listings, sellers, availability, serviceable, quoted));

        var issues = new List<CartIssue>();

        if (quote?.CouponRejection is { Length: > 0 } rejection)
        {
            // Not blocking. A basket with a bad coupon on it still has a price, and refusing to
            // check out over a code the shopper typed hopefully would be absurd.
            issues.Add(new CartIssue(CartIssueCodes.CouponRejected, rejection, IsBlocking: false));
        }

        // The third of the five gates ADR-018 names, and the one a shopper meets first. It is a
        // basket-level issue rather than a line-level one because it is not any seller's fault: the
        // whole address is outside where this store has decided to deliver, or outside what any
        // courier will carry to.
        if (!string.IsNullOrWhiteSpace(context.Pincode))
        {
            var destination = await deliveries
                .CheckDestinationAsync(context.Pincode, isCod: false, cancellationToken)
                .ConfigureAwait(false);

            if (!destination.Deliverable)
            {
                issues.Add(destination.Refusal == DeliveryRefusal.NotCovered
                    ? new CartIssue(
                        CartIssueCodes.NotCovered,
                        destination.Message ?? "We do not deliver to that area yet.",
                        IsBlocking: true)
                    : new CartIssue(
                        CartIssueCodes.NotServiceable,
                        "No courier currently delivers to that PIN code.",
                        IsBlocking: true));
            }
        }

        var ready = lines.Count > 0
                    && !issues.Any(issue => issue.IsBlocking)
                    && !lines.SelectMany(line => line.Issues).Any(issue => issue.IsBlocking);

        return new CartResponse(
            cart.Id,
            cart.CustomerId,
            cart.Status.ToString(),
            cart.CurrencyCode,
            cart.CouponCode,
            cart.LineCount,
            cart.ExpiresAt,
            lines,
            saved,
            Group(lines, sellers, quote, context),
            quote,
            issues,
            ready);
    }

    /// <summary>Prices the live lines, or returns null for a basket with nothing in it.</summary>
    private async Task<QuoteResult?> QuoteAsync(
        Cart cart,
        IReadOnlyList<CartLine> active,
        IReadOnlyDictionary<Guid, ListingSummary> listings,
        CartRenderContext context,
        CancellationToken cancellationToken)
    {
        // Only lines the catalogue still knows about are sent. An archived offer has no price, and
        // asking for one would drop the whole quote rather than the one line that is wrong.
        var priceable = active
            .Where(line => listings.ContainsKey(line.ListingId))
            .Select(line => new QuoteLineRequest(line.Id, line.ListingId, line.Quantity))
            .ToList();

        if (priceable.Count == 0)
        {
            return null;
        }

        var shipping = context.ShippingByVendor?.Values.Sum() ?? 0m;

        var request = new QuoteRequest(
            priceable,
            cart.CustomerId,
            context.PlaceOfSupplyStateId,
            cart.CouponCode,
            context.PaymentMethod,
            context.IsFirstOrder,
            shipping,
            context.WalletRedeemRequested);

        return await engine.QuoteAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Which sellers deliver to the destination, asked once per seller rather than once per line.
    /// </summary>
    /// <remarks>
    /// Skipped entirely until an address exists. A cart page has no destination, and answering
    /// "unserviceable" before the shopper has said where they live would be wrong on every basket.
    /// </remarks>
    private async Task<IReadOnlyDictionary<Guid, bool>> ServiceabilityAsync(
        IEnumerable<Guid> vendorIds,
        CartRenderContext context,
        CancellationToken cancellationToken)
    {
        if (context.PlaceOfSupplyStateId is not { } stateId || string.IsNullOrWhiteSpace(context.Pincode))
        {
            return new Dictionary<Guid, bool>();
        }

        var result = new Dictionary<Guid, bool>();

        foreach (var vendorId in vendorIds)
        {
            result[vendorId] = await vendors
                .IsServiceableAsync(vendorId, stateId, context.Pincode, cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>Projects one line, with everything wrong with it.</summary>
    private static CartLineResponse Project(
        CartLine line,
        IReadOnlyDictionary<Guid, ListingSummary> listings,
        IReadOnlyDictionary<Guid, VendorSummary> sellers,
        IReadOnlyDictionary<Guid, StockAvailability> availability,
        IReadOnlyDictionary<Guid, bool> serviceable,
        IReadOnlyDictionary<Guid, QuoteLine> quoted)
    {
        var issues = new List<CartIssue>();
        var listing = listings.GetValueOrDefault(line.ListingId);
        var seller = sellers.GetValueOrDefault(listing?.VendorId ?? line.VendorId);
        var stock = availability.GetValueOrDefault(line.ListingId);
        var quote = quoted.GetValueOrDefault(line.Id);

        if (listing is null || !listing.IsPurchasable)
        {
            issues.Add(new CartIssue(
                CartIssueCodes.ListingUnavailable,
                "This item is no longer available. Remove it to continue.",
                IsBlocking: true));
        }

        if (seller is null || !seller.IsActive)
        {
            issues.Add(new CartIssue(
                CartIssueCodes.VendorInactive,
                "The seller of this item is not currently trading. Remove it to continue.",
                IsBlocking: true));
        }

        if (listing is not null && stock is not null && !stock.CanFulfil(line.Quantity))
        {
            issues.Add(new CartIssue(
                CartIssueCodes.OutOfStock,
                stock.QuantityAvailable > 0
                    ? $"Only {stock.QuantityAvailable} left. Reduce the quantity to continue."
                    : "This item has sold out. Remove it to continue.",
                IsBlocking: true));
        }

        // Reported on a line that is otherwise fine, and never blocking: a price that moved is a
        // disclosure, and one that moved *down* would be an absurd thing to stop a sale over.
        if (quote is not null && quote.UnitPrice != line.UnitPriceAtAdd)
        {
            issues.Add(new CartIssue(
                CartIssueCodes.PriceChanged,
                quote.UnitPrice > line.UnitPriceAtAdd
                    ? "The price of this item has gone up since you added it."
                    : "Good news — the price of this item has come down since you added it.",
                IsBlocking: false));
        }

        if (seller is not null && serviceable.TryGetValue(seller.Id, out var delivers) && !delivers)
        {
            issues.Add(new CartIssue(
                CartIssueCodes.NotServiceable,
                $"{seller.DisplayName} does not deliver to the address you chose.",
                IsBlocking: true));
        }

        return new CartLineResponse(
            line.Id,
            line.ListingId,
            listing?.VendorId ?? line.VendorId,
            seller?.DisplayName ?? "Unavailable seller",
            listing?.Sku ?? string.Empty,
            listing?.Name ?? "Unavailable item",
            listing?.PrimaryImageFileId,
            line.Quantity,
            line.SavedForLater,
            quote?.UnitMrp ?? listing?.Mrp ?? 0m,
            quote?.UnitPrice ?? listing?.SellingPrice ?? line.UnitPriceAtAdd,
            line.UnitPriceAtAdd,
            quote?.LineTotal ?? 0m,
            stock?.QuantityAvailable ?? 0,
            listing?.IsCodAllowed ?? false,
            issues);
    }

    /// <summary>
    /// Groups the lines by seller, taking the money from the quote's own groups where there is one.
    /// </summary>
    /// <remarks>
    /// The quote already groups by seller, because that is the split Orders will make; re-deriving
    /// the figures here would be a second implementation of the same sum, and the first time the
    /// two rounded differently the cart and the invoice would disagree.
    /// </remarks>
    private static IReadOnlyList<CartVendorGroupResponse> Group(
        IReadOnlyList<CartLineResponse> lines,
        IReadOnlyDictionary<Guid, VendorSummary> sellers,
        QuoteResult? quote,
        CartRenderContext context)
    {
        var quoted = quote?.VendorGroups.ToDictionary(group => group.VendorId)
                     ?? new Dictionary<Guid, QuoteVendorGroup>();

        return
        [
            .. lines
                .GroupBy(line => line.VendorId)
                .Select(group =>
                {
                    var seller = sellers.GetValueOrDefault(group.Key);
                    var figures = quoted.GetValueOrDefault(group.Key);
                    var shipping = context.ShippingByVendor?.GetValueOrDefault(group.Key) ?? 0m;

                    return new CartVendorGroupResponse(
                        group.Key,
                        seller?.DisplayName ?? "Unavailable seller",
                        seller?.DispatchSlaHours ?? 0,
                        [.. group.Select(line => line.Id)],
                        figures?.Subtotal ?? group.Sum(line => line.LineTotal),
                        figures?.Discount ?? 0m,
                        figures?.TaxTotal ?? 0m,
                        figures?.Shipping ?? shipping,
                        (figures?.Total ?? group.Sum(line => line.LineTotal)) + (figures is null ? shipping : 0m));
                })
                .OrderBy(group => group.VendorName, StringComparer.Ordinal),
        ];
    }
}
