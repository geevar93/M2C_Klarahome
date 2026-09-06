using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Payments;
using KlaraHome.Modules.Payments.Application;
using KlaraHome.Modules.Payments.Domain;
using KlaraHome.Modules.Payments.Infrastructure.Gateway;
using KlaraHome.Modules.Payments.Infrastructure.Gateway.Razorpay;
using KlaraHome.Modules.Payments.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Payments.Infrastructure.Initiation;

/// <summary>
/// Opens a collection against a placed order — the seam Orders declared at Step 14.
/// </summary>
/// <remarks>
/// <para>
/// This is what replaces the polite refusal the Ordering module registers with <c>TryAdd</c>. From
/// here a prepaid placement returns a real gateway order id and a publishable key, and the storefront
/// can open the checkout widget.
/// </para>
/// <para>
/// It is idempotent on the placement key, which is what makes a double-tapped <em>Pay</em> safe end
/// to end: Cart's unique index gives back one order, and this module's unique index on the same key
/// gives back one collection. A replayed placement returns the instruction it returned the first
/// time rather than opening a second gateway order against one sale.
/// </para>
/// <para>
/// It commits its own transaction, deliberately, before returning. The alternative — leaving the
/// caller to save — would let a placement return a gateway order id for a collection this platform
/// has no record of, and a shopper would then pay against something nothing here could match.
/// </para>
/// </remarks>
/// <param name="context">The Payments data context.</param>
/// <param name="providers">Finds the configured gateway.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class PaymentInitiationService(
    PaymentsDbContext context,
    PaymentProviderRegistry providers,
    IClock clock) : IPaymentInitiation
{
    /// <inheritdoc />
    public async Task<Result<PaymentInstruction>> InitiateAsync(
        PaymentInitiationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resolved = providers.Require(providers.Default?.Name);

        if (resolved.IsFailure)
        {
            return Result.Failure<PaymentInstruction>(resolved.Error);
        }

        var provider = resolved.Value;

        // The replayed-placement path. It is checked before anything is created and before the
        // gateway is called, so a retry costs one indexed read rather than a second gateway order.
        var existing = await context.Payments
            .FirstOrDefaultAsync(
                payment => payment.IdempotencyKey == request.IdempotencyKey,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing.ProviderOrderId is { Length: > 0 } providerOrderId
                ? Result.Success(Instruct(existing, providerOrderId, provider))
                : await OpenAtGatewayAsync(existing, provider, request, cancellationToken).ConfigureAwait(false);
        }

        var payment = Payment.Open(
            request.OrderId,
            request.OrderNumber,
            request.CustomerId,
            provider.Name,
            request.Amount,
            request.CurrencyCode,
            request.IdempotencyKey,
            clock.UtcNow);

        context.Payments.Add(payment);

        return await OpenAtGatewayAsync(payment, provider, request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens the collection at the gateway and commits it.
    /// </summary>
    /// <remarks>
    /// The row is saved <em>after</em> the gateway answers, so a gateway that refuses leaves nothing
    /// behind: the placement fails, the order stays awaiting payment, and the shopper is offered a
    /// retry that will open a fresh collection rather than finding a dead one.
    /// </remarks>
    private async Task<Result<PaymentInstruction>> OpenAtGatewayAsync(
        Payment payment,
        IPaymentProvider provider,
        PaymentInitiationRequest request,
        CancellationToken cancellationToken)
    {
        var intent = await provider
            .CreatePaymentIntentAsync(
                new PaymentIntentRequest(
                    payment.Id,
                    payment.Receipt,
                    payment.Amount,
                    payment.CurrencyCode,
                    // Nothing personal. Notes are stored by a third party and echoed into logs on
                    // both sides; these two are what a returning webhook needs to find its way home.
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [RazorpayNotes.PaymentId] = payment.Id.ToString(),
                        [RazorpayNotes.OrderNumber] = payment.OrderNumber,
                    }),
                cancellationToken)
            .ConfigureAwait(false);

        if (intent.IsFailure)
        {
            return Result.Failure<PaymentInstruction>(intent.Error);
        }

        payment.AttachProviderOrder(intent.Value.ProviderOrderId, intent.Value.ExpiresAt);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(new PaymentInstruction(
            provider.Name,
            intent.Value.ProviderOrderId,
            intent.Value.PublicKey,
            payment.Amount,
            payment.CurrencyCode));
    }

    private static PaymentInstruction Instruct(Payment payment, string providerOrderId, IPaymentProvider provider)
        => new(
            payment.Provider,
            providerOrderId,
            provider.PublicKey,
            payment.Amount,
            payment.CurrencyCode);
}
