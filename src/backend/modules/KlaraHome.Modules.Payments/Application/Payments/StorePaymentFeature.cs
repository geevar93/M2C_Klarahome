using FluentValidation;
using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Payments;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Payments.Domain;
using KlaraHome.Modules.Payments.Infrastructure;
using KlaraHome.Modules.Payments.Infrastructure.Gateway;
using KlaraHome.Modules.Payments.Infrastructure.Persistence;
using KlaraHome.Modules.Payments.Infrastructure.Processing;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Payments.Application.Payments;

/// <summary>Reads where the money for one of the caller's own orders stands.</summary>
/// <param name="OrderId">The order.</param>
internal sealed record GetMyPaymentQuery(Guid OrderId) : IQuery<MyPaymentResponse>;

/// <summary>Asks for a fresh payment instruction on an order that was not paid for.</summary>
/// <param name="OrderId">The order.</param>
/// <param name="IdempotencyKey">The caller's key, so a double tap opens one collection.</param>
internal sealed record RetryPaymentCommand(Guid OrderId, string IdempotencyKey)
    : ICommand<PaymentInstructionResponse>;

/// <summary>
/// Records the handshake the browser hands back when the checkout widget closes.
/// </summary>
/// <remarks>
/// A UX signal, and the command deliberately cannot confirm anything. It exists so the storefront
/// has somewhere to send the widget's result, so a signature that does not verify is noticed, and so
/// the attempt appears in the support record even when the webhook is slow.
/// </remarks>
/// <param name="OrderId">The order.</param>
/// <param name="ProviderOrderId">The gateway order id the widget was opened with.</param>
/// <param name="ProviderPaymentId">The payment id it handed back.</param>
/// <param name="Signature">The signature it handed back.</param>
internal sealed record VerifyCheckoutCommand(
    Guid OrderId,
    string ProviderOrderId,
    string ProviderPaymentId,
    string Signature) : ICommand<MyPaymentResponse>;

/// <summary>Validates the browser's handshake.</summary>
internal sealed class VerifyCheckoutValidator : AbstractValidator<VerifyCheckoutCommand>
{
    public VerifyCheckoutValidator()
    {
        RuleFor(command => command.ProviderOrderId).NotEmpty().MaximumLength(64);
        RuleFor(command => command.ProviderPaymentId).NotEmpty().MaximumLength(64);
        RuleFor(command => command.Signature).NotEmpty().MaximumLength(256);
    }
}

