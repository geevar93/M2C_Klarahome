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
/// Razorpay Route, behind this platform's own interface (docs/08-integrations.md §1).
/// </summary>
/// <remarks>
/// <para>
/// A Route transfer moves money from the platform's balance at the gateway to a seller's linked
/// account at the same gateway; the seller's own settlement to their bank then happens on Razorpay's
/// schedule. It is the recommended rail because the deferred model — capture to the platform, then
/// transfer after the return window — is exactly the shape a settlement ledger with statutory
/// deductions has.
/// </para>
/// <para>
/// The linked account is created when a seller is activated and stored as their
/// <c>gateway_account_id</c>. A seller without one is not payable through this rail, and the batch
/// records that as a skipped item with the reason on it rather than failing the run.
/// </para>
/// <para>
/// Nothing here throws at a caller. A gateway that is unreachable, slow or angry is an ordinary
/// outcome: it becomes a failed <see cref="Result"/>, the item stays unsent, and the batch carries on
/// to the next seller. An exception escaping would abandon four hundred transfers because one bank
/// details field was wrong.
/// </para>
/// <para>
/// Every amount crossing this boundary goes through <see cref="RazorpayPayoutHttp.ToMinorUnits"/> and
/// its inverse. Razorpay counts in integer paise and this platform counts in rupees, and every
/// hand-rolled multiplication by a hundred is a place money can be lost by a factor of a hundred.
/// </para>
/// </remarks>
/// <param name="options">The credentials and endpoint. Blank until an operator fills them in.</param>
/// <param name="clients">Supplies the named, allow-listed outbound client.</param>
/// <param name="logger">Reports what the gateway said when it refused.</param>
internal sealed partial class RazorpayRoutePayoutProvider(
    IOptionsMonitor<RazorpayPayoutOptions> options,
    IHttpClientFactory clients,
    ILogger<RazorpayRoutePayoutProvider> logger) : IPayoutProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The Route statuses that mean the money has arrived.</summary>
    private static readonly string[] Processed = ["processed", "settled"];

    /// <summary>The Route statuses that mean it will not.</summary>
    /// <remarks>
    /// <c>reversed</c> is here deliberately. A reversal is money that went and came back, and calling
    /// it a success because it once succeeded would leave a seller marked paid with an empty account.
    /// </remarks>
    private static readonly string[] Failed = ["failed", "reversed", "cancelled"];

    /// <inheritdoc />
    public string Name => PayoutProviders.Route;

    /// <inheritdoc />
    public bool IsConfigured => options.CurrentValue.HasCredentials && options.CurrentValue.RouteEnabled;

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
                "The seller has no linked account at the gateway, so a Route transfer cannot be made."));
        }

        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["account"] = request.DestinationAccountId,
            ["amount"] = RazorpayPayoutHttp.ToMinorUnits(request.Amount),
            ["currency"] = request.CurrencyCode,

            // Our own ids, echoed back on every event the gateway sends about this transfer. It is
            // what lets a webhook or a report be matched to a payout item without a lookup table.
            ["notes"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["payout_item_id"] = request.PayoutItemId.ToString(),
                ["batch_reference"] = request.Reference,
                ["vendor_id"] = request.VendorId.ToString(),
            },
        };

        // The gateway's own idempotency header, keyed on the payout item. A retry after a timeout —
        // the case where we do not know whether the money went — returns the original transfer rather
        // than making a second one.
        var result = await SendAsync<RazorpayTransfer>(
                HttpMethod.Post,
                "transfers",
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
            return Result.Failure<ProviderPayout>(SettlementsErrors.NotFound("transfer"));
        }

        var result = await SendAsync<RazorpayTransfer>(
                HttpMethod.Get,
                $"transfers/{Uri.EscapeDataString(providerPayoutId)}",
                body: null,
                idempotencyKey: null,
                cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure
            ? Result.Failure<ProviderPayout>(result.Error)
            : Map(result.Value);
    }

    /// <summary>Turns the gateway's shape into this platform's.</summary>
    /// <remarks>
    /// A transfer with no id is not a transfer. Route answers 200 with an error body in some
    /// conditions, and treating that as a success would record a payout the gateway never made.
    /// </remarks>
    private static Result<ProviderPayout> Map(RazorpayTransfer transfer)
    {
        if (string.IsNullOrWhiteSpace(transfer.Id))
        {
            return Result.Failure<ProviderPayout>(
                SettlementsErrors.ProviderFailed("The gateway did not return a transfer."));
        }

        var status = transfer.Status ?? "created";

        return Result.Success(new ProviderPayout(
            transfer.Id,
            status,
            Processed.Contains(status, StringComparer.OrdinalIgnoreCase),
            Failed.Contains(status, StringComparer.OrdinalIgnoreCase),

            // Route does not publish a bank reference on the transfer itself: the money reaches the
            // seller's bank through their own settlement, which carries its own UTR. Leaving it null
            // is honest; inventing one from the transfer id would put a number on a statement that no
            // bank has ever heard of.
            Utr: null,
            transfer.ErrorDescription,
            RazorpayPayoutHttp.FromUnixSeconds(transfer.CreatedAt)));
    }

    /// <summary>
    /// One call to the gateway, and the single place a gateway failure becomes an ordinary result.
    /// </summary>
    /// <remarks>
    /// The error description is surfaced, because "the linked account is not activated" is worth
    /// putting on a payout item and "the request failed" is not. What is never surfaced, and never
    /// logged, is the request: it carries the merchant credential.
    /// </remarks>
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
            // The caller went away. Not a gateway fault, and not something to log as one.
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

    [LoggerMessage(EventId = 1821, Level = LogLevel.Warning,
        Message = "Razorpay Route refused {Path} with HTTP {StatusCode}: {GatewayMessage}")]
    private static partial void GatewayRefused(
        ILogger logger,
        string path,
        int statusCode,
        string? gatewayMessage);

    [LoggerMessage(EventId = 1822, Level = LogLevel.Error,
        Message = "Razorpay Route could not be reached for {Path}.")]
    private static partial void GatewayUnreachable(ILogger logger, string path, Exception exception);
}
