using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Shipping.Infrastructure.Courier.Aggregator;

/// <summary>The named outbound client parcels are booked through.</summary>
internal static class AggregatorHttp
{
    /// <summary>The named <see cref="HttpClient"/> the adapter resolves.</summary>
    public const string ClientName = "shipping-aggregator";
}

/// <summary>
/// Refuses any outbound request to a host other than the configured aggregator's.
/// </summary>
/// <remarks>
/// <para>
/// The same control the payments client carries (docs/07-security-compliance.md §3), with one
/// difference that matters. Razorpay's host is a constant, so its allow-list is a literal; a
/// logistics aggregator is chosen per deployment, so the list here is derived from the configured
/// base URL and nothing else. That keeps the guarantee — one host, from configuration only — without
/// pretending to know in advance which aggregator a client will use.
/// </para>
/// <para>
/// Every request this client makes carries the aggregator's bearer token, so a misdirected one would
/// be a credential disclosure rather than a failed call. That is what makes the check worth having
/// even though no caller supplies the URL.
/// </para>
/// </remarks>
/// <param name="options">Supplies the configured base URL, re-read on each request.</param>
/// <param name="logger">Reports a refusal, which should never happen and must be loud when it does.</param>
internal sealed partial class AggregatorAllowedHostHandler(
    IOptionsMonitor<ShippingOptions> options,
    ILogger<AggregatorAllowedHostHandler> logger) : DelegatingHandler
{
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var host = request.RequestUri?.Host;
        var allowed = AllowedHost(options.CurrentValue.BaseUrl);

        if (host is null
            || allowed is null
            || !string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase))
        {
            BlockedHost(logger, host ?? "<none>", allowed ?? "<unconfigured>");

            throw new HttpRequestException(
                $"Outbound request to '{host}' was refused: it is not the configured logistics host.");
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static string? AllowedHost(string? baseUrl)
        => Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri.Host
            : null;

    [LoggerMessage(EventId = 1720, Level = LogLevel.Error,
        Message = "Refused an outbound logistics request to {Host}; only {AllowedHost} is configured.")]
    private static partial void BlockedHost(ILogger logger, string host, string allowedHost);
}

/// <summary>
/// The webhook signature check, and the unit conversions the aggregator's API needs.
/// </summary>
/// <remarks>
/// <para>
/// The comparison is constant-time. A check that returns as soon as two bytes differ leaks, over
/// enough attempts, how much of a forged signature was right — and the webhook endpoint is reachable
/// by anybody on the internet.
/// </para>
/// <para>
/// The conversions are here rather than scattered through the adapter because they are where a
/// parcel silently becomes ten times heavier. Aggregators in India quote weight in <em>kilograms</em>
/// as a decimal and dimensions in centimetres; this platform stores grams and centimetres, and every
/// crossing of that boundary goes through these methods.
/// </para>
/// </remarks>
internal static class AggregatorSignature
{
    /// <summary>Whether an HMAC-SHA256 hex digest of the raw body matches the offered signature.</summary>
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

        // Length first and separately: FixedTimeEquals requires equal lengths, and a wrong-length
        // signature is malformed rather than a timing signal worth protecting.
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

    /// <summary>Grams to the decimal kilograms an aggregator quotes in.</summary>
    /// <param name="grams">The weight in grams.</param>
    public static decimal ToKilograms(int grams) => Math.Round(grams / 1000m, 3, MidpointRounding.AwayFromZero);

    /// <summary>Decimal kilograms back to grams, rounded up so a part-gram is never lost.</summary>
    /// <param name="kilograms">The weight in kilograms, or null.</param>
    public static int? ToGrams(decimal? kilograms)
        => kilograms is null or <= 0m ? null : (int)Math.Ceiling(kilograms.Value * 1000m);
}
