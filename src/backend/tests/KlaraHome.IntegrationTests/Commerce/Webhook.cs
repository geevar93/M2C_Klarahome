using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Builds the Razorpay webhook bodies these tests sign and post, exactly as the gateway would shape
/// them — one JSON object, a stable key order, and the fields <see cref="FakePaymentProvider"/>'s
/// <c>ReadWebhook</c> reads.
/// </summary>
/// <remarks>
/// A helper class rather than string interpolation scattered across the test files, because the HMAC
/// is computed over these exact bytes: a body built two different ways in two different tests would
/// be two different byte sequences, and a signature test standing on that would be proving nothing
/// about the platform.
/// </remarks>
internal static class Webhook
{
    /// <summary>A <c>payment.captured</c> event body against one gateway order.</summary>
    /// <param name="providerOrderId">The collection it is against.</param>
    /// <param name="providerPaymentId">
    /// The gateway's own id for the payment — the one <see cref="FakePaymentProvider.PayOrder"/>
    /// handed back, and the id the module's re-fetch will look up. A body naming any other id
    /// verifies but resolves to nothing, because the gateway it is signed for has never heard of it.
    /// </param>
    /// <param name="amount">What was captured, in rupees.</param>
    /// <param name="eventId">The gateway's own id for the event, or a fresh one.</param>
    /// <param name="occurredAt">When the gateway says it happened, or now.</param>
    public static string PaymentCaptured(
        string providerOrderId,
        string providerPaymentId,
        decimal amount,
        string? eventId = null,
        DateTimeOffset? occurredAt = null)
        => Build("payment.captured", providerOrderId, providerPaymentId, eventId, occurredAt);

    /// <summary>A <c>payment.failed</c> event body against one gateway order.</summary>
    /// <param name="providerOrderId">The collection it is against.</param>
    /// <param name="providerPaymentId">The gateway's own id for the failed attempt.</param>
    /// <param name="eventId">The gateway's own id for the event, or a fresh one.</param>
    public static string PaymentFailed(string providerOrderId, string providerPaymentId, string? eventId = null)
        => Build("payment.failed", providerOrderId, providerPaymentId, eventId, occurredAt: null);

    /// <summary>A body whose declared timestamp is outside the skew window.</summary>
    /// <param name="providerOrderId">The collection it is against.</param>
    /// <param name="providerPaymentId">The gateway's own id for the payment.</param>
    /// <param name="occurredAt">When the gateway claims it happened.</param>
    public static string StalePaymentCaptured(
        string providerOrderId,
        string providerPaymentId,
        DateTimeOffset occurredAt)
        => PaymentCaptured(providerOrderId, providerPaymentId, amount: 1m, occurredAt: occurredAt);

    /// <summary>Reads the event id a body carries, the way the stored row would.</summary>
    /// <param name="body">A body built by this class.</param>
    public static string EventId(string body)
        => JsonDocument.Parse(body).RootElement.GetProperty("id").GetString()!;

    /// <summary>Signs a body under the fake gateway's shared secret.</summary>
    /// <param name="body">The exact bytes to sign.</param>
    public static string Sign(string body) => FakePaymentProvider.Sign(body);

    /// <summary>A body that re-serialises differently from how it was signed — reordered keys and
    /// extra whitespace — while carrying an equally valid event.</summary>
    /// <param name="providerOrderId">The collection it is against.</param>
    public static string ReorderedPaymentCaptured(string providerOrderId)
    {
        var eventId = $"evt_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        // Keys in a different order and pretty-printed, on purpose: parsing this and re-emitting it
        // through a serialiser would change the bytes, and the raw-body contract exists precisely so
        // that never happens between receipt and verification.
        return $$"""
                 {
                   "event": "payment.captured",
                   "created_at": {{now}},
                   "id": "{{eventId}}",
                   "order_id": "{{providerOrderId}}",
                   "payment_id": "pay_{{Guid.NewGuid():N}}"
                 }
                 """;
    }

    private static string Build(
        string eventType,
        string providerOrderId,
        string paymentId,
        string? eventId,
        DateTimeOffset? occurredAt)
    {
        var id = eventId ?? $"evt_{Guid.NewGuid():N}";
        var at = (occurredAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();

        var payload = new
        {
            id,
            @event = eventType,
            created_at = at,
            order_id = providerOrderId,
            payment_id = paymentId,
        };

        return JsonSerializer.Serialize(payload);
    }
}
