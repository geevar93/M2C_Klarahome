using System.Text.Json.Serialization;
using KlaraHome.Modules.Shipping.Domain;

namespace KlaraHome.Modules.Shipping.Infrastructure.Courier.Shiprocket;

/// <summary>
/// The wire shapes of the Shiprocket API (docs/08-integrations.md §2.2).
/// </summary>
/// <remarks>
/// <para>
/// Shiprocket is the v1 courier (ADR-018): one integration, many carriers, automatic carrier
/// selection, cash on delivery supported. The property names are theirs, which is why they are
/// snake-cased and mapped explicitly. <b>The paths are documented rather than proved</b> — no
/// account exists yet, and they are to be confirmed against Shiprocket's current documentation
/// when one does.
/// </para>
/// <para>
/// Only the fields this platform is willing to keep are declared. The aggregator returns a great
/// deal more about a consignment and its recipient, and the way to guarantee none of it is ever
/// written is to have nowhere for it to land.
/// </para>
/// </remarks>
internal static class ShiprocketRoutes
{
    /// <summary>
    /// The prefix every call carries.
    /// </summary>
    /// <remarks>
    /// Written here rather than expected in <c>Shipping:BaseUrl</c>, because the base URL is also
    /// the outbound allow-list and an operator pasting a path into it must not be able to change
    /// which endpoints this adapter calls. The adapter normalises the configured value to its
    /// origin and these paths are then authoritative.
    /// </remarks>
    public const string Prefix = "v1/external/";

    /// <summary>Exchanges an API user's credentials for a bearer token.</summary>
    public const string Login = Prefix + "auth/login";

    /// <summary>Asks which couriers serve a route, and at what price.</summary>
    public const string Serviceability = Prefix + "courier/serviceability/";

    /// <summary>What city and state a PIN code is. Open: it needs no token.</summary>
    public const string PostcodeDetails = Prefix + "open/postcode/details";

    /// <summary>Registers the consignment.</summary>
    public const string CreateOrder = Prefix + "orders/create/adhoc";

    /// <summary>Assigns an air waybill, choosing the courier.</summary>
    public const string AssignAwb = Prefix + "courier/assign/awb";

    /// <summary>Produces the courier's own label.</summary>
    public const string GenerateLabel = Prefix + "courier/generate/label";

    /// <summary>Asks the courier to collect.</summary>
    public const string SchedulePickup = Prefix + "courier/generate/pickup";

    /// <summary>Produces the handover sheet.</summary>
    public const string GenerateManifest = Prefix + "manifests/generate";

    /// <summary>Cancels a consignment the courier has not collected.</summary>
    public const string CancelOrder = Prefix + "orders/cancel";

    /// <summary>Reads everything the courier currently says about one air waybill.</summary>
    public const string Track = Prefix + "courier/track/awb/";
}

/// <summary>What a PIN code turns out to be, in Shiprocket's postcode lookup.</summary>
internal sealed class ShiprocketPostcodeDetails
{
    /// <summary>The city or town.</summary>
    public string? City { get; set; }

    /// <summary>The state.</summary>
    public string? State { get; set; }

    /// <summary>The PIN code itself, echoed back.</summary>
    public string? Postcode { get; set; }
}

/// <summary>The postcode-lookup response.</summary>
internal sealed class ShiprocketPostcodeResponse
{
    /// <summary>The body, where the lookup found one.</summary>
    [JsonPropertyName("postcode_details")]
    public ShiprocketPostcodeDetails? Details { get; set; }
}

/// <summary>The credentials exchanged for a bearer token.</summary>
/// <param name="Email">The API user.</param>
/// <param name="Password">Its secret.</param>
internal sealed record ShiprocketLoginRequest(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("password")] string Password);

/// <summary>What the token endpoint returns.</summary>
internal sealed class ShiprocketLoginResponse
{
    /// <summary>The bearer token.</summary>
    public string? Token { get; set; }
}

/// <summary>One courier's answer about a route.</summary>
internal sealed class ShiprocketCourierOption
{
    /// <summary>The courier's name.</summary>
    [JsonPropertyName("courier_name")]
    public string? CourierName { get; set; }

    /// <summary>Whether cash can be collected on this route. Reported as 0 or 1.</summary>
    public int Cod { get; set; }

    /// <summary>Days in transit, as the courier estimates.</summary>
    [JsonPropertyName("estimated_delivery_days")]
    public string? EstimatedDeliveryDays { get; set; }

