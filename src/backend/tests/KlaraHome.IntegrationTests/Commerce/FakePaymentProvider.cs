using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KlaraHome.Modules.Payments.Application;
using KlaraHome.Modules.Payments.Domain;
using KlaraHome.Modules.Payments.Infrastructure.Gateway;
using KlaraHome.SharedKernel.Results;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Razorpay at the network boundary, and nothing below it.
/// </summary>
/// <remarks>
/// <para>
/// Registered under <see cref="PaymentProviders.Razorpay"/> because that is the name the module
/// stores on a payment row and looks the adapter back up by. A fake calling itself something else
/// would be a provider the reconciliation job could never find again.
/// </para>
/// <para>
/// It remembers the collections it opened and the payments made against them, so the recovery
/// paths — a lost webhook re-fetched, a settlement report pulled for a window — have something
/// truthful to find. Signature verification is a real HMAC over a shared secret rather than a stub
/// returning <c>true</c>: several of the rows this fake exists to close are about a webhook with a
/// <em>wrong</em> signature being refused, and a stub cannot prove that.
/// </para>
/// </remarks>
internal sealed class FakePaymentProvider : IPaymentProvider
{
    /// <summary>The secret webhook bodies are signed with in these tests.</summary>
    public const string WebhookSecret = "test-webhook-secret";

    private readonly ConcurrentDictionary<string, ProviderPayment> _payments = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<string>> _paymentsByOrder = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ProviderRefund> _refunds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Guid> _ourPaymentIdByOrder = new(StringComparer.Ordinal);
    /// <summary>
    /// Shared across every instance in the process, not per instance.
    /// </summary>
    /// <remarks>
    /// Every test gets its own <see cref="FakePaymentProvider"/>, but they all write into the one
    /// database the collection shares. An instance counter starting at zero would hand two different
    /// tests' collections the identical id <c>order_1</c>, and a webhook naming that id would then
    /// resolve to whichever row a query happened to return first — the two tests' money silently
    /// crossing over rather than either one failing loudly.
    /// </remarks>
    private static int _sequence;

    /// <inheritdoc />
    public string Name => PaymentProviders.Razorpay;

    /// <summary>Whether this deployment has credentials. Settable, so the 503 path stays provable.</summary>
    public bool IsConfigured { get; set; } = true;

    /// <inheritdoc />
    public string PublicKey => "rzp_test_fake";

    /// <summary>Settlement reports this gateway will hand back.</summary>
    public List<ProviderSettlement> Settlements { get; } = [];

    /// <summary>The collections opened, in order. What the intent tests assert against.</summary>
    public List<PaymentIntentRequest> Intents { get; } = [];

    /// <summary>How many times each refund idempotency key was sent, so a double send is visible.</summary>
    public ConcurrentDictionary<string, int> RefundAttempts { get; } = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<Result<PaymentIntent>> CreatePaymentIntentAsync(
        PaymentIntentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsConfigured)
        {
            return Task.FromResult(Result.Failure<PaymentIntent>(PaymentsErrors.ProviderUnavailable));
        }

        lock (Intents)
        {
            Intents.Add(request);
        }

        var orderId = $"order_{Next()}";
        _ourPaymentIdByOrder[orderId] = request.PaymentId;

