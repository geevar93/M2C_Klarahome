using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.Modules.Shipping.Application;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.SharedKernel.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Shipping.Infrastructure.Courier.Aggregator;

/// <summary>
/// The logistics aggregator, spoken to over its own API (docs/08-integrations.md §2).
/// </summary>
/// <remarks>
/// <para>
/// One integration, many couriers, automatic courier selection and cash on delivery — the
/// recommendation the specification makes for v1, and the reason a direct courier contract is not
/// wired here. A later Delhivery or Blue Dart adapter is a second class behind
/// <see cref="IShippingProvider"/> and nothing else changes.
/// </para>
/// <para>
/// Booking is two calls, and that is the aggregator's design rather than ours: the consignment is
/// registered first and an air waybill is assigned second, because the second call is where a
/// courier is chosen. A failure between them leaves a registered consignment with no waybill, which
/// this adapter reports as a plain failure — the caller leaves the parcel in the exception queue,
/// and an operator either retries or books it by hand.
/// </para>
/// <para>
/// The token is fetched lazily and cached in memory, and a single <c>401</c> triggers exactly one
/// re-fetch and one retry. Tokens in this class of API last days, so a deployment that could not
/// refresh would stop booking parcels the week after it was set up; retrying more than once on the
/// other hand would turn a revoked credential into a login storm.
/// </para>
/// <para>
/// Nothing here throws. An aggregator being unreachable is an ordinary outcome on this path — the
/// parcel stays packed and appears in the exception queue — and a failure to reach a courier must
/// never be able to take down the request that discovered it.
/// </para>
/// </remarks>
/// <param name="factory">Supplies the named client with its allow-list handler.</param>
/// <param name="options">Credentials and endpoint, re-read on each call.</param>
/// <param name="logger">Reports what the aggregator refused.</param>
internal sealed partial class AggregatorShippingProvider(
    IHttpClientFactory factory,
    IOptionsMonitor<ShippingOptions> options,
    ILogger<AggregatorShippingProvider> logger) : IShippingProvider, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _token;

    /// <summary>Releases the lock that serialises token refreshes.</summary>
    /// <remarks>
    /// The adapter is a singleton, so this runs at shutdown. It exists because a type that owns a
    /// disposable must dispose it — not because anything here is expected to leak.
    /// </remarks>
    public void Dispose() => _tokenLock.Dispose();

    /// <inheritdoc />
    public string Name => ShippingProviders.Aggregator;

    /// <inheritdoc />
    public bool IsConfigured => options.CurrentValue.HasAggregator;

    /// <inheritdoc />
    public bool CanVerifyWebhooks => options.CurrentValue.CanVerifyWebhooks;

    /// <inheritdoc />
    public async Task<Result<CourierServiceability>> CheckServiceabilityAsync(
        string pincode,
        string? pickupPincode,
        int weightGrams,
        bool isCod,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pincode);

        var query =
            $"{AggregatorRoutes.Serviceability}?delivery_postcode={pincode}"
            + $"&pickup_postcode={pickupPincode ?? pincode}"
            + $"&weight={AggregatorSignature.ToKilograms(Math.Max(weightGrams, 1)).ToString(CultureInfo.InvariantCulture)}"
            + $"&cod={(isCod ? 1 : 0)}";

        var answered = await SendAsync<AggregatorServiceabilityResponse>(
                HttpMethod.Get,
                query,
                body: null,
                cancellationToken)
            .ConfigureAwait(false);

        if (answered.IsFailure)
        {
            return Result.Failure<CourierServiceability>(answered.Error);
        }

        var couriers = answered.Value?.Data?.Couriers ?? [];

        if (couriers.Count == 0)
        {
            // Served by nobody. Recorded as a negative answer rather than a failure: it is a fact
            // about the destination, and caching it is the point of asking.
            return Result.Success(new CourierServiceability(
                Name,
                PrepaidOk: false,
                CodOk: false,
                PickupOk: false,
                EtaDays: null,
                MaxWeightGrams: null));
        }

        // The best of what is on offer, because that is what the destination can actually have: one
        // courier refusing cash does not make the PIN code prepaid-only.
        var eta = couriers
            .Select(courier => ParseDays(courier.EstimatedDeliveryDays))
            .Where(days => days is > 0)
            .DefaultIfEmpty(null)
            .Min();

        return Result.Success(new CourierServiceability(
            couriers[0].CourierName ?? Name,
            PrepaidOk: true,
            CodOk: couriers.Any(courier => courier.Cod == 1),
            PickupOk: couriers.Any(courier =>
                string.Equals(courier.PickupAvailability, "1", StringComparison.Ordinal)
                || string.Equals(courier.PickupAvailability, "yes", StringComparison.OrdinalIgnoreCase)),
            eta,
            MaxWeightGrams: null));
    }

    /// <inheritdoc />
    public async Task<Result<CourierBooking>> CreateShipmentAsync(
        CourierBookingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Pickup.LocationCode))
        {
            // The aggregator books against a pickup address it has registered, by its own name for
            // it. Without that there is nothing to send, and sending the street address instead
            // would book every parcel from an address the courier has never surveyed.
            return Result.Failure<CourierBooking>(ShippingErrors.NoPickupLocation);
        }

        var registration = new AggregatorCreateOrderRequest
        {
            OrderId = request.Reference,
            OrderDate = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            PickupLocation = request.Pickup.LocationCode,
            BillingCustomerName = request.Destination.Name,
            BillingAddress = request.Destination.Line1,
            BillingAddress2 = request.Destination.Line2,
            BillingCity = request.Destination.City,
            BillingPincode = request.Destination.Pincode,
            BillingState = request.Destination.StateName,
            BillingPhone = request.Destination.Phone,
            OrderItems =
            [
                .. request.Items.Select(item => new AggregatorOrderItem
                {
                    Name = item.Name,
                    Sku = item.Sku,
                    Units = item.Quantity,
                    SellingPrice = item.Quantity > 0
                        ? Math.Round(item.DeclaredValue / item.Quantity, 2, MidpointRounding.AwayFromZero)
                        : item.DeclaredValue,
                }),
            ],
            PaymentMethod = request.CodAmount is > 0m ? "COD" : "Prepaid",
            SubTotal = request.DeclaredValue,
            Length = Math.Max(1m, request.Dimensions.LengthCm),
            Breadth = Math.Max(1m, request.Dimensions.WidthCm),
            Height = Math.Max(1m, request.Dimensions.HeightCm),
            Weight = AggregatorSignature.ToKilograms(Math.Max(request.WeightGrams, 1)),
        };

        var registered = await SendAsync<AggregatorCreateOrderResponse>(
                HttpMethod.Post,
                AggregatorRoutes.CreateOrder,
                registration,
                cancellationToken)
            .ConfigureAwait(false);

        if (registered.IsFailure)
        {
            return Result.Failure<CourierBooking>(registered.Error);
        }

        var shipmentId = registered.Value?.ShipmentId ?? 0;

        if (shipmentId <= 0)
        {
            return Result.Failure<CourierBooking>(
                ShippingErrors.ProviderFailed("The courier registered the parcel without returning an id."));
        }

        var assigned = await SendAsync<AggregatorAwbResponse>(
                HttpMethod.Post,
                AggregatorRoutes.AssignAwb,
                new { shipment_id = shipmentId },
                cancellationToken)
            .ConfigureAwait(false);

        if (assigned.IsFailure)
        {
            // Registered and unassigned. Reported plainly so the caller leaves the parcel packed and
            // an operator decides — the aggregator's own dashboard now holds a consignment that this
            // platform will not book twice, because a second attempt sends the same reference.
            RegisteredWithoutAwb(logger, request.Reference, shipmentId);
            return Result.Failure<CourierBooking>(assigned.Error);
        }

        var awb = assigned.Value?.Response?.Data;

        return string.IsNullOrWhiteSpace(awb?.AwbCode)
            ? Result.Failure<CourierBooking>(
                ShippingErrors.ProviderFailed("The courier did not return an air waybill."))
            : Result.Success(new CourierBooking(
                awb.CourierName ?? Name,
                ServiceName: awb.CourierName,
                awb.AwbCode,
                shipmentId.ToString(CultureInfo.InvariantCulture),
                TrackingUrl: null,
                ParseInstant(awb.EstimatedDelivery),
                awb.FreightCharges,
                LabelUrl: null));
    }

    /// <inheritdoc />
    public async Task<Result<byte[]>> GenerateLabelAsync(
        string? providerShipmentId,
        string awb,
        CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(providerShipmentId, CultureInfo.InvariantCulture, out var shipmentId))
        {
            return Result.Failure<byte[]>(ShippingErrors.NotBooked);
        }

        var produced = await SendAsync<AggregatorLabelResponse>(
                HttpMethod.Post,
                AggregatorRoutes.GenerateLabel,
                new { shipment_id = new[] { shipmentId } },
                cancellationToken)
            .ConfigureAwait(false);

        if (produced.IsFailure)
        {
            return Result.Failure<byte[]>(produced.Error);
        }

        var url = produced.Value?.LabelUrl;

        if (string.IsNullOrWhiteSpace(url))
        {
            return Result.Failure<byte[]>(
                ShippingErrors.ProviderFailed("The courier produced no label for that parcel."));
        }

        return await DownloadAsync(url, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<CourierManifest>> GenerateManifestAsync(
        IReadOnlyList<string> awbs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(awbs);

        var produced = await SendAsync<AggregatorManifestResponse>(
                HttpMethod.Post,
                AggregatorRoutes.GenerateManifest,
                new { awbs },
                cancellationToken)
            .ConfigureAwait(false);

        return produced.IsFailure
            ? Result.Failure<CourierManifest>(produced.Error)
            : Result.Success(new CourierManifest(produced.Value?.ManifestId, produced.Value?.ManifestUrl));
    }

    /// <inheritdoc />
    public async Task<Result<DateTimeOffset>> SchedulePickupAsync(
        string? providerShipmentId,
        string awb,
        DateTimeOffset pickupAt,
        CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(providerShipmentId, CultureInfo.InvariantCulture, out var shipmentId))
        {
            return Result.Failure<DateTimeOffset>(ShippingErrors.NotBooked);
        }

        var scheduled = await SendAsync<JsonElement>(
                HttpMethod.Post,
                AggregatorRoutes.SchedulePickup,
                new
                {
                    shipment_id = new[] { shipmentId },
                    pickup_date = new[] { pickupAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                },
                cancellationToken)
            .ConfigureAwait(false);

        return scheduled.IsFailure
            ? Result.Failure<DateTimeOffset>(scheduled.Error)
            : Result.Success(pickupAt);
    }

    /// <inheritdoc />
    public async Task<Result> CancelShipmentAsync(
        string? providerShipmentId,
        string awb,
        CancellationToken cancellationToken = default)
    {
        var cancelled = await SendAsync<JsonElement>(
                HttpMethod.Post,
                AggregatorRoutes.CancelOrder,
                new { awbs = new[] { awb } },
                cancellationToken)
            .ConfigureAwait(false);

        return cancelled.IsFailure ? Result.Failure(cancelled.Error) : Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<CourierTracking>> TrackAsync(
        string awb,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(awb);

        var tracked = await SendAsync<AggregatorTrackingResponse>(
                HttpMethod.Get,
                AggregatorRoutes.Track + Uri.EscapeDataString(awb),
                body: null,
                cancellationToken)
            .ConfigureAwait(false);

        if (tracked.IsFailure)
        {
            return Result.Failure<CourierTracking>(tracked.Error);
        }

        var data = tracked.Value?.TrackingData;

        if (data is null)
        {
            return Result.Failure<CourierTracking>(
                ShippingErrors.ProviderFailed("The courier has no record of that air waybill."));
        }

        return Result.Success(new CourierTracking(
            data.Awb ?? awb,
            AggregatorStatusMap.ToShipmentStatus(data.CurrentStatus),
            AggregatorSignature.ToGrams(data.ChargedWeight),
            data.FreightCharges,
            ParseInstant(data.ExpectedDelivery),
            [.. (data.Activities ?? []).Select(ToScan).OrderBy(scan => scan.OccurredAt)]));
    }

    /// <inheritdoc />
    public bool VerifyWebhookSignature(string rawBody, string? signature)
        => AggregatorSignature.Verify(rawBody, signature, options.CurrentValue.WebhookSecret);

    /// <inheritdoc />
    public Result<CourierWebhookEnvelope> ReadWebhook(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return Result.Failure<CourierWebhookEnvelope>(
                ShippingErrors.ProviderFailed("The webhook body was empty."));
        }

        AggregatorWebhookBody? body;

        try
        {
            body = JsonSerializer.Deserialize<AggregatorWebhookBody>(rawBody, Json);
        }
        catch (JsonException exception)
        {
            return Result.Failure<CourierWebhookEnvelope>(ShippingErrors.ProviderFailed(exception.Message));
        }

        if (body is null || string.IsNullOrWhiteSpace(body.Awb))
        {
            return Result.Failure<CourierWebhookEnvelope>(
                ShippingErrors.ProviderFailed("The webhook named no air waybill."));
        }

        var occurredAt = ParseInstant(body.CurrentTimestamp) ?? DateTimeOffset.UtcNow;

        // The payload carries either a whole history or a single current state. Both are normalised
        // to a list of scans here, so nothing downstream has to know which arrived.
        var scans = body.Scans is { Count: > 0 }
            ? body.Scans.Select(ToScan).OrderBy(scan => scan.OccurredAt).ToArray()
            :
            [
                new CourierScan(
                    TrackingEvent.SyntheticId(body.CurrentStatus, occurredAt),
                    AggregatorStatusMap.ToShipmentStatus(body.CurrentStatus),
                    body.CurrentStatus,
                    body.Location,
                    body.Etd,
                    AggregatorStatusMap.ToShipmentStatus(body.CurrentStatus) == ShipmentStatus.Exception
                        ? AggregatorStatusMap.ToNdrReason(body.Etd ?? body.CurrentStatus)
                        : null,
                    occurredAt,
                    rawBody),
            ];

        return Result.Success(new CourierWebhookEnvelope(
            $"{body.Awb}:{scans[^1].ProviderEventId}",
            body.CurrentStatus ?? "tracking.update",
            body.Awb,
            ShipmentId: null,
            occurredAt,
            scans));
    }

    private static CourierScan ToScan(AggregatorActivity activity)
    {
        var occurredAt = ParseInstant(activity.Date) ?? DateTimeOffset.UtcNow;
        var courierStatus = activity.StatusLabel ?? activity.Activity;
        var status = AggregatorStatusMap.ToShipmentStatus(courierStatus);

        return new CourierScan(
            TrackingEvent.SyntheticId(courierStatus, occurredAt),
            status,
            courierStatus,
            activity.Location,
            activity.Activity,
            status == ShipmentStatus.Exception
                ? AggregatorStatusMap.ToNdrReason(activity.Activity ?? courierStatus)
                : null,
            occurredAt,
            Raw: null);
    }

    private static int? ParseDays(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Aggregators write these as "3", "3 Days" or "2-4 days". The first number is the answer.
        var digits = new string([.. value.TakeWhile(char.IsDigit)]);

        return int.TryParse(digits, CultureInfo.InvariantCulture, out var days) && days > 0 ? days : null;
    }

    private static DateTimeOffset? ParseInstant(string? value)
        => DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// Makes one authenticated call, refreshing the token once if it is refused.
    /// </summary>
    /// <remarks>
    /// Exactly one retry. A revoked credential retried in a loop is a login storm against somebody
    /// else's service, and an expired one needs precisely one refresh.
    /// </remarks>
    private async Task<Result<TResponse?>> SendAsync<TResponse>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return Result.Failure<TResponse?>(ShippingErrors.ProviderUnavailable);
        }

        var first = await AttemptAsync<TResponse>(method, path, body, allowRefresh: true, cancellationToken)
            .ConfigureAwait(false);

        return first;
    }

    private async Task<Result<TResponse?>> AttemptAsync<TResponse>(
        HttpMethod method,
        string path,
        object? body,
        bool allowRefresh,
        CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        var token = await TokenAsync(settings, forceRefresh: false, cancellationToken).ConfigureAwait(false);

        if (token.IsFailure)
        {
            return Result.Failure<TResponse?>(token.Error);
        }

        using var client = Client(settings);
        using var request = new HttpRequestMessage(method, path);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        try
        {
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden && allowRefresh)
            {
                await TokenAsync(settings, forceRefresh: true, cancellationToken).ConfigureAwait(false);

                return await AttemptAsync<TResponse>(method, path, body, allowRefresh: false, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                CallFailed(logger, method.Method, path, (int)response.StatusCode, Clip(detail));

                return Result.Failure<TResponse?>(
                    ShippingErrors.ProviderFailed($"The courier answered {(int)response.StatusCode}."));
            }

            var value = await response.Content
                .ReadFromJsonAsync<TResponse>(Json, cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            CallThrew(logger, method.Method, path, exception);

            return Result.Failure<TResponse?>(ShippingErrors.ProviderFailed());
        }
    }

    /// <summary>The bearer token, fetched once and reused until it is refused.</summary>
    private async Task<Result<string>> TokenAsync(
        ShippingOptions settings,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        // A static token configured by an operator is used as it stands. There is nothing to refresh
        // and nothing to store.
        if (!string.IsNullOrWhiteSpace(settings.ApiKey) && string.IsNullOrWhiteSpace(settings.ApiUser))
        {
            return Result.Success(settings.ApiKey);
        }

        if (!forceRefresh && _token is { Length: > 0 } cached)
        {
            return Result.Success(cached);
        }

        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!forceRefresh && _token is { Length: > 0 } current)
            {
                return Result.Success(current);
            }

            if (string.IsNullOrWhiteSpace(settings.ApiUser) || string.IsNullOrWhiteSpace(settings.ApiSecret))
            {
                return string.IsNullOrWhiteSpace(settings.ApiKey)
                    ? Result.Failure<string>(ShippingErrors.ProviderUnavailable)
                    : Result.Success(settings.ApiKey);
            }

            using var client = Client(settings);

            using var response = await client
                .PostAsJsonAsync(
                    AggregatorRoutes.Login,
                    new AggregatorLoginRequest(settings.ApiUser, settings.ApiSecret),
                    Json,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LoginFailed(logger, (int)response.StatusCode);

                return Result.Failure<string>(
                    ShippingErrors.ProviderFailed("The courier refused this deployment's credentials."));
            }

            var issued = await response.Content
                .ReadFromJsonAsync<AggregatorLoginResponse>(Json, cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(issued?.Token))
            {
                return Result.Failure<string>(
                    ShippingErrors.ProviderFailed("The courier issued no token."));
            }

            _token = issued.Token;

            return Result.Success(issued.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            CallThrew(logger, "POST", AggregatorRoutes.Login, exception);

            return Result.Failure<string>(ShippingErrors.ProviderFailed());
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>
    /// Fetches a document the aggregator published, through the same allow-listed client.
    /// </summary>
    /// <remarks>
    /// Through the allow-list on purpose. A label URL is a value from a third party, and following
    /// it without a check would be a server-side request forgery with this platform's own network
    /// position behind it (docs/07-security-compliance.md §3).
    /// </remarks>
    private async Task<Result<byte[]>> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return Result.Failure<byte[]>(
                ShippingErrors.ProviderFailed("The courier returned a label link that could not be used."));
        }

        try
        {
            using var client = Client(options.CurrentValue);

            var bytes = await client.GetByteArrayAsync(uri, cancellationToken).ConfigureAwait(false);

            return bytes.Length == 0
                ? Result.Failure<byte[]>(ShippingErrors.ProviderFailed("The courier returned an empty label."))
                : Result.Success(bytes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            CallThrew(logger, "GET", "label", exception);

            return Result.Failure<byte[]>(ShippingErrors.ProviderFailed());
        }
    }

    private HttpClient Client(ShippingOptions settings)
    {
        var client = factory.CreateClient(AggregatorHttp.ClientName);

        client.BaseAddress = new Uri(settings.BaseUrl, UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);

        return client;
    }

    private static string Clip(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Length <= 500 ? value : value[..500];

    [LoggerMessage(EventId = 1721, Level = LogLevel.Warning,
        Message = "The logistics aggregator answered {Status} to {Method} {Path}: {Detail}")]
    private static partial void CallFailed(
        ILogger logger,
        string method,
        string path,
        int status,
        string detail);

    [LoggerMessage(EventId = 1722, Level = LogLevel.Warning,
        Message = "A logistics call to {Method} {Path} could not be completed.")]
    private static partial void CallThrew(ILogger logger, string method, string path, Exception exception);

    [LoggerMessage(EventId = 1723, Level = LogLevel.Error,
        Message = "The logistics aggregator refused this deployment's credentials with {Status}. "
                  + "Parcels will be booked by hand until it is fixed.")]
    private static partial void LoginFailed(ILogger logger, int status);

    [LoggerMessage(EventId = 1724, Level = LogLevel.Error,
        Message = "Parcel {Reference} was registered with the aggregator as consignment {ShipmentId} but no "
                  + "air waybill was assigned. It is in the exception queue.")]
    private static partial void RegisteredWithoutAwb(ILogger logger, string reference, long shipmentId);
}