    /// <summary>What the aggregator would charge, in rupees.</summary>
    public decimal? Rate { get; set; }

    /// <summary>Whether the courier will take a reverse pickup on this route.</summary>
    [JsonPropertyName("pickup_availability")]
    public string? PickupAvailability { get; set; }
}

/// <summary>The body of a serviceability answer.</summary>
internal sealed class ShiprocketServiceabilityData
{
    /// <summary>The couriers that serve the route.</summary>
    [JsonPropertyName("available_courier_companies")]
    public IReadOnlyList<ShiprocketCourierOption>? Couriers { get; set; }
}

/// <summary>A serviceability answer.</summary>
internal sealed class ShiprocketServiceabilityResponse
{
    /// <summary>The body.</summary>
    public ShiprocketServiceabilityData? Data { get; set; }

    /// <summary>Whether the route is served at all, where the aggregator says so directly.</summary>
    public int Status { get; set; }
}

/// <summary>One line of a consignment, in the aggregator's paperwork.</summary>
internal sealed class ShiprocketOrderItem
{
    /// <summary>What it is called.</summary>
    public string? Name { get; set; }

    /// <summary>The stock-keeping unit.</summary>
    public string? Sku { get; set; }

    /// <summary>How many units.</summary>
    public int Units { get; set; }

    /// <summary>What one unit sells for.</summary>
    [JsonPropertyName("selling_price")]
    public decimal SellingPrice { get; set; }
}

/// <summary>The consignment as it is registered.</summary>
internal sealed class ShiprocketCreateOrderRequest
{
    /// <summary>Our own reference, which is the order number.</summary>
    [JsonPropertyName("order_id")]
    public string OrderId { get; set; } = string.Empty;

    /// <summary>When the order was placed, as the aggregator formats dates.</summary>
    [JsonPropertyName("order_date")]
    public string OrderDate { get; set; } = string.Empty;

    /// <summary>The registered pickup address the courier collects from.</summary>
    [JsonPropertyName("pickup_location")]
    public string PickupLocation { get; set; } = string.Empty;

    /// <summary>Who receives it.</summary>
    [JsonPropertyName("billing_customer_name")]
    public string BillingCustomerName { get; set; } = string.Empty;

    /// <summary>Building and unit.</summary>
    [JsonPropertyName("billing_address")]
    public string BillingAddress { get; set; } = string.Empty;

    /// <summary>Street, area or locality.</summary>
    [JsonPropertyName("billing_address_2")]
    public string? BillingAddress2 { get; set; }

    /// <summary>City or town.</summary>
    [JsonPropertyName("billing_city")]
    public string BillingCity { get; set; } = string.Empty;

    /// <summary>Six-digit PIN code.</summary>
    [JsonPropertyName("billing_pincode")]
    public string BillingPincode { get; set; } = string.Empty;

    /// <summary>The state, spelled the way the aggregator expects.</summary>
    [JsonPropertyName("billing_state")]
    public string? BillingState { get; set; }

    /// <summary>Always India for this deployment.</summary>
    [JsonPropertyName("billing_country")]
    public string BillingCountry { get; set; } = "India";

    /// <summary>The number the courier rings.</summary>
    [JsonPropertyName("billing_phone")]
    public string? BillingPhone { get; set; }

    /// <summary>Whether the delivery address is the billing address. Always, here.</summary>
    [JsonPropertyName("shipping_is_billing")]
    public bool ShippingIsBilling { get; set; } = true;

    /// <summary>What is in it.</summary>
    [JsonPropertyName("order_items")]
    public IReadOnlyList<ShiprocketOrderItem> OrderItems { get; set; } = [];

    /// <summary>Prepaid or cash on delivery, in the aggregator's words.</summary>
    [JsonPropertyName("payment_method")]
    public string PaymentMethod { get; set; } = "Prepaid";

    /// <summary>What the goods are worth.</summary>
    [JsonPropertyName("sub_total")]
    public decimal SubTotal { get; set; }

    /// <summary>Longest side, in centimetres.</summary>
    public decimal Length { get; set; }

    /// <summary>Width, in centimetres.</summary>
    public decimal Breadth { get; set; }

    /// <summary>Height, in centimetres.</summary>
    public decimal Height { get; set; }

    /// <summary>The chargeable weight, in kilograms.</summary>
    public decimal Weight { get; set; }
}

/// <summary>What registering a consignment returns.</summary>
internal sealed class ShiprocketCreateOrderResponse
{
    /// <summary>The aggregator's own order id.</summary>
    [JsonPropertyName("order_id")]
    public long OrderId { get; set; }

