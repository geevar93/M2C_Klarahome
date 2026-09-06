using KlaraHome.Contracts.Orders;
using KlaraHome.Modules.Carts.Application;
using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Modules.Carts.Infrastructure.Checkout;

/// <summary>
/// What <see cref="IOrderPlacement"/> answers on a deployment that has no Ordering module.
/// </summary>
/// <remarks>
/// <para>
/// Between Step 13 and Step 14 the checkout is complete and there is nothing to hand the agreed
/// basket to. The honest way to hold that gap is a refusal that names the reason: a 503 with
/// <c>ORDERING_UNAVAILABLE</c> tells an operator exactly what is missing, where a
/// missing-service exception would tell them only that something threw.
/// </para>
/// <para>
/// Registered with <c>TryAdd</c>, so the Ordering module simply replaces it. It is deliberately not
/// a stub that invents an order number — an order that exists in a response and nowhere else is
/// worse than no order at all.
/// </para>
/// </remarks>
internal sealed class UnavailableOrderPlacement : IOrderPlacement
{
    /// <inheritdoc />
    public Task<Result<PlacedOrder>> PlaceAsync(
        PlaceOrderRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<PlacedOrder>(CartsErrors.OrderingUnavailable));
}
