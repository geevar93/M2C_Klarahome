using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Settlements.Infrastructure.Payouts.Razorpay;

/// <summary>
/// The gateway's credentials, as this module reads them
/// (docs/06-infrastructure-devops.md §4.1, docs/08-integrations.md §1).
/// </summary>
/// <remarks>
/// <para>
/// Bound to the same <c>Razorpay</c> configuration section the Payments module binds its own options
/// to, so a deployment that has configured payments has already supplied the key pair a payout needs.
/// It is a second class rather than a shared one because a module may not reference another's types —
/// the same boundary cost that duplicates the financial-year arithmetic three times over.
/// </para>
/// <para>
/// It reads a strict subset: the key pair, the merchant account number that RazorpayX payouts are
/// drawn from, and the flag saying whether Route is enabled. The webhook secret is deliberately
/// absent — this module learns a transfer's fate by asking, not by being told, and holding a signing
/// secret it never verifies with would be holding a credential for no reason.
/// </para>
/// </remarks>
internal sealed class RazorpayPayoutOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Razorpay";

    /// <summary>The publishable key id, which is also the API username.</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>The API secret. Never leaves this process and is never logged.</summary>
    public string KeySecret { get; set; } = string.Empty;

    /// <summary>
    /// The merchant's own account number at RazorpayX, which a direct payout is drawn from.
    /// </summary>
    /// <remarks>
    /// Required by the X rail and meaningless to Route, which moves money inside the gateway and
    /// needs no source account. A deployment on Route leaves it blank.
    /// </remarks>
    public string AccountNumber { get; set; } = string.Empty;

    /// <summary>Whether Route is enabled on this merchant account.</summary>
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

/// <summary>The named outbound client, and the only host a payout may be discussed with.</summary>
/// <remarks>
/// The same arrangement the payment adapter's client uses, and for the same reason
/// (docs/07-security-compliance.md §3). Every request this client makes carries the merchant's API
/// secret in an Authorization header, so a misdirected one is a credential disclosure rather than a
/// failed call — which is what makes the allow-list worth having even though the base address comes
/// from configuration only an operator can set.
/// </remarks>
internal static class RazorpayPayoutHttp
{
    /// <summary>The named <see cref="HttpClient"/> the adapters resolve.</summary>
    public const string ClientName = "razorpay-payouts";

    /// <summary>The hosts this client may reach.</summary>
    public static readonly IReadOnlySet<string> AllowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "api.razorpay.com",
    };

    /// <summary>
    /// Rupees to integer paise, rounded half away from zero.
    /// </summary>
    /// <remarks>
    /// Half away from zero rather than .NET's banker's rounding default. Banker's rounding is right
    /// for a long series of independent figures and wrong for a single amount somebody is owed: a
    /// seller's statement says 1.01 and the transfer must be 101 paise, not 100.
    /// </remarks>
    /// <param name="amount">The amount in rupees.</param>
    public static long ToMinorUnits(decimal amount)
        => (long)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

    /// <summary>Integer paise back to rupees.</summary>
    /// <param name="minorUnits">The amount in paise.</param>
    public static decimal FromMinorUnits(long minorUnits) => minorUnits / 100m;

    /// <summary>The Unix seconds a Razorpay timestamp field carries, as an instant.</summary>
    /// <param name="seconds">The value, or null.</param>
    public static DateTimeOffset? FromUnixSeconds(long? seconds)
        => seconds is null or <= 0 ? null : DateTimeOffset.FromUnixTimeSeconds(seconds.Value);

    /// <summary>The Basic credential for the merchant key pair.</summary>
    /// <param name="keyId">The publishable key id.</param>
    /// <param name="keySecret">The API secret.</param>
    public static string BasicCredential(string keyId, string keySecret)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{keyId}:{keySecret}")));
}

/// <summary>Refuses any outbound request to a host outside the allow-list.</summary>
internal sealed partial class RazorpayPayoutAllowedHostHandler(ILogger<RazorpayPayoutAllowedHostHandler> logger)
    : DelegatingHandler
{
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var host = request.RequestUri?.Host;

        if (host is null || !RazorpayPayoutHttp.AllowedHosts.Contains(host))
        {
            BlockedHost(logger, host ?? "<none>");

            throw new HttpRequestException(
                $"Outbound request to '{host}' was refused: it is not in the payout-provider allow-list.");
        }

        return base.SendAsync(request, cancellationToken);
    }

    [LoggerMessage(EventId = 1820, Level = LogLevel.Error,
        Message = "Refused an outbound payout request to {Host}: not in the allow-list.")]
    private static partial void BlockedHost(ILogger logger, string host);
}

/// <summary>A Route transfer, as the gateway returns it.</summary>
/// <remarks>
/// Only the fields this platform reads. Razorpay's payloads carry a great deal more, and modelling
/// all of it would be modelling somebody else's release schedule.
/// </remarks>
internal sealed record RazorpayTransfer
{
    /// <summary>The gateway's id for the transfer, as <c>trf_…</c>.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>Its own status word: <c>created</c>, <c>pending</c>, <c>processed</c>, <c>failed</c>, <c>reversed</c>.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>The linked account it went to.</summary>
    [JsonPropertyName("recipient")]
    public string? Recipient { get; init; }

    /// <summary>What was transferred, in paise.</summary>
    [JsonPropertyName("amount")]
    public long Amount { get; init; }

    /// <summary>When it was created, in Unix seconds.</summary>
    [JsonPropertyName("created_at")]
    public long? CreatedAt { get; init; }

    /// <summary>The gateway's own description of why it failed.</summary>
    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; init; }
}

/// <summary>A RazorpayX payout, as the gateway returns it.</summary>
internal sealed record RazorpayXPayout
{
    /// <summary>The gateway's id for the payout, as <c>pout_…</c>.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>
    /// Its own status word: <c>queued</c>, <c>pending</c>, <c>processing</c>, <c>processed</c>,
    /// <c>reversed</c>, <c>cancelled</c>, <c>rejected</c>, <c>failed</c>.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>What was sent, in paise.</summary>
    [JsonPropertyName("amount")]
    public long Amount { get; init; }

    /// <summary>The bank's unique transaction reference, once the transfer has cleared.</summary>
    [JsonPropertyName("utr")]
    public string? Utr { get; init; }

    /// <summary>When it was created, in Unix seconds.</summary>
    [JsonPropertyName("created_at")]
    public long? CreatedAt { get; init; }

    /// <summary>Why it failed, where the gateway says.</summary>
    [JsonPropertyName("failure_reason")]
    public string? FailureReason { get; init; }

    /// <summary>The structured error, on the responses that carry one instead.</summary>
    [JsonPropertyName("status_details")]
    public RazorpayStatusDetails? StatusDetails { get; init; }
}

/// <summary>The structured reason a payout is in the state it is in.</summary>
internal sealed record RazorpayStatusDetails
{
    /// <summary>A machine-readable reason, as <c>beneficiary_bank_failure</c>.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    /// <summary>A sentence an operator can act on.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

/// <summary>The envelope Razorpay wraps an error in.</summary>
internal sealed record RazorpayErrorEnvelope
{
    /// <summary>The error itself.</summary>
    [JsonPropertyName("error")]
    public RazorpayErrorBody? Error { get; init; }
}

/// <summary>What Razorpay says went wrong.</summary>
internal sealed record RazorpayErrorBody
{
    /// <summary>Its own code, as <c>BAD_REQUEST_ERROR</c>.</summary>
    [JsonPropertyName("code")]
    public string? Code { get; init; }

    /// <summary>A sentence, sometimes one worth showing an operator.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }
}