    /// <summary>The aggregator's own consignment id, which every later call takes.</summary>
    [JsonPropertyName("shipment_id")]
    public long ShipmentId { get; set; }

    /// <summary>Its status word.</summary>
    public string? Status { get; set; }
}

/// <summary>The air waybill assignment.</summary>
internal sealed class ShiprocketAwbData
{
    /// <summary>The air waybill.</summary>
    [JsonPropertyName("awb_code")]
    public string? AwbCode { get; set; }

    /// <summary>The courier chosen.</summary>
    [JsonPropertyName("courier_name")]
    public string? CourierName { get; set; }

    /// <summary>What it will cost, in rupees.</summary>
    [JsonPropertyName("freight_charges")]
    public decimal? FreightCharges { get; set; }

    /// <summary>When the courier expects to deliver.</summary>
    [JsonPropertyName("etd")]
    public string? EstimatedDelivery { get; set; }
}

/// <summary>The assignment response.</summary>
internal sealed class ShiprocketAwbResponse
{
    /// <summary>The assignment, when it succeeded.</summary>
    [JsonPropertyName("response")]
    public ShiprocketAwbEnvelope? Response { get; set; }
}

/// <summary>The envelope the assignment arrives in.</summary>
internal sealed class ShiprocketAwbEnvelope
{
    /// <summary>The assignment.</summary>
    public ShiprocketAwbData? Data { get; set; }
}

/// <summary>The label response.</summary>
internal sealed class ShiprocketLabelResponse
{
    /// <summary>Where the PDF can be fetched.</summary>
    [JsonPropertyName("label_url")]
    public string? LabelUrl { get; set; }
}

/// <summary>The manifest response.</summary>
internal sealed class ShiprocketManifestResponse
{
    /// <summary>Where the sheet can be fetched.</summary>
    [JsonPropertyName("manifest_url")]
    public string? ManifestUrl { get; set; }

    /// <summary>The aggregator's id for the handover.</summary>
    [JsonPropertyName("manifest_id")]
    public string? ManifestId { get; set; }
}

/// <summary>One scan, as the aggregator reports it.</summary>
internal sealed class ShiprocketActivity
{
    /// <summary>When it happened, in the aggregator's local format.</summary>
    public string? Date { get; set; }

    /// <summary>What the courier wrote.</summary>
    public string? Activity { get; set; }

    /// <summary>Where the scan happened.</summary>
    public string? Location { get; set; }

    /// <summary>The courier's status word.</summary>
    [JsonPropertyName("sr-status-label")]
    public string? StatusLabel { get; set; }
}

/// <summary>The tracking body.</summary>
internal sealed class ShiprocketTrackingData
{
    /// <summary>The air waybill.</summary>
    [JsonPropertyName("awb")]
    public string? Awb { get; set; }

    /// <summary>The courier's current status word.</summary>
    [JsonPropertyName("current_status")]
    public string? CurrentStatus { get; set; }

    /// <summary>What the courier billed for, in kilograms.</summary>
    [JsonPropertyName("charged_weight")]
    public decimal? ChargedWeight { get; set; }

    /// <summary>What they charged, in rupees.</summary>
    [JsonPropertyName("freight_charges")]
    public decimal? FreightCharges { get; set; }

    /// <summary>Their current promise.</summary>
    [JsonPropertyName("edd")]
    public string? ExpectedDelivery { get; set; }

    /// <summary>The scan history.</summary>
    [JsonPropertyName("shipment_track_activities")]
    public IReadOnlyList<ShiprocketActivity>? Activities { get; set; }
}

/// <summary>The tracking response.</summary>
internal sealed class ShiprocketTrackingResponse
{
    /// <summary>The body.</summary>
    [JsonPropertyName("tracking_data")]
    public ShiprocketTrackingData? TrackingData { get; set; }
}

/// <summary>A webhook body.</summary>
internal sealed class ShiprocketWebhookBody
{
    /// <summary>The air waybill it concerns.</summary>
    public string? Awb { get; set; }

    /// <summary>Our own consignment id, echoed back from the reference we sent.</summary>
    [JsonPropertyName("order_id")]
    public string? OrderId { get; set; }

    /// <summary>The courier's status word.</summary>
    [JsonPropertyName("current_status")]
    public string? CurrentStatus { get; set; }

