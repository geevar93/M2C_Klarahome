using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Shipping.Domain;

/// <summary>Where a stored courier webhook has got to.</summary>
internal enum CourierEventStatus
{
    /// <summary>Waiting for the worker.</summary>
    Pending = 0,

    /// <summary>Applied. The scans it carried are on their parcels.</summary>
    Processed = 10,

    /// <summary>Read and deliberately not acted on — a stale replay, or a type nobody handles.</summary>
    Ignored = 20,

    /// <summary>An attempt failed and there are attempts left.</summary>
    Failed = 30,

    /// <summary>Out of attempts. This is the dead-letter queue, and it waits for a human.</summary>
    DeadLettered = 40,
}

/// <summary>
/// One courier webhook, exactly as it arrived (docs/04-api-specification.md §5,
/// docs/08-integrations.md §2).
/// </summary>
/// <remarks>
/// <para>
/// The same table doing the same three jobs as the payments gateway log, because a courier webhook
/// has the same three problems. Its unique <see cref="ProviderEventId"/> is the <b>replay
/// protection</b>; its <see cref="Payload"/> is the <b>evidence</b>, written before any processing,
/// so a dispute about what the courier said has an answer; and its <see cref="Status"/> is the
/// <b>dead-letter queue</b>.
/// </para>
/// <para>
/// The endpoint that writes these does nothing else. It verifies, stores and answers <c>200</c> in
/// milliseconds, and the worker applies what was stored — which is what keeps an aggregator from
/// timing out and redelivering while a transaction moves an order and notifies a shopper.
/// </para>
/// <para>
/// Nothing here is deleted and the payload is never rewritten. An unprocessable event is not rubbish
/// to be swept up; it is the record of a parcel that moved and a platform that did not notice.
/// </para>
/// </remarks>
internal sealed class CourierEvent : Entity<Guid>, ITenantScoped
{
    private CourierEvent(
        Guid id,
        string provider,
        string providerEventId,
        string eventType,
        bool signatureValid,
        string payload,
        DateTimeOffset receivedAt)
        : base(id)
    {
        Provider = provider;
        ProviderEventId = providerEventId;
        EventType = eventType;
        SignatureValid = signatureValid;
        Payload = payload;
        ReceivedAt = receivedAt;
        Status = CourierEventStatus.Pending;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private CourierEvent()
    {
        Provider = string.Empty;
        ProviderEventId = string.Empty;
        EventType = string.Empty;
        Payload = "{}";
    }

    /// <summary>Which aggregator sent it.</summary>
    public string Provider { get; private set; }

    /// <summary>Its own id for the event. Unique per provider — this is the replay protection.</summary>
    public string ProviderEventId { get; private set; }

    /// <summary>What happened, in the courier's vocabulary.</summary>
    public string EventType { get; private set; }

    /// <summary>Whether the signature over the raw body verified. Recorded, never inferred later.</summary>
    public bool SignatureValid { get; private set; }

    /// <summary>The raw body. Written once, before processing, and never rewritten.</summary>
    public string Payload { get; private set; }

    /// <summary>The air waybill the payload named, so an operator can find it without parsing JSON.</summary>
    public string? Awb { get; private set; }

    /// <summary>When this platform received it.</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>When the courier says it happened, where the payload carries that.</summary>
    public DateTimeOffset? OccurredAt { get; private set; }

    /// <summary>Where processing stands.</summary>
    public CourierEventStatus Status { get; private set; }

    /// <summary>When it was successfully applied.</summary>
    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>Why the last attempt failed.</summary>
    public string? ProcessError { get; private set; }

    /// <summary>How many times processing has been tried.</summary>
    public int Attempts { get; private set; }

    /// <summary>When the worker should try again. Null when it is not waiting.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <summary>The parcel it turned out to concern, resolved during processing.</summary>
    public Guid? ShipmentId { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Stores a webhook.</summary>
    /// <param name="provider">Which aggregator sent it.</param>
    /// <param name="providerEventId">Its own id for the event.</param>
    /// <param name="eventType">What happened.</param>
    /// <param name="signatureValid">Whether the signature verified.</param>
    /// <param name="payload">The raw body.</param>
    /// <param name="awb">The air waybill it named, where it named one.</param>
    /// <param name="occurredAt">When the courier says it happened.</param>
    /// <param name="receivedAt">When we received it.</param>
    public static CourierEvent Receive(
        string provider,
        string providerEventId,
        string eventType,
        bool signatureValid,
        string payload,
        string? awb,
        DateTimeOffset? occurredAt,
        DateTimeOffset receivedAt)
        => new(
            UuidV7.NewAt(receivedAt),
            Guard.NotNullOrWhiteSpace(provider),
            Guard.MaxLength(Guard.NotNullOrWhiteSpace(providerEventId), 128),
            Guard.MaxLength(Guard.NotNullOrWhiteSpace(eventType), 64),
            signatureValid,
            Guard.NotNullOrWhiteSpace(payload),
            receivedAt)
        {
            Awb = awb,
            OccurredAt = occurredAt,
        };

    /// <summary>Records that the event was applied.</summary>
    /// <param name="shipmentId">The parcel it concerned, when one was resolved.</param>
    /// <param name="at">When.</param>
    public void MarkProcessed(Guid? shipmentId, DateTimeOffset at)
    {
        Status = CourierEventStatus.Processed;
        ShipmentId ??= shipmentId;
        ProcessedAt = at;
        ProcessError = null;
        NextAttemptAt = null;
        Attempts++;
    }

    /// <summary>Records that this platform will not act on the event.</summary>
    /// <param name="at">When it was read.</param>
    public void MarkIgnored(DateTimeOffset at)
    {
        Status = CourierEventStatus.Ignored;
        ProcessedAt = at;
        NextAttemptAt = null;
    }

    /// <summary>
    /// Records a failed attempt, and dead-letters the event once the budget is spent.
    /// </summary>
    /// <param name="error">What went wrong.</param>
    /// <param name="maxAttempts">How many tries the event gets in total.</param>
    /// <param name="nextAttemptAt">When to try again, when there is a try left.</param>
    /// <returns>Whether this failure is the one that dead-lettered it.</returns>
    public bool MarkFailed(string error, int maxAttempts, DateTimeOffset nextAttemptAt)
    {
        Attempts++;
        ProcessError = string.IsNullOrWhiteSpace(error)
            ? "Processing failed."
            : error.Length <= 1000 ? error : error[..1000];

        if (Attempts >= maxAttempts)
        {
            Status = CourierEventStatus.DeadLettered;
            NextAttemptAt = null;
            return true;
        }

        Status = CourierEventStatus.Failed;
        NextAttemptAt = nextAttemptAt;

        return false;
    }

    /// <summary>
    /// Puts a failed or dead-lettered event back in the queue, at an operator's request.
    /// </summary>
    /// <remarks>
    /// The attempt counter is reset, because a replay is a decision by somebody who has read the
    /// payload rather than another automatic try. What is not reset is the payload or the signature
    /// verdict: an event whose signature never verified must never become processable by being
    /// replayed.
    /// </remarks>
    /// <param name="at">The current instant.</param>
    /// <returns>Whether the event was in a state that could be replayed.</returns>
    public bool Replay(DateTimeOffset at)
    {
        if (Status is not (CourierEventStatus.Failed or CourierEventStatus.DeadLettered))
        {
            return false;
        }

        Status = CourierEventStatus.Pending;
        Attempts = 0;
        NextAttemptAt = at;
        ProcessError = null;

        return true;
    }
}
