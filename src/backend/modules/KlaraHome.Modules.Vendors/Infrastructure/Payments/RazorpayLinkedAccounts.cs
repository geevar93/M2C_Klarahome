using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KlaraHome.Modules.Vendors.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Vendors.Infrastructure.Payments;

/// <summary>
/// The gateway credentials this module needs to open a seller's linked account
/// (docs/08-integrations.md §1, docs/06-infrastructure-devops.md §4.1).
/// </summary>
/// <remarks>
/// Bound to the same <c>Razorpay</c> configuration section Payments and Settlements bind their own
/// options to, so a deployment that configured payments has already supplied the key pair. A third
/// class rather than a shared one because a module may not reference another's types — the same
/// boundary cost that already gives Payments and Settlements one options class each.
/// </remarks>
internal sealed class RazorpayAccountOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Razorpay";

    /// <summary>The publishable key id, which is also the API username.</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>The API secret. Never leaves this process and is never logged.</summary>
    public string KeySecret { get; set; } = string.Empty;

    /// <summary>
    /// Whether Route is enabled on this merchant account. Off means no linked account is opened,
    /// which is the correct behaviour for a deployment paying sellers by bank transfer by hand.
    /// </summary>
    public bool RouteEnabled { get; set; }

    /// <summary>The API root. Configurable so a sandbox or a proxy can be pointed at.</summary>
    [Required]
    public string BaseUrl { get; set; } = "https://api.razorpay.com/v1/";

    /// <summary>How long the gateway has to answer before a call is abandoned, in seconds.</summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>Whether the key pair is present at all.</summary>
    public bool HasCredentials
        => !string.IsNullOrWhiteSpace(KeyId) && !string.IsNullOrWhiteSpace(KeySecret);
}

/// <summary>The named outbound client, and the only host a linked account may be discussed with.</summary>
internal static class RazorpayAccountHttp
{
    /// <summary>The named <see cref="HttpClient"/> the adapter resolves.</summary>
    public const string ClientName = "razorpay-accounts";

    /// <summary>The hosts this client may reach.</summary>
    public static readonly IReadOnlySet<string> AllowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "api.razorpay.com",
    };

    /// <summary>The Basic credential for the merchant key pair.</summary>
    /// <param name="keyId">The publishable key id.</param>
    /// <param name="keySecret">The API secret.</param>
    public static string BasicCredential(string keyId, string keySecret)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{keyId}:{keySecret}")));
}

/// <summary>Refuses any outbound request to a host outside the allow-list.</summary>
/// <remarks>
/// Every request this client makes carries the merchant's API secret and a seller's bank account
/// number in the clear, so a misdirected one is a disclosure rather than a failed call
/// (docs/07-security-compliance.md §3).
/// </remarks>
/// <param name="logger">Reports the refusal, which is a security event.</param>
internal sealed partial class RazorpayAccountAllowedHostHandler(ILogger<RazorpayAccountAllowedHostHandler> logger)
    : DelegatingHandler
{
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var host = request.RequestUri?.Host;

        if (host is null || !RazorpayAccountHttp.AllowedHosts.Contains(host))
        {
            BlockedHost(logger, host ?? "<none>");

            throw new HttpRequestException(
                $"Outbound request to '{host}' was refused: it is not in the payout-account allow-list.");
        }

        return base.SendAsync(request, cancellationToken);
    }

    [LoggerMessage(EventId = 1910, Level = LogLevel.Error,
        Message = "Refused an outbound payout-account request to {Host}: not in the allow-list.")]
    private static partial void BlockedHost(ILogger logger, string host);
}

/// <summary>A linked account, as the gateway returns it. Only the field this platform reads.</summary>
internal sealed record RazorpayLinkedAccount
{
    /// <summary>The gateway's id for the account, as <c>acc_…</c>. Stored as the seller's payout account.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }
}