        return Task.FromResult(Result.Success(new PaymentIntent(
            orderId,
            PublicKey,
            request.Amount,
            request.CurrencyCode,
            DateTimeOffset.UtcNow.AddMinutes(15))));
    }

    /// <summary>
    /// Records that the shopper paid a collection, as the gateway would have.
    /// </summary>
    /// <remarks>
    /// The one half of the gateway a test drives directly. Everything after it — the webhook, the
    /// re-fetch that confirms it, the reconciliation sweep that recovers it — goes through the
    /// module's own code.
    /// </remarks>
    /// <param name="providerOrderId">The collection the payment is against.</param>
    /// <param name="amount">What was paid.</param>
    /// <param name="captured">Whether the money was taken, or only held.</param>
    /// <param name="method">The rail it arrived on.</param>
    public ProviderPayment PayOrder(
        string providerOrderId,
        decimal amount,
        bool captured = true,
        PaymentMethod method = PaymentMethod.Upi)
        => Record(providerOrderId, new ProviderPayment(
            $"pay_{Next()}",
            captured ? "captured" : "authorized",
            method,
            amount,
            AmountRefunded: 0m,
            IsAuthorized: true,
            IsCaptured: captured,
            IsFailed: false,
            new PaymentMethodDetail { UpiHandle = "okhdfcbank" },
            ErrorCode: null,
            ErrorDescription: null,
            DateTimeOffset.UtcNow));

    /// <summary>Records that the gateway refused a collection.</summary>
    /// <param name="providerOrderId">The collection.</param>
    /// <param name="amount">What was attempted.</param>
    /// <param name="errorCode">The refusal code.</param>
    public ProviderPayment FailOrder(string providerOrderId, decimal amount, string errorCode = "BAD_REQUEST_ERROR")
        => Record(providerOrderId, new ProviderPayment(
            $"pay_{Next()}",
            "failed",
            PaymentMethod.Card,
            amount,
            AmountRefunded: 0m,
            IsAuthorized: false,
            IsCaptured: false,
            IsFailed: true,
            new PaymentMethodDetail { Issuer = "HDFC", Last4 = "4242", CardType = "credit" },
            errorCode,
            "The bank declined the payment.",
            DateTimeOffset.UtcNow));

    /// <summary>Our own collection id for a gateway order, as the notes we sent would carry it back.</summary>
    /// <param name="providerOrderId">The gateway order id.</param>
    public Guid? OurPaymentIdFor(string providerOrderId)
        => _ourPaymentIdByOrder.TryGetValue(providerOrderId, out var id) ? id : null;

    /// <inheritdoc />
    public Task<Result<ProviderPayment>> FetchPaymentAsync(
        string providerPaymentId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(_payments.TryGetValue(providerPaymentId, out var payment)
            ? Result.Success(payment)
            : Result.Failure<ProviderPayment>(PaymentsErrors.NotFound("payment")));

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<ProviderPayment>>> FetchPaymentsForOrderAsync(
        string providerOrderId,
        CancellationToken cancellationToken = default)
    {
        if (!_paymentsByOrder.TryGetValue(providerOrderId, out var ids))
        {
            return Task.FromResult(Result.Success<IReadOnlyList<ProviderPayment>>([]));
        }

        List<ProviderPayment> payments;

        lock (ids)
        {
            payments = [.. ids.Select(id => _payments[id])];
        }

        // Newest first, which is the order the recovery path documents and relies on.
        payments.Reverse();

        return Task.FromResult(Result.Success<IReadOnlyList<ProviderPayment>>(payments));
    }

    /// <inheritdoc />
    public Task<Result<ProviderPayment>> CapturePaymentAsync(
        string providerPaymentId,
        decimal amount,
        string currencyCode,
        CancellationToken cancellationToken = default)
    {
        if (!_payments.TryGetValue(providerPaymentId, out var payment))
        {
            return Task.FromResult(Result.Failure<ProviderPayment>(PaymentsErrors.NotFound("payment")));
        }

        var captured = payment with { Status = "captured", IsCaptured = true, Amount = amount };
        _payments[providerPaymentId] = captured;

        return Task.FromResult(Result.Success(captured));
    }

    /// <inheritdoc />
    public Task<Result<ProviderRefund>> RefundAsync(
        string providerPaymentId,
        decimal amount,
        string currencyCode,
        string idempotencyKey,
        RefundSpeed speed,
        CancellationToken cancellationToken = default)
    {
        RefundAttempts.AddOrUpdate(idempotencyKey, 1, (_, count) => count + 1);

        var refundId = KeyedId(idempotencyKey);

        // The gateway's own idempotency, modelled rather than assumed: the same key never moves
        // money twice and the first refund's identifier comes back. Without it, a test that retries
        // a refund would be proving this fake's forgetfulness rather than the module's key.
        if (_refunds.TryGetValue(refundId, out var already))
        {
            return Task.FromResult(Result.Success(already));
        }

        if (!_payments.TryGetValue(providerPaymentId, out var payment))
        {
            return Task.FromResult(Result.Failure<ProviderRefund>(PaymentsErrors.NotFound("payment")));
        }

        var refund = new ProviderRefund(
            refundId,
            providerPaymentId,
            "processed",
            amount,
            IsProcessed: true,
            IsFailed: false,
            Error: null,
            DateTimeOffset.UtcNow);

        _refunds[refundId] = refund;
        _payments[providerPaymentId] = payment with { AmountRefunded = payment.AmountRefunded + amount };

        return Task.FromResult(Result.Success(refund));
    }

    /// <inheritdoc />
    public Task<Result<ProviderRefund>> FetchRefundAsync(
        string providerRefundId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(_refunds.TryGetValue(providerRefundId, out var refund)
            ? Result.Success(refund)
            : Result.Failure<ProviderRefund>(PaymentsErrors.NotFound("refund")));

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<ProviderSettlement>>> FetchSettlementsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        lock (Settlements)
        {
            IReadOnlyList<ProviderSettlement> window =
            [
                .. Settlements.Where(settlement =>
                    settlement.SettledAt is null || (settlement.SettledAt >= from && settlement.SettledAt <= to)),
            ];

            return Task.FromResult(Result.Success(window));
        }
    }

    /// <inheritdoc />
    public bool VerifyWebhookSignature(string rawBody, string? signature)
        => signature is not null
           && CryptographicOperations.FixedTimeEquals(
               Encoding.UTF8.GetBytes(signature),
               Encoding.UTF8.GetBytes(Sign(rawBody)));

    /// <inheritdoc />
    public bool VerifyCheckoutSignature(string providerOrderId, string providerPaymentId, string? signature)
        => signature is not null
           && CryptographicOperations.FixedTimeEquals(
               Encoding.UTF8.GetBytes(signature),
               Encoding.UTF8.GetBytes(Sign($"{providerOrderId}|{providerPaymentId}")));

    /// <inheritdoc />
    public Result<WebhookEnvelope> ReadWebhook(string rawBody)
    {
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;

            return Result.Success(new WebhookEnvelope(
                root.GetProperty("id").GetString()!,
                root.GetProperty("event").GetString()!,
                root.TryGetProperty("created_at", out var created)
                    ? DateTimeOffset.FromUnixTimeSeconds(created.GetInt64())
                    : DateTimeOffset.UtcNow,
                Optional(root, "order_id"),
                Optional(root, "payment_id"),
                Optional(root, "refund_id"),
                Optional(root, "kh_payment_id") is { } ours ? Guid.Parse(ours, CultureInfo.InvariantCulture) : null,
                Optional(root, "transfer_id")));
        }
        catch (JsonException exception)
        {
            return Result.Failure<WebhookEnvelope>(PaymentsErrors.ProviderFailed(exception.Message));
        }
    }

    /// <summary>The signature this gateway would have put on a payload.</summary>
    /// <param name="payload">The bytes as they would be sent.</param>
    public static string Sign(string payload)
        => Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(WebhookSecret),
            Encoding.UTF8.GetBytes(payload)));

    private static string? Optional(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string KeyedId(string idempotencyKey) => $"rfnd_{idempotencyKey}";

    private ProviderPayment Record(string providerOrderId, ProviderPayment payment)
    {
        _payments[payment.ProviderPaymentId] = payment;

        _paymentsByOrder.AddOrUpdate(
            providerOrderId,
            _ => [payment.ProviderPaymentId],
            (_, existing) =>
            {
                lock (existing)
                {
                    existing.Add(payment.ProviderPaymentId);
                }

                return existing;
            });

        return payment;
    }

    private static int Next() => Interlocked.Increment(ref _sequence);
}
