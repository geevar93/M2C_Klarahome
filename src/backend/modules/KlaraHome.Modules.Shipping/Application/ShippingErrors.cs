using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Modules.Shipping.Application;

/// <summary>
/// Every failure this module reports, with the stable code the frontend switches on
/// (docs/04-api-specification.md §1.2).
/// </summary>
/// <remarks>
/// Declared in one place so two handlers cannot answer the same situation with two different codes.
/// Several of these are read by a packer with a parcel in their hands and a queue behind them, so
/// the wording says what to do rather than what went wrong.
/// </remarks>
internal static class ShippingErrors
{
    /// <summary>The zone, rate, consignment, report or manifest does not exist, or is not this caller's.</summary>
    /// <remarks>
    /// One code for both cases, on purpose. Telling a seller that another seller's consignment id is
    /// real is a disclosure, and 404 is what docs/04-api-specification.md §1.2 requires for "not
    /// visible to this caller".
    /// </remarks>
    /// <param name="what">What was being looked for, in words a caller can read.</param>
    public static Error NotFound(string what)
        => Error.NotFound("SHIPPING_NOT_FOUND", $"That {what} does not exist.");

    /// <summary>No courier integration is usable, and none was needed to be.</summary>
    /// <remarks>
    /// The state a deployment with no aggregator account is in. It is deliberately not fatal
    /// anywhere: dispatch falls back to a hand-typed air waybill, and only the calls that genuinely
    /// need a courier API — fetching their label, polling their tracking — answer this.
    /// </remarks>
    public static Error ProviderUnavailable { get; } =
        Error.Unavailable(
            "SHIPPING_PROVIDER_UNAVAILABLE",
            "No logistics provider is configured on this deployment.");

    /// <summary>The aggregator did not answer, or answered with a failure.</summary>
    /// <param name="detail">What went wrong, in words safe to show an operator.</param>
    public static Error ProviderFailed(string? detail = null)
        => Error.Unavailable(
            "SHIPPING_PROVIDER_FAILED",
            detail is { Length: > 0 }
                ? detail
                : "The courier could not be reached. The parcel is unchanged; please try again or book it by hand.");

    /// <summary>A hand-booking was asked for without the air waybill the courier gave.</summary>
    public static Error ManualAwbRequired { get; } =
        Error.Validation(
            "SHIPPING_MANUAL_AWB_REQUIRED",
            "Enter the air waybill number the courier gave you.");

    /// <summary>The seller's part is not in a state that can be dispatched.</summary>
    /// <param name="status">Where it actually is.</param>
    public static Error NotDispatchable(string status)
        => Error.Conflict(
            "SHIPPING_SUB_ORDER_NOT_DISPATCHABLE",
            $"That order is {status} and cannot be dispatched.");

    /// <summary>The consignment already has an air waybill.</summary>
    public static Error AlreadyBooked { get; } =
        Error.Conflict("SHIPMENT_ALREADY_BOOKED", "That parcel has already been booked with a courier.");

    /// <summary>The consignment has not been booked, and the operation needs a courier.</summary>
    public static Error NotBooked { get; } =
        Error.Conflict("SHIPMENT_NOT_BOOKED", "That parcel has not been booked with a courier yet.");

    /// <summary>The consignment cannot move the way it was asked to.</summary>
    /// <param name="from">Where it is.</param>
    /// <param name="to">Where it was asked to go.</param>
    public static Error InvalidTransition(object from, object to)
        => Error.Conflict(
            "SHIPMENT_INVALID_TRANSITION",
            $"A parcel that is {from} cannot become {to}.");

    /// <summary>Nothing was packed into the consignment.</summary>
    public static Error NothingToShip { get; } =
        Error.Validation("SHIPMENT_NOTHING_TO_SHIP", "Add at least one item to the parcel.");

    /// <summary>More units were packed than the order has left to send.</summary>
    /// <param name="sku">Which item.</param>
    /// <param name="remaining">How many are still unsent.</param>
    public static Error TooManyUnits(string sku, int remaining)
        => Error.Validation(
            "SHIPMENT_TOO_MANY_UNITS",
            $"Only {remaining} of {sku} are still to be sent.");

    /// <summary>Nobody weighed the parcel, and a courier will not take one unweighed.</summary>
    public static Error WeightRequired { get; } =
        Error.Validation("SHIPMENT_WEIGHT_REQUIRED", "Weigh the parcel before booking it.");

    /// <summary>The seller has no address a courier can collect from.</summary>
    public static Error NoPickupLocation { get; } =
        Error.Conflict(
            "SHIPPING_NO_PICKUP_LOCATION",
            "This seller has no active pickup address. Add one before dispatching.");

    /// <summary>Nothing in this platform can price a parcel to that destination.</summary>
    /// <remarks>
    /// A rate card with a hole in it rather than a courier refusing: the two are different problems
    /// and an operator needs to know which. This one is fixed by adding a zone or a rate, and the
    /// message says so.
    /// </remarks>
    public static Error NoRate { get; } =
        Error.Conflict(
            "SHIPPING_NO_RATE",
            "No shipping rate covers that destination and weight. Add a rate rule for it.");

    /// <summary>The courier will not deliver there at all.</summary>
    public static Error NotServiceable { get; } =
        Error.Conflict("SHIPPING_NOT_SERVICEABLE", "We cannot deliver to that PIN code yet.");

    /// <summary>The label has not been produced, so there is nothing to print.</summary>
    public static Error LabelMissing { get; } =
        Error.NotFound("SHIPMENT_LABEL_MISSING", "That parcel has no label to print yet.");

    /// <summary>The failed-delivery report has already been worked.</summary>
    public static Error NdrNotOpen { get; } =
        Error.Conflict("SHIPPING_NDR_NOT_OPEN", "That delivery report has already been actioned.");

    /// <summary>The courier event is not in a state that can be replayed.</summary>
    public static Error EventNotReplayable { get; } =
        Error.Conflict(
            "COURIER_EVENT_NOT_REPLAYABLE",
            "Only a failed or dead-lettered event can be replayed.");

    /// <summary>A zone or rate was described in a way that cannot be satisfied.</summary>
    /// <param name="detail">What is wrong with it.</param>
    public static Error InvalidRule(string detail)
        => Error.Validation("SHIPPING_INVALID_RULE", detail);

    /// <summary>The zone code is already used by another zone.</summary>
    /// <param name="code">The code.</param>
    public static Error DuplicateZone(string code)
        => Error.Conflict("SHIPPING_ZONE_DUPLICATE", $"A zone with the code '{code}' already exists.");

    /// <summary>The order this consignment belongs to could not be read.</summary>
    /// <param name="detail">What the ordering module said.</param>
    public static Error OrderUnavailable(string? detail = null)
        => Error.Unavailable(
            "SHIPPING_ORDER_UNAVAILABLE",
            detail is { Length: > 0 } ? detail : "The order could not be read.");
}
