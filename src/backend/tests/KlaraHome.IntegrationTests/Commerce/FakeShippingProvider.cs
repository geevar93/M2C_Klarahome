using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KlaraHome.Modules.Shipping.Application;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure.Courier;
using KlaraHome.SharedKernel.Results;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The aggregator at the network boundary, and nothing below it.
/// </summary>
/// <remarks>
/// <para>
/// Registered under <see cref="ShippingProviders.Shiprocket"/> — the name <c>Shipping:Provider</c>
/// selects and a booked shipment stores — so the registry picks it and every later call about a
/// parcel comes back to it, exactly as it would with the real adapter.
/// </para>
/// <para>
/// It keeps the consignments it booked and the scans recorded against them, because the two paths
/// worth proving here both read a parcel back: the polling fallback that recovers a webhook nobody
/// delivered, and the tracking a shopper sees. Serviceability is a set the test controls rather
/// than a blanket yes, so <c>PINCODE_NOT_SERVICEABLE</c> stays provable and stays distinct from
/// the delivery-coverage refusal beside it.
/// </para>
/// </remarks>
internal sealed class FakeShippingProvider : IShippingProvider
{
    /// <summary>The secret webhook bodies are signed with in these tests.</summary>
    public const string WebhookSecret = "test-courier-secret";

    private readonly ConcurrentDictionary<string, CourierBooking> _bookings = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<CourierScan>> _scans = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Guid> _shipmentIdByAwb = new(StringComparer.Ordinal);
    private static int _sequence;

    /// <inheritdoc />
    public string Name => ShippingProviders.Shiprocket;

    /// <summary>Whether this deployment has credentials. Settable, so the fallback stays provable.</summary>
    public bool IsConfigured { get; set; } = true;

    /// <inheritdoc />
    public bool CanVerifyWebhooks { get; set; } = true;

    /// <summary>
    /// PIN codes this courier will carry to. Null means every one of them.
    /// </summary>
    /// <remarks>
    /// Null rather than an empty set as the permissive default, so a test that never mentions
    /// serviceability is not silently asserting that nowhere is serviceable.
    /// </remarks>
    public HashSet<string>? Serviceable { get; set; }

    /// <summary>PIN codes that are reachable but will not take cash. Empty by default.</summary>
    public HashSet<string> NoCod { get; } = new(StringComparer.Ordinal);

    /// <summary>Whether the next booking should fail, so the manual-waybill fallback is reachable.</summary>
    public bool FailBookings { get; set; }

    /// <summary>Every booking this courier was asked for, in order.</summary>
    public List<CourierBookingRequest> Bookings { get; } = [];

    /// <summary>Every handover sheet it was told about.</summary>
    public List<IReadOnlyList<string>> Manifests { get; } = [];

    /// <summary>Collections it was asked to make, by air waybill.</summary>
    public ConcurrentDictionary<string, DateTimeOffset> Pickups { get; } = new(StringComparer.Ordinal);

    /// <summary>Air waybills it was told to cancel.</summary>
    public List<string> Cancellations { get; } = [];

    /// <inheritdoc />
    public Task<Result<CourierServiceability>> CheckServiceabilityAsync(
        string pincode,
        string? pickupPincode,
        int weightGrams,
        bool isCod,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return Task.FromResult(Result.Failure<CourierServiceability>(ShippingErrors.ProviderUnavailable));
        }

        var reachable = Serviceable is null || Serviceable.Contains(pincode);