/// <summary>
/// Reads the shopper's own payment.
/// </summary>
/// <remarks>
/// The order is resolved through the ordering contract with the caller's own id, so an order
/// belonging to somebody else does not resolve and answers the same 404 an invented one does. The
/// collection is then found by that order id — which is why this handler never has to check
/// ownership itself.
/// </remarks>
/// <param name="context">The Payments data context.</param>
/// <param name="orders">Resolves the order, scoped to the caller.</param>
/// <param name="scope">The signed-in shopper.</param>
/// <param name="options">Supplies the retry window.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class GetMyPaymentQueryHandler(
    PaymentsDbContext context,
    IOrderPaymentSync orders,
    PaymentsScope scope,
    IOptions<PaymentsOptions> options,
    IClock clock) : IQueryHandler<GetMyPaymentQuery, MyPaymentResponse>
{
    public async Task<Result<MyPaymentResponse>> HandleAsync(
        GetMyPaymentQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var order = await orders
            .GetAsync(query.OrderId, scope.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        if (order.IsFailure)
        {
            return Result.Failure<MyPaymentResponse>(PaymentsErrors.NotFound("order"));
        }

        var payment = await context.Payments
            .AsNoTracking()
            .Where(candidate => candidate.OrderId == query.OrderId)
            .OrderByDescending(candidate => candidate.OpenedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (payment is null)
        {
            return Result.Failure<MyPaymentResponse>(PaymentsErrors.NotFound("payment"));
        }

        var canRetry = PaymentRetry.IsOpen(payment, order.Value, options.Value, clock.UtcNow);

        return Result.Success(PaymentProjection.ToMine(payment, canRetry));
    }
}

/// <summary>
/// Opens a fresh collection for an order whose payment failed or was never completed.
/// </summary>
/// <remarks>
/// <para>
/// It goes through <see cref="IPaymentInitiation"/> — the same seam a placement uses — rather than
/// building an instruction of its own, so a retry and a first attempt open a collection in exactly
/// the same way and are idempotent under exactly the same index.
/// </para>
/// <para>
/// The refusals are named rather than generic, because a shopper reading them is deciding what to do
/// next: an order that has in fact been paid says so, one whose window has closed points at support,
/// and a cash-on-delivery order says there is nothing to pay.
/// </para>
/// </remarks>
/// <param name="context">The Payments data context.</param>
/// <param name="orders">Resolves the order, scoped to the caller.</param>
/// <param name="initiation">Opens the collection.</param>
/// <param name="scope">The signed-in shopper.</param>
/// <param name="options">Supplies the retry window.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class RetryPaymentCommandHandler(
    PaymentsDbContext context,
    IOrderPaymentSync orders,
    IPaymentInitiation initiation,
    PaymentsScope scope,
    IOptions<PaymentsOptions> options,
    IClock clock) : ICommandHandler<RetryPaymentCommand, PaymentInstructionResponse>
{
    public async Task<Result<PaymentInstructionResponse>> HandleAsync(
        RetryPaymentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var read = await orders
            .GetAsync(command.OrderId, scope.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        if (read.IsFailure)
        {
            return Result.Failure<PaymentInstructionResponse>(PaymentsErrors.NotFound("order"));
        }

        var order = read.Value;

        if (order.IsPaid)
        {
            return Result.Failure<PaymentInstructionResponse>(PaymentsErrors.AlreadyCaptured);
        }

        if (!order.IsAwaitingPayment
            || string.Equals(order.PaymentMethod, "CashOnDelivery", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<PaymentInstructionResponse>(PaymentsErrors.NotPayable);
        }

        if (!PaymentRetry.IsWindowOpen(order, options.Value, clock.UtcNow))
        {
            return Result.Failure<PaymentInstructionResponse>(PaymentsErrors.RetryWindowClosed);
        }

        // An open collection already exists for this order — the shopper walked away from the widget
        // rather than failing at it. Send them back to that one; the partial unique index would
        // refuse a second anyway, and opening a second gateway order for one sale is how a
        // reconciliation report ends up with two payments against one receipt.
        var open = await context.Payments
            .AsNoTracking()
            .Where(payment => payment.OrderId == command.OrderId
                              && (payment.Status == PaymentStatus.Created
                                  || payment.Status == PaymentStatus.Authorized))
            .OrderByDescending(payment => payment.OpenedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var key = open?.IdempotencyKey ?? command.IdempotencyKey;

        var instruction = await initiation
            .InitiateAsync(
                new PaymentInitiationRequest(
                    order.OrderId,
                    order.OrderNumber,
                    order.CustomerId,
                    order.AmountPayable,
                    order.CurrencyCode,
                    order.CustomerName,
                    order.CustomerEmail,
                    order.CustomerMobile,
                    key),
                cancellationToken)
            .ConfigureAwait(false);

        if (instruction.IsFailure)
        {
            return Result.Failure<PaymentInstructionResponse>(instruction.Error);
        }

        return Result.Success(new PaymentInstructionResponse(
            order.OrderId,
            order.OrderNumber,
            instruction.Value.Provider,
            instruction.Value.ProviderOrderId,
            instruction.Value.PublicKey,
            instruction.Value.Amount,
            instruction.Value.CurrencyCode,
            order.CustomerName,
            order.CustomerEmail,
            order.CustomerMobile));
    }
}

/// <summary>
/// Records the browser's callback as an attempt, and answers with what is currently believed.
/// </summary>
/// <remarks>
/// <para>
/// It verifies the gateway's signature over the two identifiers and it stops there. It does not
/// re-fetch the payment, it does not confirm the order, and it deliberately does not wait for the
/// webhook: the browser has just been told the payment succeeded, and the honest answer to "did it"
/// is what this platform knows right now — which the storefront then polls until the webhook lands.
/// </para>
/// <para>
/// That is not caution for its own sake. A browser is an untrusted client on a network the shopper
/// controls; confirming an order on anything it says would make a forged callback into free goods.
/// The signature check is what makes the attempt <em>worth recording</em>, not what makes it
/// authoritative.
/// </para>
/// </remarks>
/// <param name="context">The Payments data context.</param>
/// <param name="orders">Resolves the order, scoped to the caller.</param>
/// <param name="providers">Verifies the handshake.</param>
/// <param name="scope">The signed-in shopper.</param>
/// <param name="options">Supplies the retry window.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class VerifyCheckoutCommandHandler(
    PaymentsDbContext context,
    IOrderPaymentSync orders,
    PaymentProviderRegistry providers,
    PaymentsScope scope,
    IOptions<PaymentsOptions> options,
    IClock clock) : ICommandHandler<VerifyCheckoutCommand, MyPaymentResponse>
{
    public async Task<Result<MyPaymentResponse>> HandleAsync(
        VerifyCheckoutCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var read = await orders
            .GetAsync(command.OrderId, scope.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        if (read.IsFailure)
        {
            return Result.Failure<MyPaymentResponse>(PaymentsErrors.NotFound("order"));
        }

        var payment = await context.Payments
            .Where(candidate => candidate.OrderId == command.OrderId)
            .OrderByDescending(candidate => candidate.OpenedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (payment is null)
        {
            return Result.Failure<MyPaymentResponse>(PaymentsErrors.NotFound("payment"));
        }

        var resolved = providers.Require(payment.Provider);

        if (resolved.IsFailure)
        {
            return Result.Failure<MyPaymentResponse>(resolved.Error);
        }

        var verified = resolved.Value.VerifyCheckoutSignature(
            command.ProviderOrderId,
            command.ProviderPaymentId,
            command.Signature);

        if (!verified)
        {
            return Result.Failure<MyPaymentResponse>(PaymentsErrors.InvalidCheckoutSignature);
        }

        payment.Record(PaymentAttempt.Record(
            payment.Id,
            command.ProviderPaymentId,
            "checkout-callback",
            payment.Method,
            payment.Amount,
            detail: null,
            errorCode: null,
            errorDescription: null,
            PaymentAttemptSource.Checkout,
            clock.UtcNow));

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var canRetry = PaymentRetry.IsOpen(payment, read.Value, options.Value, clock.UtcNow);

        return Result.Success(PaymentProjection.ToMine(payment, canRetry));
    }
}

/// <summary>Whether a shopper may still be sent back to the widget.</summary>
/// <remarks>
/// Two conditions, and both have to hold: the collection has to be in a state that can be paid, and
/// the order has to be inside the configured window. Written once because three handlers ask it, and
/// three copies of a window calculation is three places for it to be wrong by an hour.
/// </remarks>
internal static class PaymentRetry
{
    /// <summary>Whether this collection could still be paid.</summary>
    /// <param name="payment">The collection.</param>
    /// <param name="order">The order it is against.</param>
    /// <param name="options">Supplies the window.</param>
    /// <param name="now">The current instant.</param>
    public static bool IsOpen(Payment payment, OrderPaymentView order, PaymentsOptions options, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(payment);

        return PaymentLifecycle.IsRetryable(payment.Status)
               && order.IsAwaitingPayment
               && IsWindowOpen(order, options, now);
    }

    /// <summary>Whether the order is still inside the retry window.</summary>
    /// <param name="order">The order.</param>
    /// <param name="options">Supplies the window.</param>
    /// <param name="now">The current instant.</param>
    public static bool IsWindowOpen(OrderPaymentView order, PaymentsOptions options, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(options);

        return order.PlacedAt.AddHours(options.RetryWindowHours) > now;
    }
}