/// <summary>
/// Opens a seller's Razorpay Route linked account (docs/08-integrations.md §2).
/// </summary>
/// <remarks>
/// <para>
/// The implementation of the seam Step 9 declared and Step 18 was to fill. It landed here instead
/// because the data it needs — the legal name, the PAN, the verified primary bank account — is this
/// module's, and Settlements would have had to reach across a module boundary to read it
/// (Step 28B, deliverable 21). Without it every Route payout item is recorded <c>Skipped</c> with
/// "the seller has no payout account at the gateway", which is exactly what a marketplace that
/// cannot pay anybody looks like.
/// </para>
/// <para>
/// Nothing here throws at its caller and nothing here fails an activation. A gateway that is
/// unreachable, slow or angry is an ordinary outcome: it answers null, the seller trades without a
/// <c>gateway_account_id</c>, and the next activation attempt or an operator's re-run creates the
/// account. Refusing a seller permission to trade because a third party was down would be the wrong
/// trade in every direction.
/// </para>
/// <para>
/// <c>tnc_accepted</c> is sent as true because the seller accepted this platform's terms when they
/// applied, and Route requires the marketplace to assert that before it will open an account. It is
/// the one field here that is a claim rather than a copy, and it is only ever sent for a seller an
/// operator has explicitly activated.
/// </para>
/// <para>
/// Built and left unproven, on the same terms as Steps 15 to 18: the Razorpay credentials are Step
/// 32's, so no call in this class has been made against the real gateway.
/// </para>
/// </remarks>
/// <param name="options">The credentials and endpoint. Blank until an operator fills them in.</param>
/// <param name="clients">Supplies the named, allow-listed outbound client.</param>
/// <param name="logger">Reports what the gateway said when it refused.</param>
internal sealed partial class RazorpayLinkedAccounts(
    IOptionsMonitor<RazorpayAccountOptions> options,
    IHttpClientFactory clients,
    ILogger<RazorpayLinkedAccounts> logger) : IVendorPayoutAccounts
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public bool IsConfigured => options.CurrentValue.HasCredentials && options.CurrentValue.RouteEnabled;

    /// <inheritdoc />
    public async Task<string?> CreateAsync(PayoutAccountRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsConfigured)
        {
            return null;
        }

        var settings = options.CurrentValue;

        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = request.DisplayName,
            ["email"] = request.Email,
            ["tnc_accepted"] = true,
            ["account_details"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["business_name"] = request.LegalName,
                ["business_type"] = BusinessTypeOf(request),
            },
            ["bank_account"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["ifsc_code"] = request.Ifsc,
                ["account_number"] = request.AccountNumber,
                ["beneficiary_name"] = request.AccountHolderName,
            },

            // Our own id, echoed back on everything the gateway ever says about this account. It is
            // what lets an operator reconcile a linked account to a seller without a lookup table.
            ["notes"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["vendor_id"] = request.VendorId.ToString(),
            },
        };

        try
        {
            var client = clients.CreateClient(RazorpayAccountHttp.ClientName);
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);

            using var message = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(new Uri(settings.BaseUrl), "accounts"));

            message.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                RazorpayAccountHttp.BasicCredential(settings.KeyId, settings.KeySecret));

            // Keyed on the seller, so a retry after a timeout — the case where we do not know
            // whether the account was created — returns the original rather than opening a second.
            message.Headers.TryAddWithoutValidation("X-Payout-Idempotency", request.VendorId.ToString());

            message.Content = new StringContent(
                JsonSerializer.Serialize(body, Json),
                Encoding.UTF8,
                "application/json");

            using var response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var failure = await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false);
                GatewayRefused(logger, request.VendorId, (int)response.StatusCode, failure ?? "no reason given");

                return null;
            }

            var account = await response.Content
                .ReadFromJsonAsync<RazorpayLinkedAccount>(Json, cancellationToken)
                .ConfigureAwait(false);

            if (account?.Id is not { Length: > 0 } accountId)
            {
                GatewayRefused(logger, request.VendorId, (int)response.StatusCode, "no account id was returned");
                return null;
            }

            LinkedAccountCreated(logger, request.VendorId, accountId);
            return accountId;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller went away. Not a gateway fault, and not something to log as one.
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            GatewayUnreachable(logger, request.VendorId, exception);
            return null;
        }
    }

    /// <summary>
    /// This platform's legal forms, in Route's own vocabulary.
    /// </summary>
    /// <remarks>
    /// Route accepts a fixed set of business types and refuses anything else, so the mapping is
    /// explicit rather than a lowercased enum name. An unmapped form falls back to
    /// <c>individual</c>, which is the most restrictive of them: an account opened too narrowly is
    /// corrected by an operator, whereas one opened too broadly is a compliance problem.
    /// </remarks>
    private static string BusinessTypeOf(PayoutAccountRequest request)
        => request.BusinessType switch
        {
            VendorBusinessType.Individual => "individual",
            VendorBusinessType.SoleProprietorship => "proprietorship",
            VendorBusinessType.Partnership => "partnership",
            VendorBusinessType.LimitedLiabilityPartnership => "llp",
            VendorBusinessType.PrivateLimited => "private_limited",
            VendorBusinessType.PublicLimited => "public_limited",
            VendorBusinessType.HinduUndividedFamily => "huf",
            VendorBusinessType.Trust => "trust",
            _ => "individual",
        };

    /// <summary>Reads the gateway's own description of a refusal, where it gave one.</summary>
    private static async Task<string?> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            // Truncated. The gateway's error bodies are short, but a proxy or a captive portal in
            // front of it can answer with a page, and a page does not belong in a log line.
            return body.Length <= 500 ? body : body[..500];
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            return null;
        }
    }

    [LoggerMessage(EventId = 1911, Level = LogLevel.Information,
        Message = "Opened Razorpay linked account {GatewayAccountId} for vendor {VendorId}.")]
    private static partial void LinkedAccountCreated(ILogger logger, Guid vendorId, string gatewayAccountId);

    [LoggerMessage(EventId = 1912, Level = LogLevel.Warning,
        Message = "The gateway refused to open a linked account for vendor {VendorId} with {StatusCode}: "
                  + "{Failure}. They can trade but cannot yet be paid.")]
    private static partial void GatewayRefused(ILogger logger, Guid vendorId, int statusCode, string failure);

    [LoggerMessage(EventId = 1913, Level = LogLevel.Warning,
        Message = "The gateway could not be reached to open a linked account for vendor {VendorId}. "
                  + "They can trade but cannot yet be paid; the next activation attempt retries.")]
    private static partial void GatewayUnreachable(ILogger logger, Guid vendorId, Exception exception);
}
