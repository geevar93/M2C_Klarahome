using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KlaraHome.Modules.Payments.Application;
using KlaraHome.Modules.Payments.Domain;
using KlaraHome.SharedKernel.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Payments.Infrastructure.Gateway.Razorpay;

/// <summary>
/// Razorpay, behind this platform's own interface (docs/08-integrations.md §1).
/// </summary>
/// <remarks>
/// <para>
/// Hosted checkout only. This adapter opens an order, reads payments back, captures, refunds and
/// pulls settlement reports — and it never sees a card number, because the card is collected by
/// Razorpay's own widget in the shopper's browser. That is the whole of ADR-008 and the whole of why
/// this merchant stays in PCI-DSS SAQ-A scope.
/// </para>
/// <para>
/// Nothing here throws at a caller. A gateway that is unreachable, slow or angry is an ordinary
/// outcome on this path: it becomes a failed <see cref="Result"/>, the order stays awaiting payment,
/// the shopper is offered a retry, and the reconciliation sweep will ask again in fifteen minutes.
/// An exception escaping into a checkout request would turn a recoverable gateway blip into a 500 on
/// the one screen where a customer is deciding whether to trust this shop.
/// </para>
/// <para>
/// Every amount crossing this boundary goes through <see cref="RazorpaySignature.ToMinorUnits"/> and
/// its inverse. Razorpay counts in integer paise, this platform counts in rupees, and every
/// hand-rolled multiplication by a hundred is a place money can be lost by a factor of a hundred.
/// </para>
/// </remarks>
/// <param name="options">The credentials and endpoint. Blank until an operator fills them in.</param>
/// <param name="paymentsOptions">Supplies the auto-capture switch.</param>
/// <param name="clients">Supplies the named, allow-listed outbound client.</param>
/// <param name="logger">Reports what the gateway said when it refused.</param>
internal sealed partial class RazorpayPaymentProvider(
    IOptionsMonitor<RazorpayOptions> options,
    IOptionsMonitor<PaymentsOptions> paymentsOptions,
    IHttpClientFactory clients,
    ILogger<RazorpayPaymentProvider> logger) : IPaymentProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public string Name => PaymentProviders.Razorpay;

    /// <inheritdoc />
    public bool IsConfigured => options.CurrentValue.IsConfigured;

    /// <inheritdoc />
    public string PublicKey => options.CurrentValue.KeyId;

    /// <inheritdoc />
    public async Task<Result<PaymentIntent>> CreatePaymentIntentAsync(
        PaymentIntentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = options.CurrentValue;

        if (!settings.IsConfigured)
        {
            return Result.Failure<PaymentIntent>(PaymentsErrors.ProviderUnavailable);
        }

        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["amount"] = RazorpaySignature.ToMinorUnits(request.Amount),
            ["currency"] = request.CurrencyCode,
            ["receipt"] = request.Receipt,
            ["payment_capture"] = paymentsOptions.CurrentValue.AutoCapture,
            ["notes"] = request.Notes,
        };

        var result = await PostAsync<RazorpayOrder>("orders", body, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result.Failure<PaymentIntent>(result.Error);
        }

        var order = result.Value;

        return string.IsNullOrWhiteSpace(order.Id)
            ? Result.Failure<PaymentIntent>(PaymentsErrors.ProviderFailed("The gateway did not return an order."))
            : Result.Success(new PaymentIntent(
                order.Id,
                settings.KeyId,
                RazorpaySignature.FromMinorUnits(order.Amount),
                string.IsNullOrWhiteSpace(order.Currency) ? request.CurrencyCode : order.Currency,
                ExpiresAt: null));
    }

    /// <inheritdoc />
    public async Task<Result<ProviderPayment>> FetchPaymentAsync(
        string providerPaymentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerPaymentId))
        {
            return Result.Failure<ProviderPayment>(PaymentsErrors.NotFound("payment"));
        }

        var result = await GetAsync<RazorpayPayment>(
                $"payments/{Uri.EscapeDataString(providerPaymentId)}",
                cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure
            ? Result.Failure<ProviderPayment>(result.Error)
            : Result.Success(Map(result.Value));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ProviderPayment>>> FetchPaymentsForOrderAsync(
        string providerOrderId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerOrderId))
        {
            return Result.Success<IReadOnlyList<ProviderPayment>>([]);
        }

        var result = await GetAsync<RazorpayList<RazorpayPayment>>(
                $"orders/{Uri.EscapeDataString(providerOrderId)}/payments",
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ProviderPayment>>(result.Error);
        }

        // Newest first, so a caller taking the first captured one takes the live payment rather than
        // an earlier failed attempt against the same gateway order.
        IReadOnlyList<ProviderPayment> payments =
        [
            .. (result.Value.Items ?? [])
                .Select(Map)
                .OrderByDescending(payment => payment.OccurredAt ?? DateTimeOffset.MinValue),
        ];

        return Result.Success(payments);
    }

    /// <inheritdoc />
    public async Task<Result<ProviderPayment>> CapturePaymentAsync(
        string providerPaymentId,
        decimal amount,
        string currencyCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerPaymentId))
        {
            return Result.Failure<ProviderPayment>(PaymentsErrors.NotFound("payment"));
        }

        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["amount"] = RazorpaySignature.ToMinorUnits(amount),
            ["currency"] = currencyCode,
        };

        var result = await PostAsync<RazorpayPayment>(
                $"payments/{Uri.EscapeDataString(providerPaymentId)}/capture",
                body,
                cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure
            ? Result.Failure<ProviderPayment>(result.Error)
            : Result.Success(Map(result.Value));
    }

    /// <inheritdoc />
    public async Task<Result<ProviderRefund>> RefundAsync(
        string providerPaymentId,
        decimal amount,
        string currencyCode,
        string idempotencyKey,
        RefundSpeed speed,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerPaymentId))
        {
            return Result.Failure<ProviderRefund>(PaymentsErrors.NotFound("payment"));
        }

        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["amount"] = RazorpaySignature.ToMinorUnits(amount),
            ["speed"] = speed == RefundSpeed.Optimum ? "optimum" : "normal",
            // Echoed back on the refund and on every event about it, which is how a redelivered
            // refund webhook is matched to the row that asked for it.
            ["notes"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["refund_key"] = idempotencyKey },
        };

        // Razorpay deduplicates on this header, so a send that timed out on our side and succeeded
        // on theirs does not refund the shopper twice when the job retries it.
        var result = await PostAsync<RazorpayRefund>(
                $"payments/{Uri.EscapeDataString(providerPaymentId)}/refund",
                body,
                cancellationToken,
                idempotencyKey)
            .ConfigureAwait(false);

        return result.IsFailure
            ? Result.Failure<ProviderRefund>(result.Error)
            : Result.Success(Map(result.Value));
    }

    /// <inheritdoc />
    public async Task<Result<ProviderRefund>> FetchRefundAsync(
        string providerRefundId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerRefundId))
        {
            return Result.Failure<ProviderRefund>(PaymentsErrors.NotFound("refund"));
        }

        var result = await GetAsync<RazorpayRefund>(
                $"refunds/{Uri.EscapeDataString(providerRefundId)}",
                cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure
            ? Result.Failure<ProviderRefund>(result.Error)
            : Result.Success(Map(result.Value));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ProviderSettlement>>> FetchSettlementsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var listed = await GetAsync<RazorpayList<RazorpaySettlement>>(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"settlements?from={from.ToUnixTimeSeconds()}&to={to.ToUnixTimeSeconds()}&count=100"),
                cancellationToken)
            .ConfigureAwait(false);

        if (listed.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ProviderSettlement>>(listed.Error);
        }

        var settlements = new List<ProviderSettlement>();

        foreach (var settlement in listed.Value.Items ?? [])
        {
            if (string.IsNullOrWhiteSpace(settlement.Id))
            {
                continue;
            }

            // The report and its lines are two calls at this gateway. A report whose lines cannot be
            // read is still imported, with no entries: the header is evidence that a settlement
            // happened, and the reconciliation counts will show it as unmatched rather than clean.
            var recon = await GetAsync<RazorpayList<RazorpaySettlementEntry>>(
                    $"settlements/recon/combined?settlement_id={Uri.EscapeDataString(settlement.Id)}&count=1000",
                    cancellationToken)
                .ConfigureAwait(false);

            if (recon.IsFailure)
            {
                ReconUnavailable(logger, settlement.Id, recon.Error.Message);
            }

            IReadOnlyList<ProviderSettlementEntry> entries = recon.IsFailure
                ? []
                : [.. (recon.Value.Items ?? []).Select(Map)];

            settlements.Add(new ProviderSettlement(
                settlement.Id,
                RazorpaySignature.FromMinorUnits(settlement.Amount),
                RazorpaySignature.FromMinorUnits(settlement.Fees),
                RazorpaySignature.FromMinorUnits(settlement.Tax),
                "INR",
                settlement.Utr,
                settlement.Status,
                RazorpaySignature.FromUnixSeconds(settlement.CreatedAt),
                JsonSerializer.Serialize(settlement, Json),
                entries));
        }

        return Result.Success<IReadOnlyList<ProviderSettlement>>(settlements);
    }

    /// <inheritdoc />
    public bool VerifyWebhookSignature(string rawBody, string? signature)
    {
        var secret = options.CurrentValue.WebhookSecret;

        // An unconfigured webhook secret verifies nothing, ever. Returning true "because there is no
        // secret to check against" would make an unconfigured deployment the most trusting one.
        return !string.IsNullOrWhiteSpace(secret) && RazorpaySignature.Verify(rawBody, signature, secret);
    }

    /// <inheritdoc />
    public bool VerifyCheckoutSignature(string providerOrderId, string providerPaymentId, string? signature)
    {
        var secret = options.CurrentValue.KeySecret;

        if (string.IsNullOrWhiteSpace(secret)
            || string.IsNullOrWhiteSpace(providerOrderId)
            || string.IsNullOrWhiteSpace(providerPaymentId))
        {
            return false;
        }

        // The gateway signs the two identifiers joined by a pipe, under the API secret rather than
        // the webhook secret. Getting either of those wrong fails closed.
        return RazorpaySignature.Verify($"{providerOrderId}|{providerPaymentId}", signature, secret);
    }

    /// <inheritdoc />
    public Result<WebhookEnvelope> ReadWebhook(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return Result.Failure<WebhookEnvelope>(
                PaymentsErrors.ProviderFailed("The webhook body was empty."));
        }

        RazorpayWebhook? envelope;

        try
        {
            envelope = JsonSerializer.Deserialize<RazorpayWebhook>(rawBody, Json);
        }
        catch (JsonException exception)
        {
            return Result.Failure<WebhookEnvelope>(
                PaymentsErrors.ProviderFailed($"The webhook body could not be read: {exception.Message}"));
        }

        if (envelope is null || string.IsNullOrWhiteSpace(envelope.Event))
        {
            return Result.Failure<WebhookEnvelope>(
                PaymentsErrors.ProviderFailed("The webhook body named no event."));
        }

        var payment = envelope.Payload?.Payment?.Entity;
        var refund = envelope.Payload?.Refund?.Entity;
        var order = envelope.Payload?.Order?.Entity;

        // Razorpay's own event id header is optional on some plans, so the fallback is a
        // deterministic key built from what the event is about. Two deliveries of the same fact then
        // still collide on the unique index, which is what replay protection has to guarantee.
        var eventId = !string.IsNullOrWhiteSpace(envelope.Id)
            ? envelope.Id
            : $"{envelope.Event}:{refund?.Id ?? payment?.Id ?? order?.Id ?? "unknown"}:{envelope.CreatedAt}";

        return Result.Success(new WebhookEnvelope(
            eventId,
            envelope.Event,
            RazorpaySignature.FromUnixSeconds(envelope.CreatedAt),
            order?.Id ?? payment?.OrderId ?? refund?.OrderId,
            payment?.Id ?? refund?.PaymentId,
            refund?.Id,
            ReadPaymentId(payment?.Notes)));
    }

    /// <summary>Reads back the collection id this platform sent in the order's notes.</summary>
    private static Guid? ReadPaymentId(IReadOnlyDictionary<string, string>? notes)
        => notes is not null
           && notes.TryGetValue(RazorpayNotes.PaymentId, out var value)
           && Guid.TryParse(value, out var parsed)
            ? parsed
            : null;

    private async Task<Result<TResponse>> GetAsync<TResponse>(string path, CancellationToken cancellationToken)
        => await SendAsync<TResponse>(HttpMethod.Get, path, body: null, cancellationToken, idempotencyKey: null)
            .ConfigureAwait(false);

    private async Task<Result<TResponse>> PostAsync<TResponse>(
        string path,
        object body,
        CancellationToken cancellationToken,
        string? idempotencyKey = null)
        => await SendAsync<TResponse>(HttpMethod.Post, path, body, cancellationToken, idempotencyKey)
            .ConfigureAwait(false);

    /// <summary>
    /// One call to the gateway, and the single place a gateway failure becomes an ordinary result.
    /// </summary>
    /// <remarks>
    /// The error body is read and its description surfaced, because "your card was declined by the
    /// issuing bank" is worth showing a shopper and "the request failed" is not. What is never
    /// surfaced, and never logged, is the request: it carries the merchant credential.
    /// </remarks>
    private async Task<Result<TResponse>> SendAsync<TResponse>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken,
        string? idempotencyKey)
    {
        var settings = options.CurrentValue;

        if (!settings.IsConfigured)
        {
            return Result.Failure<TResponse>(PaymentsErrors.ProviderUnavailable);
        }

        try
        {
            var client = clients.CreateClient(RazorpayHttp.ClientName);
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);

            using var request = new HttpRequestMessage(method, new Uri(new Uri(settings.BaseUrl), path));

            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                RazorpaySignature.BasicCredential(settings.KeyId, settings.KeySecret));

            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                request.Headers.TryAddWithoutValidation("X-Razorpay-Idempotency-Key", idempotencyKey);
            }

            if (body is not null)
            {
                request.Content = new StringContent(
                    JsonSerializer.Serialize(body, Json),
                    Encoding.UTF8,
                    "application/json");
            }

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var failure = await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false);
                GatewayRefused(logger, path, (int)response.StatusCode, failure);

                return Result.Failure<TResponse>(PaymentsErrors.ProviderFailed(failure));
            }

            var value = await response.Content
                .ReadFromJsonAsync<TResponse>(Json, cancellationToken)
                .ConfigureAwait(false);

            return value is null
                ? Result.Failure<TResponse>(PaymentsErrors.ProviderFailed("The gateway returned an empty response."))
                : Result.Success(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller went away. Not a gateway fault, and not something to log as one.
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            GatewayUnreachable(logger, path, exception);
            return Result.Failure<TResponse>(PaymentsErrors.ProviderFailed());
        }
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content
                .ReadFromJsonAsync<RazorpayErrorEnvelope>(Json, cancellationToken)
                .ConfigureAwait(false);

            return error?.Error?.Description;
        }
        catch (Exception exception) when (exception is JsonException or HttpRequestException or NotSupportedException)
        {
            return null;
        }
    }

    private static ProviderPayment Map(RazorpayPayment payment)
    {
        var status = payment.Status ?? "unknown";

        return new ProviderPayment(
            payment.Id ?? string.Empty,
            status,
            MapMethod(payment.Method),
            RazorpaySignature.FromMinorUnits(payment.Amount),
            RazorpaySignature.FromMinorUnits(payment.AmountRefunded),
            IsAuthorized: string.Equals(status, "authorized", StringComparison.OrdinalIgnoreCase),
            IsCaptured: payment.Captured
                        || string.Equals(status, "captured", StringComparison.OrdinalIgnoreCase),
            IsFailed: string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase),
            MapDetail(payment),
            payment.ErrorCode,
            payment.ErrorDescription,
            RazorpaySignature.FromUnixSeconds(payment.CreatedAt));
    }

    private static ProviderRefund Map(RazorpayRefund refund)
    {
        var status = refund.Status ?? "unknown";

        return new ProviderRefund(
            refund.Id ?? string.Empty,
            refund.PaymentId,
            status,
            RazorpaySignature.FromMinorUnits(refund.Amount),
            IsProcessed: string.Equals(status, "processed", StringComparison.OrdinalIgnoreCase),
            IsFailed: string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase),
            refund.Notes is not null && refund.Notes.TryGetValue("error", out var error) ? error : null,
            RazorpaySignature.FromUnixSeconds(refund.CreatedAt));
    }

    private static ProviderSettlementEntry Map(RazorpaySettlementEntry entry)
        => new(
            string.IsNullOrWhiteSpace(entry.EntityType) ? "payment" : entry.EntityType,
            entry.EntityId,
            entry.PaymentId ?? entry.EntityId,
            RazorpaySignature.FromMinorUnits(entry.Amount),
            RazorpaySignature.FromMinorUnits(entry.Fee),
            RazorpaySignature.FromMinorUnits(entry.Tax),
            RazorpaySignature.FromMinorUnits(entry.Debit),
            RazorpaySignature.FromMinorUnits(entry.Credit),
            RazorpaySignature.FromUnixSeconds(entry.SettledAt ?? entry.CreatedAt));

    /// <summary>The gateway's rail names, mapped onto this platform's vocabulary.</summary>
    /// <remarks>
    /// An unrecognised rail maps to <see cref="PaymentMethod.Unknown"/> rather than throwing. The
    /// gateway adds rails without asking, and a payment that succeeded on one this build has never
    /// heard of is still a payment: refusing to record it would strand a confirmed order.
    /// </remarks>
    private static PaymentMethod MapMethod(string? method)
        => method?.ToLowerInvariant() switch
        {
            "upi" => PaymentMethod.Upi,
            "card" => PaymentMethod.Card,
            "netbanking" => PaymentMethod.NetBanking,
            "wallet" => PaymentMethod.Wallet,
            "emi" => PaymentMethod.Emi,
            _ => PaymentMethod.Unknown,
        };

    /// <summary>
    /// What this platform is allowed to remember about the instrument.
    /// </summary>
    /// <remarks>
    /// Everything else the gateway returns about a card — the token, the issuer's identifiers, the
    /// full VPA — is dropped here and never reaches a database. The rule is enforced by this method
    /// returning a closed type rather than the gateway's own (docs/07-security-compliance.md §4).
    /// </remarks>
    private static PaymentMethodDetail MapDetail(RazorpayPayment payment)
        => new()
        {
            Issuer = payment.Bank ?? payment.Wallet ?? payment.CardNetwork,
            Last4 = payment.CardLast4,
            // The domain half only: "user@okhdfcbank" keeps "okhdfcbank" and drops the identifier,
            // which is personal data and is not needed to answer any support question.
            UpiHandle = payment.Vpa is { Length: > 0 } vpa && vpa.Contains('@', StringComparison.Ordinal)
                ? vpa[(vpa.IndexOf('@', StringComparison.Ordinal) + 1)..]
                : null,
            CardType = payment.CardType,
            International = payment.International,
            EmiMonths = payment.EmiMonths,
        };

    [LoggerMessage(EventId = 1521, Level = LogLevel.Warning,
        Message = "Razorpay refused {Path} with {StatusCode}: {Detail}")]
    private static partial void GatewayRefused(ILogger logger, string path, int statusCode, string? detail);

    [LoggerMessage(EventId = 1522, Level = LogLevel.Error,
        Message = "Razorpay could not be reached for {Path}.")]
    private static partial void GatewayUnreachable(ILogger logger, string path, Exception exception);

    [LoggerMessage(EventId = 1523, Level = LogLevel.Warning,
        Message = "Settlement {SettlementId} was imported without its lines: {Detail}")]
    private static partial void ReconUnavailable(ILogger logger, string settlementId, string? detail);
}
