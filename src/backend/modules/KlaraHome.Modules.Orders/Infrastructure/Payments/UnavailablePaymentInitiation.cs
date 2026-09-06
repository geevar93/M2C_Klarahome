using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Payments;
using KlaraHome.Modules.Orders.Application;
using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Modules.Orders.Infrastructure.Payments;

/// <summary>
/// What <see cref="IPaymentInitiation"/> answers on a deployment that has no Payments module.
/// </summary>
/// <remarks>
/// <para>
/// Between Step 14 and Step 15 an order can be created and there is nothing to collect the money
/// with. The honest way to hold that gap is a refusal that names the reason: a 503 with
/// <c>PAYMENTS_UNAVAILABLE</c> tells an operator exactly what is missing, where a missing-service
/// exception would tell them only that something threw.
/// </para>
/// <para>
/// It refuses <em>before</em> the order is committed, so no order is left in the database with no
/// way to pay for it. Cash on delivery never reaches here and works end to end today.
/// </para>
/// <para>
/// Registered with <c>TryAdd</c>, so the Payments module simply replaces it. It is deliberately not
/// a stub that invents a gateway reference — a payment that exists in a response and nowhere else is
/// worse than no payment at all.
/// </para>
/// </remarks>
internal sealed class UnavailablePaymentInitiation : IPaymentInitiation
{
    /// <inheritdoc />
    public Task<Result<PaymentInstruction>> InitiateAsync(
        PaymentInitiationRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<PaymentInstruction>(OrdersErrors.PaymentsUnavailable));
}
