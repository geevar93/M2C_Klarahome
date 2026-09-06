using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KlaraHome.Modules.Settlements.Application;
using KlaraHome.SharedKernel.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Settlements.Infrastructure.Payouts.Razorpay;

/// <summary>
/// RazorpayX, behind this platform's own interface (docs/08-integrations.md §1).
/// </summary>
/// <remarks>
/// <para>
/// The fallback rail, for a merchant on whose account Route is not enabled. It differs from Route in
/// the way that matters operationally: an X payout moves real money out of a real balance the
/// platform has to keep funded, and it reaches the seller's own bank rather than an account inside
/// the gateway. A transfer therefore carries a UTR, and a seller's bank statement shows this
/// platform's narration.
/// </para>
/// <para>
/// The destination is a fund account at the gateway rather than a bank account this platform holds.
/// That is not a limitation, it is the point: the account number never leaves the seller's own record
/// encrypted, and no bank detail crosses the module boundary to make this call.
/// </para>
/// <para>
/// <c>queue_if_low_balance</c> is deliberately false. A payout the gateway holds because the balance
/// is short is a payout that will go at an unpredictable time, and a settlement ledger that had marked
/// it sent would have a seller's cycle discharged by money that had not moved. Refusing it means an
/// operator funds the account and the batch is re-run, which is slower and true.
/// </para>
/// </remarks>
/// <param name="options">The credentials and endpoint. Blank until an operator fills them in.</param>
/// <param name="payoutOptions">Supplies the rail and the narration.</param>
/// <param name="clients">Supplies the named, allow-listed outbound client.</param>
/// <param name="logger">Reports what the gateway said when it refused.</param>
internal sealed partial class RazorpayXPayoutProvider(
    IOptionsMonitor<RazorpayPayoutOptions> options,
    IOptionsMonitor<PayoutOptions> payoutOptions,
    IHttpClientFactory clients,
    ILogger<RazorpayXPayoutProvider> logger) : IPayoutProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The X statuses that mean the money has arrived.</summary>
    private static readonly string[] Processed = ["processed"];

    /// <summary>The X statuses that mean it will not.</summary>
    /// <remarks>
    /// <c>reversed</c> is here for the same reason it is on the Route adapter: money that went and
    /// came back is money the seller does not have.
    /// </remarks>
    private static readonly string[] Failed = ["failed", "reversed", "cancelled", "rejected"];

    /// <inheritdoc />
    public string Name => PayoutProviders.X;

    /// <inheritdoc />
    /// <remarks>
    /// The source account number is part of the test, unlike Route's. An X payout is drawn from a
    /// named balance, and an adapter with credentials but no account number would fail on the first
    /// send rather than declare itself unusable — which is the difference between a configuration
    /// problem an operator can see and one they discover on payout day.
    /// </remarks>
    public bool IsConfigured
        => options.CurrentValue.HasCredentials
           && !string.IsNullOrWhiteSpace(options.CurrentValue.AccountNumber);

    /// <inheritdoc />
    public async Task<Result<ProviderPayout>> SendAsync(
        PayoutRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsConfigured)
        {
            return Result.Failure<ProviderPayout>(SettlementsErrors.ProviderUnavailable);
        }

        if (string.IsNullOrWhiteSpace(request.DestinationAccountId))
        {
            return Result.Failure<ProviderPayout>(SettlementsErrors.ProviderFailed(
                "The seller has no fund account at the gateway, so a direct payout cannot be made."));
        }

        var settings = payoutOptions.CurrentValue;

        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["account_number"] = options.CurrentValue.AccountNumber,
            ["fund_account_id"] = request.DestinationAccountId,
            ["amount"] = RazorpayPayoutHttp.ToMinorUnits(request.Amount),
            ["currency"] = request.CurrencyCode,
            ["mode"] = settings.Mode,
            ["purpose"] = "vendor_payment",

            // Deliberately false. See the class remarks: a queued payout is money that moves at a
            // time the ledger cannot predict.
            ["queue_if_low_balance"] = false,
            ["reference_id"] = request.PayoutItemId.ToString(),
            ["narration"] = Narration(settings.Narration),
            ["notes"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["payout_item_id"] = request.PayoutItemId.ToString(),
                ["batch_reference"] = request.Reference,
                ["vendor_id"] = request.VendorId.ToString(),
            },
        };

        var result = await SendAsync<RazorpayXPayout>(
                HttpMethod.Post,
                "payouts",
                body,
                request.PayoutItemId.ToString(),
                cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure
            ? Result.Failure<ProviderPayout>(result.Error)
            : Map(result.Value);
    }

    /// <inheritdoc />
    public async Task<Result<ProviderPayout>> FetchAsync(
        string providerPayoutId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerPayoutId))
        {
            return Result.Failure<ProviderPayout>(SettlementsErrors.NotFound("payout"));
        }

        var result = await SendAsync<RazorpayXPayout>(
                HttpMethod.Get,
                $"payouts/{Uri.EscapeDataString(providerPayoutId)}",
                body: null,
                idempotencyKey: null,
                cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure
            ? Result.Failure<ProviderPayout>(result.Error)
            : Map(result.Value);
    }

    /// <summary>Turns the gateway's shape into this platform's.</summary>
    private static Result<ProviderPayout> Map(RazorpayXPayout payout)
    {
        if (string.IsNullOrWhiteSpace(payout.Id))
        {
            return Result.Failure<ProviderPayout>(
                SettlementsErrors.ProviderFailed("The gateway did not return a payout."));
        }

        var status = payout.Status ?? "queued";

        return Result.Success(new ProviderPayout(
            payout.Id,
            status,
            Processed.Contains(status, StringComparer.OrdinalIgnoreCase),
            Failed.Contains(status, StringComparer.OrdinalIgnoreCase),
            payout.Utr,
            payout.FailureReason ?? payout.StatusDetails?.Description,
            RazorpayPayoutHttp.FromUnixSeconds(payout.CreatedAt)));
    }

    /// <summary>
    /// The narration, trimmed to what a bank will actually carry.
    /// </summary>
    /// <remarks>
    /// Thirty characters, and a bank truncates anything longer without saying so. Trimming here means
    /// the operator's configured wording is cut where they can see it rather than in the middle of a
    /// word on somebody's statement.
    /// </remarks>
    private static string Narration(string configured)
    {
        var narration = string.IsNullOrWhiteSpace(configured) ? "Marketplace payout" : configured.Trim();
        return narration.Length <= 30 ? narration : narration[..30];
    }

    /// <summary>
    /// One call to the gateway, and the single place a gateway failure becomes an ordinary result.
    /// </summary>
    private async Task<Result<TResponse>> SendAsync<TResponse>(
        HttpMethod method,
        string path,
        object? body,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;

        if (!settings.HasCredentials)
        {
            return Result.Failure<TResponse>(SettlementsErrors.ProviderUnavailable);
        }

        try
        {
            var client = clients.CreateClient(RazorpayPayoutHttp.ClientName);
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);

            using var request = new HttpRequestMessage(method, new Uri(new Uri(settings.BaseUrl), path));

            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                RazorpayPayoutHttp.BasicCredential(settings.KeyId, settings.KeySecret));

            // RazorpayX's own idempotency header. A retry after a timeout — the case where we do not
            // know whether the money went — returns the original payout rather than making a second.
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                request.Headers.TryAddWithoutValidation("X-Payout-Idempotency", idempotencyKey);
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

                return Result.Failure<TResponse>(SettlementsErrors.ProviderFailed(failure));
            }

            var value = await response.Content
                .ReadFromJsonAsync<TResponse>(Json, cancellationToken)
                .ConfigureAwait(false);

            return value is null
                ? Result.Failure<TResponse>(
                    SettlementsErrors.ProviderFailed("The gateway returned an empty response."))
                : Result.Success(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            GatewayUnreachable(logger, path, exception);
            return Result.Failure<TResponse>(SettlementsErrors.ProviderFailed());
        }
    }

    /// <summary>Reads the gateway's own description of a refusal, where it gave one.</summary>
    private static async Task<string?> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
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

    [LoggerMessage(EventId = 1823, Level = LogLevel.Warning,
        Message = "RazorpayX refused {Path} with HTTP {StatusCode}: {GatewayMessage}")]
    private static partial void GatewayRefused(
        ILogger logger,
        string path,
        int statusCode,
        string? gatewayMessage);

    [LoggerMessage(EventId = 1824, Level = LogLevel.Error,
        Message = "RazorpayX could not be reached for {Path}.")]
    private static partial void GatewayUnreachable(ILogger logger, string path, Exception exception);
}
