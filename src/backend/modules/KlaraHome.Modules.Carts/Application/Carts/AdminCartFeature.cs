using System.Globalization;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Carts.Application.Checkout;
using KlaraHome.Modules.Carts.Domain;
using KlaraHome.Modules.Carts.Infrastructure.Carts;
using KlaraHome.Modules.Carts.Infrastructure.Checkout;
using KlaraHome.Modules.Carts.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Carts.Application.Carts;

/// <summary>A basket in an operator's list.</summary>
/// <param name="Id">The basket.</param>
/// <param name="CustomerId">The shopper, or null for one nobody signed in to.</param>
/// <param name="Status">Where it is in its life.</param>
/// <param name="CurrencyCode">ISO 4217 code the figures are in.</param>
/// <param name="CouponCode">The code on it, if any.</param>
/// <param name="LineCount">How many lines are in it.</param>
/// <param name="EstimatedValue">
/// What it is worth at the prices the cart last saw. Deliberately not a quote: this is a list, and
/// running the promotion engine over every row of it would be the most expensive screen in the
/// admin.
/// </param>
/// <param name="LastActivityAt">When the shopper last touched it.</param>
/// <param name="AbandonedAt">When it was written off, if it was.</param>
/// <param name="ReminderCount">How many reminders have gone out about it.</param>
/// <param name="ConvertedOrderId">The order it became, if it did.</param>
internal sealed record AdminCartSummary(
    Guid Id,
    Guid? CustomerId,
    string Status,
    string CurrencyCode,
    string? CouponCode,
    int LineCount,
    decimal EstimatedValue,
    DateTimeOffset LastActivityAt,
    DateTimeOffset? AbandonedAt,
    int ReminderCount,
    Guid? ConvertedOrderId);

/// <summary>Lists baskets for support and merchandising.</summary>
/// <param name="Status">Restrict to one state.</param>
/// <param name="CustomerId">Restrict to one shopper.</param>
/// <param name="HasCoupon">Only baskets carrying a code.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListCartsQuery(
    string? Status,
    Guid? CustomerId,
    bool? HasCoupon,
    string? Cursor,
    int? Size) : IQuery<PagedResult<AdminCartSummary>>;

/// <summary>
/// The abandoned-cart worklist, most valuable first.
/// </summary>
/// <remarks>
/// Ordered by value rather than by time, because that is the question a campaign actually asks: not
/// "what was left most recently" but "which of these is worth chasing".
/// </remarks>
/// <param name="Since">Only baskets abandoned on or after this instant.</param>
/// <param name="MinValue">Only baskets worth at least this much.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListAbandonedCartsQuery(
    DateTimeOffset? Since,
    decimal? MinValue,
    string? Cursor,
    int? Size) : IQuery<PagedResult<AdminCartSummary>>;

/// <summary>Reads one basket in full, priced exactly as the shopper sees it.</summary>
/// <param name="CartId">The basket.</param>
internal sealed record GetAdminCartQuery(Guid CartId) : IQuery<CartResponse>;

/// <summary>Retires a basket an operator is certain of.</summary>
/// <param name="CartId">The basket.</param>
internal sealed record ExpireCartCommand(Guid CartId) : ICommand;

/// <summary>Reads one checkout session, its snapshots and its placements.</summary>
/// <param name="SessionId">The session.</param>
internal sealed record GetAdminCheckoutQuery(Guid SessionId) : IQuery<CheckoutResponse>;