    /// <summary>When the courier says it happened.</summary>
    [JsonPropertyName("current_timestamp")]
    public string? CurrentTimestamp { get; set; }

    /// <summary>Where the scan happened.</summary>
    public string? Location { get; set; }

    /// <summary>What the courier wrote. On a failed attempt this is the reason.</summary>
    [JsonPropertyName("etd")]
    public string? Etd { get; set; }

    /// <summary>The scan history, where the aggregator sends the whole of it.</summary>
    [JsonPropertyName("scans")]
    public IReadOnlyList<ShiprocketActivity>? Scans { get; set; }
}

/// <summary>
/// Translates a courier's own status words into this platform's vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// The single place a courier's language is interpreted, and the reason the rest of the module never
/// sees a string like <c>"OUT FOR DELIVERY"</c>. Every aggregator words these differently and each
/// of them changes the wording occasionally, so the matching is deliberately loose: it looks for the
/// phrase anywhere in the label, lower-cased, rather than demanding an exact value.
/// </para>
/// <para>
/// Anything unrecognised maps to <see cref="ShipmentStatus.InTransit"/> and keeps the courier's own
/// words alongside. That is the honest default — a scan this platform cannot read is still evidence
/// the parcel is moving — and it is safe, because <c>InTransit</c> is never the state anything
/// irreversible hangs off.
/// </para>
/// </remarks>
internal static class ShiprocketStatusMap
{
    /// <summary>What a courier's status word means here.</summary>
    /// <param name="courierStatus">The courier's own word.</param>
    public static ShipmentStatus ToShipmentStatus(string? courierStatus)
    {
        var text = courierStatus?.Trim().ToLowerInvariant() ?? string.Empty;

        // Order matters: "rto delivered" contains "delivered", and the return has to win.
        if (Has(text, "rto delivered", "returned to origin", "rto complete"))
        {
            return ShipmentStatus.RtoDelivered;
        }

        if (Has(text, "rto", "return initiated", "return to origin"))
        {
            return ShipmentStatus.RtoInitiated;
        }

        // Before the delivery check, and that is the whole point of the ordering: "undelivered"
        // contains "delivered", and reading it as an arrival would tell a shopper their parcel came
        // when a courier is standing outside a locked door.
        if (Has(text, "undelivered", "not delivered", "delivery failed", "exception", "ndr"))
        {
            return ShipmentStatus.Exception;
        }

        if (Has(text, "out for delivery", "ofd"))
        {
            return ShipmentStatus.OutForDelivery;
        }

        if (Has(text, "delivered"))
        {
            return ShipmentStatus.Delivered;
        }

        if (Has(text, "picked up", "pickup complete", "collected"))
        {
            return ShipmentStatus.PickedUp;
        }

        if (Has(text, "pickup scheduled", "pickup generated", "pickup booked"))
        {
            return ShipmentStatus.PickupScheduled;
        }

        if (Has(text, "cancel"))
        {
            return ShipmentStatus.Cancelled;
        }

        if (Has(text, "awb assigned", "label", "manifest"))
        {
            return ShipmentStatus.LabelGenerated;
        }

        return ShipmentStatus.InTransit;
    }

    /// <summary>What a courier's failure reason means here.</summary>
    /// <param name="remark">The courier's words.</param>
    public static NdrReasonCode ToNdrReason(string? remark)
    {
        var text = remark?.Trim().ToLowerInvariant() ?? string.Empty;

        if (Has(text, "not available", "unavailable", "customer not", "premises closed", "office closed"))
        {
            return NdrReasonCode.CustomerUnavailable;
        }

        if (Has(text, "incorrect address", "wrong address", "address not found", "incomplete address"))
        {
            return NdrReasonCode.AddressIncorrect;
        }

        if (Has(text, "refused", "rejected by customer", "denied"))
        {
            return NdrReasonCode.Refused;
        }

        if (Has(text, "reschedul", "future delivery", "requested delivery on"))
        {
            return NdrReasonCode.RescheduleRequested;
        }

        if (Has(text, "cod not ready", "cash not ready", "amount not ready", "payment not ready"))
        {
            return NdrReasonCode.CodNotReady;
        }

        if (Has(text, "out of delivery area", "unreachable", "no network", "weather", "strike", "bandh"))
        {
            return NdrReasonCode.Unreachable;
        }

        return NdrReasonCode.Other;
    }

    private static bool Has(string text, params string[] phrases)
        => phrases.Any(phrase => text.Contains(phrase, StringComparison.Ordinal));
}
