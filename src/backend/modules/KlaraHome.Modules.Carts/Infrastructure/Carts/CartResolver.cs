using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Carts.Domain;
using KlaraHome.Modules.Carts.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Carts.Infrastructure.Carts;

/// <summary>
/// Finds the caller's basket, and opens one when they do not have one.
/// </summary>
/// <remarks>
/// <para>
/// Every storefront cart endpoint starts here, and none of them takes a cart id. The basket is
/// found from the access token or from the cookie, so a caller cannot name somebody else's — the
/// authorisation is in the shape of the lookup rather than in a check each of eight handlers has to
/// remember.
/// </para>
/// <para>
/// It is also where <b>merge on login</b> happens, and it happens without being asked. A shopper
/// who fills a basket, signs in and finds it empty does not file a bug, they leave; so a signed-in
/// caller who still presents a guest cookie has that basket folded into their own on the next read,
/// whether or not the storefront remembered to call <c>/store/cart/merge</c>.
/// </para>
/// </remarks>
/// <param name="context">The Cart data context.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="cookie">The anonymous basket's cookie.</param>
/// <param name="settings">Supplies the store currency and the per-line quantity ceiling.</param>
/// <param name="options">Lifetimes.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class CartResolver(
    CartsDbContext context,
    CartsScope scope,
    CartCookie cookie,
    IStoreSettings settings,
    IOptions<CartsOptions> options,
    IClock clock)
{
    /// <summary>How long an untouched basket lives.</summary>
    public TimeSpan Lifetime => TimeSpan.FromDays(options.Value.CartLifetimeDays);

    /// <summary>
    /// The caller's live basket, or null when they have none and <paramref name="createIfMissing"/>
    /// is false.
    /// </summary>
    /// <param name="createIfMissing">Whether to open one. A read does not; a write does.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Cart?> ResolveAsync(bool createIfMissing, CancellationToken cancellationToken)
    {
        var token = cookie.Read();

        return scope.CustomerId is { } customerId
            ? await ForCustomerAsync(customerId, token, createIfMissing, cancellationToken).ConfigureAwait(false)
            : await ForGuestAsync(token, createIfMissing, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The signed-in shopper's basket, having folded in anything the cookie still points at.</summary>
    private async Task<Cart?> ForCustomerAsync(
        Guid customerId,
        string? token,
        bool createIfMissing,
        CancellationToken cancellationToken)
    {
        var own = await LoadAsync(
                cart => cart.CustomerId == customerId && cart.Status == CartStatus.Active,
                cancellationToken)
            .ConfigureAwait(false);

        var guest = token is null
            ? null
            : await LoadAsync(
                    cart => cart.AnonymousTokenHash == CartCookie.HashOf(token) && cart.Status == CartStatus.Active,
                    cancellationToken)
                .ConfigureAwait(false);

        var now = clock.UtcNow;

        if (guest is not null)
        {
            var commerce = await settings.GetAsync<CommerceSettings>(cancellationToken).ConfigureAwait(false);

            if (own is null)
            {
                // Nothing of their own to merge into: the guest basket simply becomes theirs. This
                // is the common case — a first-time shopper who filled a basket and then registered.
                guest.AttachTo(customerId, now, Lifetime);
                cookie.Clear();
                return guest;
            }

            own.MergeFrom(guest, commerce.MaxQuantityPerLine, now, Lifetime);

            // The source is retired rather than deleted, so a shopper who says "my basket lost
            // something" can be answered from the record rather than from a guess.
            guest.MarkExpired(now);
            cookie.Clear();

            return own;
        }

        if (own is not null || !createIfMissing)
        {
            return own;
        }

        var currency = await CurrencyAsync(cancellationToken).ConfigureAwait(false);
        var opened = Cart.ForCustomer(customerId, currency, now, Lifetime);

        context.Carts.Add(opened);
        return opened;
    }

    /// <summary>The browser's basket, opening one and issuing a cookie when there is none.</summary>
    private async Task<Cart?> ForGuestAsync(
        string? token,
        bool createIfMissing,
        CancellationToken cancellationToken)
    {
        if (token is not null)
        {
            var existing = await LoadAsync(
                    cart => cart.AnonymousTokenHash == CartCookie.HashOf(token) && cart.Status == CartStatus.Active,
                    cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                return existing;
            }
        }

        if (!createIfMissing)
        {
            return null;
        }

        // A token that matches no row is one whose basket has expired or been merged. Issuing a
        // fresh one is right: reusing it would tie the new basket to a value that has already been
        // in a cookie jar longer than it should have been.
        var currency = await CurrencyAsync(cancellationToken).ConfigureAwait(false);
        var issued = cookie.Issue();
        var opened = Cart.ForGuest(CartCookie.HashOf(issued), currency, clock.UtcNow, Lifetime);

        context.Carts.Add(opened);
        return opened;
    }

    /// <summary>Loads a basket with its lines, tracked, because every caller of this is about to write.</summary>
    private Task<Cart?> LoadAsync(
        System.Linq.Expressions.Expression<Func<Cart, bool>> predicate,
        CancellationToken cancellationToken)
        => context.Carts
            .Include(cart => cart.Lines)
            .FirstOrDefaultAsync(predicate, cancellationToken);

    /// <summary>The currency the store trades in.</summary>
    private async Task<string> CurrencyAsync(CancellationToken cancellationToken)
        => (await settings.GetAsync<LocalizationSettings>(cancellationToken).ConfigureAwait(false)).CurrencyCode;
}