        return Task.FromResult(Result.Success(new CourierServiceability(
            ShippingProviders.Shiprocket,
            PrepaidOk: reachable,
            CodOk: reachable && !NoCod.Contains(pincode),
            PickupOk: reachable,
            EtaDays: reachable ? 3 : null,
            MaxWeightGrams: 30_000,
            City: reachable ? "Hyderabad" : null,
            State: reachable ? "Telangana" : null)));
    }

    /// <inheritdoc />
    public Task<Result<CourierBooking>> CreateShipmentAsync(
        CourierBookingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsConfigured || FailBookings)
        {
            return Task.FromResult(Result.Failure<CourierBooking>(ShippingErrors.ProviderUnavailable));
        }

        lock (Bookings)
        {
            Bookings.Add(request);
        }

        // Globally unique, not merely unique within this instance: every test in the collection
        // shares one tenant and therefore one `(tenant, awb)` unique index, but each test's own
        // CommerceApiFactory gets a fresh FakeShippingProvider whose sequence restarts at 1. A
        // sequence number alone would hand out "AWB00000001" to the first booking of every test.
        var awb = $"AWB{Guid.NewGuid():N}"[..14].ToUpperInvariant();

        var booking = new CourierBooking(
            "Delhivery Surface",
            "Surface 5kg",
            awb,
            $"shp_{Next()}",
            $"https://track.example.test/{awb}",
            DateTimeOffset.UtcNow.AddDays(3),
            FreightCost: 68.00m,
            LabelUrl: $"https://labels.example.test/{awb}.pdf");

        _bookings[awb] = booking;
        _shipmentIdByAwb[awb] = request.ShipmentId;

        return Task.FromResult(Result.Success(booking));
    }

    /// <summary>
    /// Records a courier scan, as the network would have.
    /// </summary>
    /// <remarks>
    /// The one half of the courier a test drives directly. What the platform does with the scan —
    /// deliver the sub-order, raise an NDR, start an RTO — is the module's own code, reached either
    /// by the webhook this builds a body for or by the polling fallback that reads it back.
    /// </remarks>
    /// <param name="awb">The consignment.</param>
    /// <param name="status">Where it has got to.</param>
    /// <param name="ndrReason">Why an attempt failed, when one did.</param>
    /// <param name="occurredAt">When the courier says it happened.</param>
    public CourierScan Scan(
        string awb,
        ShipmentStatus status,
        NdrReasonCode? ndrReason = null,
        DateTimeOffset? occurredAt = null)
    {
        var scan = new CourierScan(
            // Globally unique, not merely unique within this instance: the webhook receiver's
            // replay protection is keyed on (provider, provider_event_id) across the whole shared
            // tenant, and every test gets its own FakeShippingProvider whose sequence restarts at
            // 1 — "evt_2" from one test is "evt_2" from every other, and the second test's own
            // event is silently treated as a duplicate of the first's before its signature is ever
            // checked.
            $"evt_{Guid.NewGuid():N}",
            status,
            status.ToString().ToUpperInvariant(),
            "Hyderabad Hub",
            Remark: null,
            ndrReason,
            occurredAt ?? DateTimeOffset.UtcNow,
            Raw: null);

        _scans.AddOrUpdate(
            awb,
            _ => [scan],
            (_, existing) =>
            {
                lock (existing)
                {
                    existing.Add(scan);
                }

                return existing;
            });

        return scan;
    }

    /// <summary>The webhook body this courier would have posted for a scan.</summary>
    /// <param name="awb">The consignment.</param>
    /// <param name="scan">The scan to report.</param>
    public string WebhookBody(string awb, CourierScan scan)
    {
        ArgumentNullException.ThrowIfNull(scan);

        return JsonSerializer.Serialize(new
        {
            id = scan.ProviderEventId,
            @event = "shipment.status",
            awb,
            shipment_id = _shipmentIdByAwb.TryGetValue(awb, out var id) ? id.ToString() : null,
            status = (int)scan.Status,
            courier_status = scan.CourierStatus,
            location = scan.Location,
            ndr_reason = scan.NdrReason is null ? null : (int?)scan.NdrReason,
            occurred_at = scan.OccurredAt.ToUnixTimeSeconds(),
        });
    }

    /// <inheritdoc />
    public Task<Result<byte[]>> GenerateLabelAsync(
        string? providerShipmentId,
        string awb,
        CancellationToken cancellationToken = default)
        => Task.FromResult(IsConfigured
            ? Result.Success("%PDF-1.4 fake label"u8.ToArray())
            : Result.Failure<byte[]>(ShippingErrors.ProviderUnavailable));

    /// <inheritdoc />
    public Task<Result<CourierManifest>> GenerateManifestAsync(
        IReadOnlyList<string> awbs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(awbs);

        lock (Manifests)
        {
            Manifests.Add(awbs);
        }

        return Task.FromResult(Result.Success(new CourierManifest(
            $"mnf_{Next()}",
            "https://manifests.example.test/sheet.pdf")));
    }

    /// <inheritdoc />
    public Task<Result<DateTimeOffset>> SchedulePickupAsync(
        string? providerShipmentId,
        string awb,
        DateTimeOffset pickupAt,
        CancellationToken cancellationToken = default)
    {
        Pickups[awb] = pickupAt;
        return Task.FromResult(Result.Success(pickupAt));
    }

    /// <inheritdoc />
    public Task<Result> CancelShipmentAsync(
        string? providerShipmentId,
        string awb,
        CancellationToken cancellationToken = default)
    {
        lock (Cancellations)
        {
            Cancellations.Add(awb);
        }

        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc />
    public Task<Result<CourierTracking>> TrackAsync(string awb, CancellationToken cancellationToken = default)
    {
        if (!_bookings.TryGetValue(awb, out var booking))
        {
            return Task.FromResult(Result.Failure<CourierTracking>(ShippingErrors.ProviderUnavailable));
        }

        IReadOnlyList<CourierScan> scans = [];

        if (_scans.TryGetValue(awb, out var recorded))
        {
            lock (recorded)
            {
                scans = [.. recorded.OrderBy(scan => scan.OccurredAt)];
            }
        }

        return Task.FromResult(Result.Success(new CourierTracking(
            awb,
            scans.Count == 0 ? ShipmentStatus.Created : scans[^1].Status,
            ChargedWeightGrams: 1200,
            booking.FreightCost,
            booking.ExpectedDeliveryAt,
            scans)));
    }

    /// <inheritdoc />
    public bool VerifyWebhookSignature(string rawBody, string? signature)
        => CanVerifyWebhooks
           && signature is not null
           && CryptographicOperations.FixedTimeEquals(
               Encoding.UTF8.GetBytes(signature),
               Encoding.UTF8.GetBytes(Sign(rawBody)));

    /// <inheritdoc />
    public Result<CourierWebhookEnvelope> ReadWebhook(string rawBody)
    {
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;

            var awb = root.GetProperty("awb").GetString();
            var occurredAt = DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("occurred_at").GetInt64());

            var scan = new CourierScan(
                root.GetProperty("id").GetString()!,
                (ShipmentStatus)root.GetProperty("status").GetInt32(),
                Optional(root, "courier_status"),
                Optional(root, "location"),
                Remark: null,
                root.TryGetProperty("ndr_reason", out var ndr) && ndr.ValueKind == JsonValueKind.Number
                    ? (NdrReasonCode)ndr.GetInt32()
                    : null,
                occurredAt,
                rawBody);

            return Result.Success(new CourierWebhookEnvelope(
                scan.ProviderEventId,
                root.GetProperty("event").GetString()!,
                awb,
                Optional(root, "shipment_id") is { } id ? Guid.Parse(id, CultureInfo.InvariantCulture) : null,
                occurredAt,
                [scan]));
        }
        catch (JsonException exception)
        {
            return Result.Failure<CourierWebhookEnvelope>(ShippingErrors.ProviderFailed(exception.Message));
        }
    }

    /// <summary>The signature this courier would have put on a body.</summary>
    /// <param name="payload">The bytes as they would be sent.</param>
    public static string Sign(string payload)
        => Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(WebhookSecret),
            Encoding.UTF8.GetBytes(payload)));

    private static string? Optional(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int Next() => Interlocked.Increment(ref _sequence);
}
