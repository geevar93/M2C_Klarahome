using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Payments.Domain;

/// <summary>
/// One webhook, exactly as it arrived (docs/03-database-design.md §4.9,
/// docs/04-api-specification.md §5).
/// </summary>
/// <remarks>
/// <para>
/// This table is three things at once and they fit together. Its unique
/// <see cref="ProviderEventId"/> is the <b>replay protection</b> — a redelivered webhook collides
/// and becomes a no-op. Its <see cref="Payload"/> is the <b>evidence</b> — written once, before any
/// processing, so a dispute about what the gateway actually said has an answer. And its
/// <see cref="Status"/> is the <b>dead-letter queue</b> — an event that has exhausted its attempts
/// becomes <see cref="GatewayEventStatus.DeadLettered"/> and waits for a human.
/// </para>
/// <para>
/// Nothing here is ever deleted, and the payload is never rewritten. An unprocessable event is not
/// rubbish to be swept up; it is the record of money the platform may have and cannot account for.
/// </para>
/// <para>
/// An event of a type this platform does not subscribe to is stored and marked
/// <see cref="GatewayEventStatus.Ignored"/> rather than refused. Answering a gateway with a 4xx
/// invites a retry storm, and a type nobody handles today is evidence tomorrow.
/// </para>
/// </remarks>
internal sealed class GatewayEvent : Entity<Guid>, ITenantScoped
{
    private GatewayEvent(
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
        Status = GatewayEventStatus.Pending;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private GatewayEvent()
    {
        Provider = string.Empty;
        ProviderEventId = string.Empty;
        EventType = string.Empty;
        Payload = "{}";
    }

    /// <summary>Which gateway sent it.</summary>
    public string Provider { get; private set; }

    /// <summary>The gateway's own id for the event. Unique per tenant — this is replay protection.</summary>
    public string ProviderEventId { get; private set; }

    /// <summary>What happened, in the gateway's vocabulary: <c>payment.captured</c>.</summary>
    public string EventType { get; private set; }

    /// <summary>Whether the HMAC over the raw body verified. Recorded, never inferred later.</summary>
    public bool SignatureValid { get; private set; }

    /// <summary>The raw body. Written once, before processing, and never rewritten.</summary>
    public string Payload { get; private set; }

    /// <summary>When this platform received it.</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>When the gateway says it happened, where the payload carries that.</summary>
    public DateTimeOffset? OccurredAt { get; private set; }

    /// <summary>Where processing stands.</summary>
    public GatewayEventStatus Status { get; private set; }

    /// <summary>When it was successfully applied.</summary>
    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>Why the last attempt failed.</summary>
    public string? ProcessError { get; private set; }

    /// <summary>How many times processing has been tried.</summary>
    public int Attempts { get; private set; }

    /// <summary>When the processor should try again. Null when it is not waiting.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <summary>The collection it turned out to concern, resolved during processing.</summary>
    public Guid? PaymentId { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Stores a webhook.</summary>
    /// <param name="provider">Which gateway sent it.</param>
    /// <param name="providerEventId">Its own id for the event.</param>
    /// <param name="eventType">What happened.</param>
    /// <param name="signatureValid">Whether the HMAC verified.</param>
    /// <param name="payload">The raw body.</param>
    /// <param name="occurredAt">When the gateway says it happened.</param>
    /// <param name="receivedAt">When we received it.</param>
    public static GatewayEvent Receive(
        string provider,
        string providerEventId,
        string eventType,
        bool signatureValid,
        string payload,
        DateTimeOffset? occurredAt,
        DateTimeOffset receivedAt)
        => new(
            UuidV7.NewAt(receivedAt),
            Guard.NotNullOrWhiteSpace(provider),
            Guard.NotNullOrWhiteSpace(providerEventId),
            Guard.NotNullOrWhiteSpace(eventType),
            signatureValid,
            Guard.NotNullOrWhiteSpace(payload),
            receivedAt)
        {
            OccurredAt = occurredAt,
        };

    /// <summary>Records that the event was applied.</summary>
    /// <param name="paymentId">The collection it concerned, when one was resolved.</param>
    /// <param name="at">When.</param>
    public void MarkProcessed(Guid? paymentId, DateTimeOffset at)
    {
        Status = GatewayEventStatus.Processed;
        PaymentId ??= paymentId;
        ProcessedAt = at;
        ProcessError = null;
        NextAttemptAt = null;
        Attempts++;
    }

    /// <summary>Records that this platform does not handle events of this type.</summary>
    /// <param name="at">When it was read.</param>
    public void MarkIgnored(DateTimeOffset at)
    {
        Status = GatewayEventStatus.Ignored;
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
            Status = GatewayEventStatus.DeadLettered;
            NextAttemptAt = null;
            return true;
        }

        Status = GatewayEventStatus.Failed;
        NextAttemptAt = nextAttemptAt;

        return false;
    }

    /// <summary>
    /// Puts a failed or dead-lettered event back in the queue, at an operator's request.
    /// </summary>
    /// <remarks>
    /// The attempt counter is reset, because a replay is a deliberate decision by somebody who has
    /// looked at the payload — not another automatic try. What is not reset is the payload or the
    /// signature verdict: replaying an event does not re-verify it, and an event whose signature
    /// never verified must never become processable by being replayed.
    /// </remarks>
    /// <param name="at">The current instant.</param>
    /// <returns>Whether the event was in a state that could be replayed.</returns>
    public bool Replay(DateTimeOffset at)
    {
        if (Status is not (GatewayEventStatus.Failed or GatewayEventStatus.DeadLettered))
        {
            return false;
        }

        Status = GatewayEventStatus.Pending;
        Attempts = 0;
        NextAttemptAt = at;
        ProcessError = null;

        return true;
    }
}
