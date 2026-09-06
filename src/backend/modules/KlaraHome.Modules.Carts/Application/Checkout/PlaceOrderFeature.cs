using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
using KlaraHome.Contracts.Inventory;
using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Pricing;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Carts.Domain;
using KlaraHome.Modules.Carts.Infrastructure;
using KlaraHome.Modules.Carts.Infrastructure.Checkout;
using KlaraHome.Modules.Carts.Infrastructure.Events;
using KlaraHome.Modules.Carts.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Carts.Application.Checkout;

/// <summary>
/// Turns an agreed checkout into an order, at most once per key.
/// </summary>
/// <param name="SessionId">The session.</param>
/// <param name="IdempotencyKey">
/// The caller's key, from the <c>Idempotency-Key</c> header. Required rather than optional: this
/// request reserves stock and creates an order, and a shopper double-tapping <em>Pay</em> on a
/// flaky connection must produce one order and two identical responses.
/// </param>
/// <param name="Channel">Where the order came from: <c>web</c>, <c>app</c>.</param>
internal sealed record PlaceOrderCommand(Guid SessionId, string IdempotencyKey, string Channel)
    : ICommand<PlaceOrderResponse>;

/// <summary>Rejects a placement that could never be attempted.</summary>
internal sealed class PlaceOrderValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderValidator()
    {
        RuleFor(command => command.SessionId).NotEmpty();
        RuleFor(command => command.IdempotencyKey).NotEmpty().MaximumLength(128);
        RuleFor(command => command.Channel).NotEmpty().MaximumLength(16);
    }
}

