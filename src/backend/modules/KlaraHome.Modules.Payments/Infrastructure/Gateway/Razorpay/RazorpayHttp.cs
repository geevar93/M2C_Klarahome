using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Payments.Infrastructure.Gateway.Razorpay;

/// <summary>The named outbound client, and the only hosts money may be discussed with.</summary>
/// <remarks>
/// The same arrangement Identity's provider client uses, and for the same reason
/// (docs/07-security-compliance.md §3). Nothing here is caller-supplied — the host comes from
/// configuration that only an operator can set — and the allow-list is there so a future mistake
/// that lets a URL through fails at the socket instead of at somebody else's server, with a
/// merchant secret in the header.
/// </remarks>
internal static class RazorpayHttp
{
    /// <summary>The named <see cref="HttpClient"/> the adapter resolves.</summary>
    public const string ClientName = "razorpay";

    /// <summary>The hosts this client may reach.</summary>
    public static readonly IReadOnlySet<string> AllowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "api.razorpay.com",
    };
}

/// <summary>Refuses any outbound request to a host outside the allow-list.</summary>
/// <remarks>
/// A belt-and-braces control. Every request this client makes carries the merchant's API secret in
/// an Authorization header, so a misdirected one is a credential disclosure rather than a failed
/// call — which is what makes the check worth having even though the base address is not
/// caller-supplied.
/// </remarks>
internal sealed partial class RazorpayAllowedHostHandler(ILogger<RazorpayAllowedHostHandler> logger)
    : DelegatingHandler
{
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var host = request.RequestUri?.Host;

        if (host is null || !RazorpayHttp.AllowedHosts.Contains(host))
        {
            BlockedHost(logger, host ?? "<none>");

            throw new HttpRequestException(
                $"Outbound request to '{host}' was refused: it is not in the payment-provider allow-list.");
        }

        return base.SendAsync(request, cancellationToken);
    }

    [LoggerMessage(EventId = 1520, Level = LogLevel.Error,
        Message = "Refused an outbound payment request to {Host}: not in the allow-list.")]
    private static partial void BlockedHost(ILogger logger, string host);
}

/// <summary>
/// The two HMAC checks Razorpay's integration rests on, and the paise conversion beneath them.
/// </summary>
/// <remarks>
/// <para>
/// Both comparisons are constant-time. A signature check that returns as soon as two bytes differ
/// leaks, over enough attempts, how much of a forged signature was right — and this endpoint is
/// reachable by anybody on the internet.
/// </para>
/// <para>
/// The paise conversion is here rather than scattered through the adapter because it is the single
/// arithmetic in this module that silently loses money when it is wrong. Razorpay counts in integer
/// paise; this platform counts in rupees to four decimal places, and every crossing of that boundary
/// goes through these two methods.
/// </para>
/// </remarks>
internal static class RazorpaySignature
{
    /// <summary>Whether an HMAC-SHA256 hex digest of <paramref name="payload"/> matches.</summary>
    /// <param name="payload">The exact bytes that were signed.</param>
    /// <param name="signature">The hex digest offered.</param>
    /// <param name="secret">The shared secret.</param>
    public static bool Verify(string payload, string? signature, string secret)
    {
        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        var expected = Compute(payload, secret);
        var offered = signature.Trim();

        // Length is compared first and separately: FixedTimeEquals requires equal lengths, and a
        // wrong-length signature is not a timing signal worth protecting — it is malformed.
        return expected.Length == offered.Length
               && CryptographicOperations.FixedTimeEquals(
                   Encoding.ASCII.GetBytes(expected),
                   Encoding.ASCII.GetBytes(offered));
    }

    /// <summary>The lowercase hex HMAC-SHA256 of a payload under a secret.</summary>
    /// <param name="payload">What to sign.</param>
    /// <param name="secret">The shared secret.</param>
    public static string Compute(string payload, string secret)
    {
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(payload ?? string.Empty));

        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Rupees to integer paise, rounded half away from zero.
    /// </summary>
    /// <remarks>
    /// Half away from zero rather than .NET's banker's rounding default. Banker's rounding is right
    /// for a long series of independent figures and wrong for a single amount a customer has agreed
    /// to: 1.005 must become 101 paise, not 100, because the shopper was shown 1.01.
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
