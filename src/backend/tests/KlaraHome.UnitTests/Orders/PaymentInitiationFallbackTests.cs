using KlaraHome.Contracts.Payments;
using KlaraHome.Modules.Orders.Infrastructure.Payments;
using KlaraHome.SharedKernel.Results;

namespace KlaraHome.UnitTests.Orders;

/// <summary>
/// The refusal the Ordering module registers for <see cref="IPaymentInitiation"/> on a deployment
/// that has no Payments module.
/// </summary>
/// <remarks>
/// <para>
/// A unit test because the situation it describes is no longer reachable through the API: Step 15
/// registers a real initiator unconditionally, so the storefront now answers a prepaid placement
/// with a collection to pay. What remains worth pinning is that the fallback still refuses with the
/// documented code rather than throwing — a deployment that omits Payments has to fail in a way an
/// operator can read, and this is the class that decides how.
/// </para>
/// <para>
/// The half that <em>is</em> reachable — a refused initiation leaves no order behind, and gives the
/// coupon back — is proved through the API in
/// <c>OrderLifecycleTests.A_placement_that_fails_at_the_gateway_writes_no_order_and_gives_the_coupon_back</c>.
/// </para>
/// </remarks>
public sealed class PaymentInitiationFallbackTests
{
    [Fact]
    public async Task The_fallback_refuses_with_payments_unavailable_rather_than_throwing()
    {
        var initiation = new UnavailablePaymentInitiation();

        var refused = await initiation.InitiateAsync(
            new PaymentInitiationRequest(
                Guid.NewGuid(),
                "KH-2609-000184",
                Guid.NewGuid(),
                1998m,
                "INR",
                "Asha Rao",
                "asha@example.test",
                "+919876543210",
                Guid.NewGuid().ToString("N")),
            TestContext.Current.CancellationToken);

        Assert.True(refused.IsFailure);
        Assert.Equal("PAYMENTS_UNAVAILABLE", refused.Error.Code);
        Assert.Equal(ErrorType.Unavailable, refused.Error.Type);
    }
}