/// <summary>
/// The one write on this platform where a mistake costs a customer money.
/// </summary>
/// <remarks>
/// <para>
/// It runs in a fixed order, and the order is the design:
/// </para>
/// <list type="number">
/// <item>Claim the idempotency key. A repeat of a succeeded key replays the stored response and
/// touches nothing else; a repeat of one still running is refused; a repeat of one that failed is
/// allowed to try again, because an idempotency key promises <em>at most one</em> order and a
/// failure created none.</item>
/// <item>Re-validate and re-price. The shopper agreed to a total on the review screen, and between
/// then and now a promotion can have expired or an item sold out.</item>
/// <item>Hold the stock — every line or none. This is the platform's oversell boundary, and it is
/// crossed here rather than at add-to-cart, so browsing shoppers never hold stock away from buying
/// ones (docs/02-domain-model.md §4.2).</item>
/// <item>Hand the agreed basket to Orders. It creates the order from the snapshot rather than
/// re-pricing, which is what makes the confirmation the number the shopper agreed to.</item>
/// <item>Convert the basket and record the response, so the key can be replayed.</item>
/// </list>
/// <para>
/// Every failure after step 3 releases the holds before returning. Stock held for an order that was
/// never created is stock nobody can buy, and Inventory's sweeper would eventually free it — but
/// "eventually" is the wrong answer when the same shopper is about to press the button again.
/// </para>
/// </remarks>
/// <param name="workflow">The shared checkout steps.</param>
/// <param name="context">The Cart data context.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="stock">The oversell boundary.</param>
/// <param name="orders">The Ordering seam. Refuses politely until Step 14.</param>
/// <param name="events">Announces the conversion.</param>
/// <param name="options">Supplies the hold window.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class PlaceOrderCommandHandler(
    CheckoutWorkflow workflow,
    CartsDbContext context,
    CartsScope scope,
    IStockAvailability stock,
    IOrderPlacement orders,
    CartsEventPublisher events,
    IOptions<CartsOptions> options,
    IClock clock) : ICommandHandler<PlaceOrderCommand, PlaceOrderResponse>
{
    public async Task<Result<PlaceOrderResponse>> HandleAsync(
        PlaceOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await CheckoutLoader
            .LoadAsync(workflow, scope, command.SessionId, requireOpen: true, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (session, cart) = loaded.Value;

        if (session.ShippingAddress is not { } shipping || session.BillingAddress is not { } billing)
        {
            return CartsErrors.CheckoutIncomplete("a delivery address");
        }

        if (session.Shipments.Count == 0)
        {
            return CartsErrors.CheckoutIncomplete("a delivery option");
        }

        var priced = await workflow.RepriceAsync(session, cart, cancellationToken).ConfigureAwait(false);

        if (!priced.IsReadyForCheckout || priced.Quote is not { } quote)
        {
            return CartsErrors.CartNotReady;
        }

        var claim = await ClaimAsync(session, command.IdempotencyKey, quote, cancellationToken)
            .ConfigureAwait(false);

        if (claim.IsFailure)
        {
            return claim.Error;
        }

        // A replayed key that already succeeded: the stored response, verbatim, and nothing else
        // happens. Re-holding stock or re-calling Orders here is exactly the bug idempotency exists
        // to prevent.
        if (claim.Value.Replay is { } replay)
        {
            return Result.Success(replay);
        }

        var placement = claim.Value.Placement!;

        session.BeginPlacing();
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var held = await HoldStockAsync(cart, cancellationToken).ConfigureAwait(false);

        if (!held)
        {
            await ReleaseAsync(cart, cancellationToken).ConfigureAwait(false);
            return await FailAsync(session, placement, CartsErrors.OutOfStock, cancellationToken)
                .ConfigureAwait(false);
        }

        var request = new PlaceOrderRequest(
            session.Id,
            cart.Id,
            session.CustomerId,
            command.IdempotencyKey,
            quote,
            ToOrderAddress(shipping, session.Gstin),
            ToOrderAddress(billing, session.Gstin),
            [
                .. session.Shipments.Select(shipment => new OrderShipmentPlan(
                    shipment.VendorId,
                    shipment.OptionCode,
                    shipment.Carrier,
                    shipment.Amount,
                    shipment.DispatchSlaHours,
                    shipment.PromisedMinDays,
                    shipment.PromisedMaxDays)),
            ],
            session.PaymentMethod == CheckoutPaymentMethod.CashOnDelivery
                ? QuotePaymentMethod.CashOnDelivery
                : QuotePaymentMethod.Prepaid,
            cart.CouponCode,
            command.Channel);

        var placed = await orders.PlaceAsync(request, cancellationToken).ConfigureAwait(false);

        if (placed.IsFailure)
        {
            await ReleaseAsync(cart, cancellationToken).ConfigureAwait(false);
            return await FailAsync(session, placement, placed.Error, cancellationToken).ConfigureAwait(false);
        }

        var order = placed.Value;

        var response = new PlaceOrderResponse(
            order.OrderId,
            order.OrderNumber,
            order.Status,
            order.Payment is { } payment
                ? new PaymentResponse(
                    payment.Provider,
                    payment.ProviderOrderId,
                    payment.PublicKey,
                    payment.Amount,
                    payment.CurrencyCode)
                : null);

        var now = clock.UtcNow;

        session.MarkPlaced(order.OrderId, order.OrderNumber, now);
        cart.MarkConverted(order.OrderId, now);
        placement.Succeed(
            order.OrderId,
            order.OrderNumber,
            JsonSerializer.Serialize(response, PlacementJson.Options),
            now);

        events.Converted(cart, session.Id, order.OrderId, order.OrderNumber, quote.GrandTotal);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(response);
    }

    /// <summary>What claiming the key produced: a replay, or the row this attempt owns.</summary>
    private sealed record Claim(PlaceOrderResponse? Replay, CheckoutPlacement? Placement);

    /// <summary>
    /// Claims the idempotency key, or reports what the previous use of it did.
    /// </summary>
    /// <remarks>
    /// The unique index on <c>(tenant_id, idempotency_key)</c> is the enforcement, not this code:
    /// two requests racing with one key both reach the insert, one commits and one is rejected by
    /// the database, and the loser re-reads the winner's row. A check-then-insert without the index
    /// would be a race with a comfortable-looking shape.
    /// </remarks>
    private async Task<Result<Claim>> ClaimAsync(
        CheckoutSession session,
        string key,
        QuoteResult quote,
        CancellationToken cancellationToken)
    {
        var hash = HashRequest(session, quote);

        var existing = await context.CheckoutPlacements
            .FirstOrDefaultAsync(placement => placement.IdempotencyKey == key, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return Interpret(existing, hash);
        }

        var claimed = session.BeginPlacement(key, hash, clock.UtcNow);

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success(new Claim(null, claimed));
        }
        catch (DbUpdateException)
        {
            // Lost the race. The winner's row is the authority; this request reports what it did
            // rather than doing it again.
            context.Entry(claimed).State = EntityState.Detached;

            // Tracked, not AsNoTracking: a losing request whose winner had failed restarts
            // that row, and a detached entity would report a restart that was never written.
            var winner = await context.CheckoutPlacements
                .FirstOrDefaultAsync(placement => placement.IdempotencyKey == key, cancellationToken)
                .ConfigureAwait(false);

            return winner is null
                ? CartsErrors.PlacementInProgress
                : Interpret(winner, hash);
        }
    }

    /// <summary>Decides what a previous use of the key means for this request.</summary>
    private Result<Claim> Interpret(CheckoutPlacement existing, string hash)
    {
        // A key replayed against a different basket is a client bug, and answering it with somebody
        // else's order would be far worse than answering it with a conflict.
        if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
        {
            return CartsErrors.IdempotencyKeyReused;
        }

        if (existing.Status == PlacementStatus.Succeeded)
        {
            var stored = existing.Response is { Length: > 0 } json
                ? JsonSerializer.Deserialize<PlaceOrderResponse>(json, PlacementJson.Options)
                : null;

            // A succeeded row that cannot be read back is a row that must not be re-run: the order
            // exists. Reporting it as still in progress is the safe answer, and it is a support
            // question rather than a second charge.
            return stored is null
                ? CartsErrors.PlacementInProgress
                : Result.Success(new Claim(stored, null));
        }

        if (existing.Status == PlacementStatus.InProgress)
        {
            return CartsErrors.PlacementInProgress;
        }

        // A previous attempt failed, so no order was created and the key may be used again — which
        // matters, because a client that already has a key will send that one when the shopper
        // presses Pay a second time.
        existing.Restart(clock.UtcNow);

        return Result.Success(new Claim(null, existing));
    }

    /// <summary>
    /// Holds every live line, or none.
    /// </summary>
    /// <remarks>
    /// Each hold is idempotent on <c>(reference type, reference, line)</c>, so a retry of this same
    /// attempt finds its own holds rather than taking the stock twice. The reference is the
    /// <em>cart</em>, which is what Orders settles against once the order is confirmed or cancelled.
    /// </remarks>
    private async Task<bool> HoldStockAsync(Cart cart, CancellationToken cancellationToken)
    {
        var expiresAt = clock.UtcNow.AddMinutes(options.Value.PlacementHoldMinutes);

        foreach (var line in cart.Lines.Where(line => !line.SavedForLater))
        {
            var reservation = await stock.HoldAsync(
                line.ListingId,
                line.Quantity,
                ReservationReferenceTypes.Cart,
                cart.Id,
                line.Id,
                expiresAt,
                cancellationToken).ConfigureAwait(false);

            if (reservation is null)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Puts every hold this cart took back on sale.</summary>
    private async Task ReleaseAsync(Cart cart, CancellationToken cancellationToken)
        => await stock.SettleAsync(
                ReservationReferenceTypes.Cart,
                cart.Id,
                ReservationOutcome.Released,
                cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Records a failed attempt and hands the session back to the shopper.</summary>
    private async Task<Result<PlaceOrderResponse>> FailAsync(
        CheckoutSession session,
        CheckoutPlacement placement,
        Error error,
        CancellationToken cancellationToken)
    {
        placement.Fail(error.Code, clock.UtcNow);

        // Back to PaymentSet rather than Draft: a gateway that declined a card has not undone the
        // address the shopper typed, and making them retype it is the surest way to lose the sale
        // twice.
        session.AbandonPlacing();

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Failure<PlaceOrderResponse>(error);
    }

    /// <summary>Projects a stored snapshot into the shape Orders freezes onto the order.</summary>
    private static OrderAddress ToOrderAddress(AddressSnapshot address, string? sessionGstin)
        => new(
            address.RecipientName,
            address.Mobile,
            address.Line1,
            address.Line2,
            address.Landmark,
            address.City,
            address.StateId,
            address.Pincode,
            sessionGstin ?? address.Gstin);

    /// <summary>
    /// A digest of what the key was used against.
    /// </summary>
    /// <remarks>
    /// It covers the basket, the destination, the delivery choices, the payment method and the
    /// total — everything a shopper could have changed between two presses of the same button. It
    /// deliberately does not cover the quote's every field: a promotion that expired a second ago
    /// changes the total, and the total is in here, which is the part that matters.
    /// </remarks>
    private static string HashRequest(CheckoutSession session, QuoteResult quote)
    {
        var builder = new StringBuilder()
            .Append(session.CartId.ToString("N", CultureInfo.InvariantCulture))
            .Append('|')
            .Append(session.PlaceOfSupplyStateId?.ToString("N", CultureInfo.InvariantCulture) ?? "-")
            .Append('|')
            .Append(session.ShippingAddress?.SourceAddressId.ToString("N", CultureInfo.InvariantCulture) ?? "-")
            .Append('|')
            .Append(session.PaymentMethod)
            .Append('|')
            .Append(quote.GrandTotal.ToString("0.0000", CultureInfo.InvariantCulture));

        foreach (var line in quote.Lines.OrderBy(line => line.LineId))
        {
            builder
                .Append('|')
                .Append(line.ListingId.ToString("N", CultureInfo.InvariantCulture))
                .Append(':')
                .Append(line.Quantity.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var shipment in session.Shipments.OrderBy(shipment => shipment.VendorId))
        {
            builder
                .Append('|')
                .Append(shipment.VendorId.ToString("N", CultureInfo.InvariantCulture))
                .Append(':')
                .Append(shipment.OptionCode);
        }

        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}

/// <summary>How a replayed place-order response is stored and read back.</summary>
/// <remarks>
/// Its own options rather than the schema's, because this document is a <em>response body</em>
/// rather than a stored record: it is written once and handed back verbatim, so the casing has to
/// match what the API produced the first time.
/// </remarks>
internal static class PlacementJson
{
    /// <summary>camelCase, matching the API response it replays.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };
}