/// <summary>Lists baskets.</summary>
/// <param name="context">The Cart data context.</param>
internal sealed class ListCartsQueryHandler(CartsDbContext context)
    : IQueryHandler<ListCartsQuery, PagedResult<AdminCartSummary>>
{
    public async Task<Result<PagedResult<AdminCartSummary>>> HandleAsync(
        ListCartsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);
        var rows = context.Carts.AsNoTracking().Include(cart => cart.Lines).AsQueryable();

        if (Enum.TryParse<CartStatus>(query.Status, ignoreCase: true, out var status))
        {
            rows = rows.Where(cart => cart.Status == status);
        }

        if (query.CustomerId is { } customerId)
        {
            rows = rows.Where(cart => cart.CustomerId == customerId);
        }

        if (query.HasCoupon == true)
        {
            rows = rows.Where(cart => cart.CouponCode != null);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(cart => cart.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(cart => cart.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        return Result.Success(new PagedResult<AdminCartSummary>(
            page.ConvertAll(CartProjection.ToSummary),
            new PageInfo(size, hasMore ? Cursor.Encode(page[^1].Id.ToString()) : null)));
    }
}

/// <summary>Lists abandoned baskets, most valuable first.</summary>
/// <param name="context">The Cart data context.</param>
internal sealed class ListAbandonedCartsQueryHandler(CartsDbContext context)
    : IQueryHandler<ListAbandonedCartsQuery, PagedResult<AdminCartSummary>>
{
    public async Task<Result<PagedResult<AdminCartSummary>>> HandleAsync(
        ListAbandonedCartsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);

        var rows = context.Carts
            .AsNoTracking()
            .Include(cart => cart.Lines)
            .Where(cart => cart.Status == CartStatus.Abandoned);

        if (query.Since is { } since)
        {
            rows = rows.Where(cart => cart.AbandonedAt >= since);
        }

        // Ranked in memory over one page-sized window rather than in SQL, because the value is the
        // sum of a child table and this list is a worklist rather than a report. The cursor is still
        // keyset over the abandonment time, so paging cannot repeat or skip a row.
        if (Cursor.TryDecode(query.Cursor, out var key)
            && DateTimeOffset.TryParse(key, CultureInfo.InvariantCulture, out var after))
        {
            rows = rows.Where(cart => cart.AbandonedAt < after);
        }

        var page = await rows
            .OrderByDescending(cart => cart.AbandonedAt)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        var summaries = page.ConvertAll(CartProjection.ToSummary);

        if (query.MinValue is { } minimum)
        {
            summaries = summaries.FindAll(summary => summary.EstimatedValue >= minimum);
        }

        summaries.Sort((left, right) => right.EstimatedValue.CompareTo(left.EstimatedValue));

        return Result.Success(new PagedResult<AdminCartSummary>(
            summaries,
            new PageInfo(
                size,
                hasMore && page.Count > 0
                    ? Cursor.Encode(page[^1].AbandonedAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty)
                    : null)));
    }
}

/// <summary>Reads one basket in full.</summary>
/// <param name="context">The Cart data context.</param>
/// <param name="renderer">Prices and validates it, exactly as the shopper sees it.</param>
internal sealed class GetAdminCartQueryHandler(CartsDbContext context, CartRenderer renderer)
    : IQueryHandler<GetAdminCartQuery, CartResponse>
{
    public async Task<Result<CartResponse>> HandleAsync(
        GetAdminCartQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var cart = await context.Carts
            .AsNoTracking()
            .Include(candidate => candidate.Lines)
            .FirstOrDefaultAsync(candidate => candidate.Id == query.CartId, cancellationToken)
            .ConfigureAwait(false);

        return cart is null
            ? CartsErrors.NotFound("basket")
            : Result.Success(
                await renderer.RenderAsync(cart, new CartRenderContext(), cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Retires a basket.</summary>
/// <param name="context">The Cart data context.</param>
/// <param name="audit">Records the change, because it is one an operator made to a shopper's data.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class ExpireCartCommandHandler(CartsDbContext context, IAuditLogger audit, IClock clock)
    : ICommandHandler<ExpireCartCommand>
{
    /// <summary>The audited action.</summary>
    public const string AuditAction = "carts.cart.expired";

    /// <summary>The entity type recorded against it.</summary>
    public const string AuditEntityType = "Cart";

    public async Task<Result> HandleAsync(ExpireCartCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var cart = await context.Carts
            .FirstOrDefaultAsync(candidate => candidate.Id == command.CartId, cancellationToken)
            .ConfigureAwait(false);

        if (cart is null)
        {
            return Result.Failure(CartsErrors.NotFound("basket"));
        }

        if (!cart.MarkExpired(clock.UtcNow))
        {
            return Result.Failure(CartsErrors.CartClosed);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AuditEntityType,
                EntityId = cart.Id.ToString(),
                After = new { cart.Status },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Reads one checkout session.</summary>
/// <param name="context">The Cart data context.</param>
/// <param name="renderer">Prices the basket behind it.</param>
internal sealed class GetAdminCheckoutQueryHandler(CartsDbContext context, CartRenderer renderer)
    : IQueryHandler<GetAdminCheckoutQuery, CheckoutResponse>
{
    public async Task<Result<CheckoutResponse>> HandleAsync(
        GetAdminCheckoutQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var session = await context.CheckoutSessions
            .AsNoTracking()
            .Include(candidate => candidate.Shipments)
            .Include(candidate => candidate.Placements)
            .FirstOrDefaultAsync(candidate => candidate.Id == query.SessionId, cancellationToken)
            .ConfigureAwait(false);

        if (session is null)
        {
            return CartsErrors.NotFound("checkout");
        }

        var cart = await context.Carts
            .AsNoTracking()
            .Include(candidate => candidate.Lines)
            .FirstOrDefaultAsync(candidate => candidate.Id == session.CartId, cancellationToken)
            .ConfigureAwait(false);

        if (cart is null)
        {
            return CartsErrors.NotFound("basket");
        }

        var priced = await renderer
            .RenderAsync(cart, CheckoutWorkflow.ContextFor(session), cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(CheckoutWorkflow.ToResponse(session, priced));
    }
}

/// <summary>Projections shared by this module's operator-facing lists.</summary>
internal static class CartProjection
{
    /// <summary>Summarises a basket for a list.</summary>
    /// <param name="cart">The basket, with its lines loaded.</param>
    public static AdminCartSummary ToSummary(Cart cart)
    {
        ArgumentNullException.ThrowIfNull(cart);

        return new AdminCartSummary(
            cart.Id,
            cart.CustomerId,
            cart.Status.ToString(),
            cart.CurrencyCode,
            cart.CouponCode,
            cart.LineCount,
            cart.Lines.Where(line => !line.SavedForLater).Sum(line => line.UnitPriceAtAdd * line.Quantity),
            cart.LastActivityAt,
            cart.AbandonedAt,
            cart.ReminderCount,
            cart.ConvertedOrderId);
    }
}
