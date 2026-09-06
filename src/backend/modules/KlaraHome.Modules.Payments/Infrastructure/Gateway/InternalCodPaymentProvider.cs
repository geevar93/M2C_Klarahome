using KlaraHome.Modules.Payments.Application;
using KlaraHome.Modules.Payments.Domain;
using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Modules.Payments.Infrastructure.Gateway;

/// <summary>
/// Cash on delivery, through the same interface as the gateway (docs/08-integrations.md §1).
/// </summary>
/// <remarks>
/// <para>
/// A provider with nothing behind it, on purpose. Cash is a payment method this platform supports
/// and therefore a payment method it must be able to <em>record</em>: the money is collected at a
/// door days after the order, it is remitted by a courier days after that, and both facts belong in
/// the payments schema alongside the gateway's, or reconciliation has two homes.
/// </para>
/// <para>
/// Everything that involves talking to somebody refuses, because there is nobody to talk to. There
/// is no intent to open — the shopper is not sent anywhere — and no payment to fetch or capture: the
/// facts arrive from the courier through the cash-collection surface instead. Refusing here is not a
/// gap; it is this provider saying what it is.
/// </para>
/// <para>
/// A refund against cash is likewise not the gateway's problem. Money that never went through a
/// gateway cannot come back through one, and Step 17 pays it out as a bank transfer or store credit.
/// </para>
/// </remarks>
internal sealed class InternalCodPaymentProvider : IPaymentProvider
{
    private static readonly Error NoGateway = Error.Conflict(
        "PAYMENT_COD_HAS_NO_GATEWAY",
        "Cash on delivery is collected at the door and has no gateway to talk to.");

    /// <inheritdoc />
    public string Name => PaymentProviders.InternalCod;

    /// <summary>Always. It needs no credentials, which is the one advantage cash has.</summary>
    public bool IsConfigured => true;

    /// <summary>Empty. There is no widget, so there is nothing for a browser to be given.</summary>
    public string PublicKey => string.Empty;

    /// <inheritdoc />
    public Task<Result<PaymentIntent>> CreatePaymentIntentAsync(
        PaymentIntentRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<PaymentIntent>(NoGateway));

    /// <inheritdoc />
    public Task<Result<ProviderPayment>> FetchPaymentAsync(
        string providerPaymentId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<ProviderPayment>(NoGateway));

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<ProviderPayment>>> FetchPaymentsForOrderAsync(
        string providerOrderId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Success<IReadOnlyList<ProviderPayment>>([]));

    /// <inheritdoc />
    public Task<Result<ProviderPayment>> CapturePaymentAsync(
        string providerPaymentId,
        decimal amount,
        string currencyCode,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<ProviderPayment>(NoGateway));

    /// <inheritdoc />
    public Task<Result<ProviderRefund>> RefundAsync(
        string providerPaymentId,
        decimal amount,
        string currencyCode,
        string idempotencyKey,
        RefundSpeed speed,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<ProviderRefund>(Error.Conflict(
            "REFUND_COD_NOT_AUTOMATIC",
            "Cash taken at the door is refunded by bank transfer or store credit, not through a gateway.")));

    /// <inheritdoc />
    public Task<Result<ProviderRefund>> FetchRefundAsync(
        string providerRefundId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<ProviderRefund>(NoGateway));

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<ProviderSettlement>>> FetchSettlementsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Success<IReadOnlyList<ProviderSettlement>>([]));

    /// <summary>Never. Nothing signed by anybody arrives about cash.</summary>
    public bool VerifyWebhookSignature(string rawBody, string? signature) => false;

    /// <summary>Never. There is no widget and therefore no handshake.</summary>
    public bool VerifyCheckoutSignature(string providerOrderId, string providerPaymentId, string? signature)
        => false;

    /// <inheritdoc />
    public Result<WebhookEnvelope> ReadWebhook(string rawBody)
        => Result.Failure<WebhookEnvelope>(NoGateway);
}

/// <summary>
/// Finds the adapter for a provider name.
/// </summary>
/// <remarks>
/// <para>
/// Every adapter is registered whether or not it is configured, and the registry answers honestly
/// about which of them can actually be used. That is what lets this module ship before its
/// credentials exist: an unconfigured deployment gets a named 503 at the point of collection rather
/// than a missing-service exception at start-up, and filling three values into configuration is the
/// whole of turning payments on.
/// </para>
/// <para>
/// <see cref="Default"/> is the provider named in configuration rather than whichever adapter
/// happens to be configured. A deployment with credentials for two gateways must still collect
/// through exactly one, and deciding that by registration order would make it depend on a DI detail.
/// </para>
/// </remarks>
/// <param name="providers">Every adapter registered in this host.</param>
/// <param name="options">Names the provider prepaid collections are opened with.</param>
internal sealed class PaymentProviderRegistry(
    IEnumerable<IPaymentProvider> providers,
    Microsoft.Extensions.Options.IOptionsMonitor<PaymentsOptions> options)
{
    private readonly IReadOnlyList<IPaymentProvider> _providers = [.. providers];

    /// <summary>The provider prepaid collections are opened with, or null when none is registered.</summary>
    public IPaymentProvider? Default => Find(options.CurrentValue.Provider);

    /// <summary>Cash, which is always available.</summary>
    public IPaymentProvider Cod => Find(PaymentProviders.InternalCod)!;

    /// <summary>The adapter for a stored provider name, or null when this build has none.</summary>
    /// <param name="name">The provider name as it is stored on a payment.</param>
    public IPaymentProvider? Find(string? name)
        => string.IsNullOrWhiteSpace(name)
            ? null
            : _providers.FirstOrDefault(provider =>
                string.Equals(provider.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The adapter for a payment, or a named failure explaining why there is none.</summary>
    /// <param name="providerName">The provider recorded on the payment.</param>
    public Result<IPaymentProvider> Require(string? providerName)
    {
        var provider = Find(providerName);

        if (provider is null)
        {
            return Result.Failure<IPaymentProvider>(PaymentsErrors.ProviderUnavailable);
        }

        return provider.IsConfigured
            ? Result.Success(provider)
            : Result.Failure<IPaymentProvider>(PaymentsErrors.ProviderUnavailable);
    }
}
