namespace KlaraHome.Contracts.Notifications;

/// <summary>How a notification reaches a person.</summary>
public enum NotificationChannel
{
    /// <summary>Email. The one channel that needs no paid account (docs/08-integrations.md §7).</summary>
    Email = 0,

    /// <summary>SMS, DLT-registered in India.</summary>
    Sms = 1,

    /// <summary>WhatsApp Business, through a BSP.</summary>
    WhatsApp = 2,

    /// <summary>Held for the recipient to read in the application. No provider, so never suppressed.</summary>
    InApp = 3,
}

/// <summary>
/// What became of a queued message.
/// </summary>
/// <remarks>
/// Three terminal states, not two. <see cref="Suppressed"/> is the one that lets this platform run
/// before an SMS account exists: nobody was asked and nobody was going to be, which is neither a
/// success nor an incident (ADR-017).
/// </remarks>
public enum NotificationStatus
{
    /// <summary>Waiting for the dispatcher.</summary>
    Queued = 0,

    /// <summary>Claimed by a dispatcher and in flight.</summary>
    Sending = 1,

    /// <summary>A provider accepted it.</summary>
    Sent = 2,

    /// <summary>A provider confirmed it reached the recipient.</summary>
    Delivered = 3,

    /// <summary>A provider was asked and it did not work, after the retry budget was spent.</summary>
    Failed = 4,

    /// <summary>The recipient's mail server rejected it permanently.</summary>
    Bounced = 5,

    /// <summary>Nobody was asked. See the reason on the message.</summary>
    Suppressed = 6,
}

/// <summary>Why a message was never sent.</summary>
public enum NotificationSuppression
{
    /// <summary>Not suppressed.</summary>
    None = 0,

    /// <summary>No provider is configured for the channel. The state of an unfunded deployment.</summary>
    NoProvider = 1,

    /// <summary>The channel's feature flag is off.</summary>
    ChannelDisabled = 2,

    /// <summary>The recipient has opted out of this category on this channel.</summary>
    OptedOut = 3,

    /// <summary>There is no address to send to — no mobile number, or no email.</summary>
    NoRecipient = 4,

    /// <summary>No active template exists for this event, channel and locale.</summary>
    NoTemplate = 5,
}

/// <summary>
/// The bucket a message belongs to, which is what a recipient opts out of.
/// </summary>
/// <remarks>
/// Opting out is per category, never per event: a customer who does not want marketing email still
/// has to receive their invoice, and a preference screen listing forty event keys is a screen
/// nobody uses.
/// </remarks>
public enum NotificationCategory
{
    /// <summary>Sign-in codes, password changes, security alerts. Never opted out of.</summary>
    Security = 0,

    /// <summary>Order placed, confirmed, invoiced, cancelled.</summary>
    Orders = 1,

    /// <summary>Dispatch, tracking, delivery, failed delivery attempts.</summary>
    Shipping = 2,

    /// <summary>Refunds, returns, credit notes, payouts.</summary>
    Payments = 3,

    /// <summary>Vendor operations: onboarding, new orders, settlement statements.</summary>
    Vendor = 4,

    /// <summary>Offers and campaigns. Opt-in in practice, and the only category that is not transactional.</summary>
    Marketing = 5,
}

/// <summary>
/// Who to notify. At least one address is required, and which one is used depends on the channel.
/// </summary>
/// <param name="UserId">The account, when the recipient has one. Used to read their preferences.</param>
/// <param name="Email">Email address, for the email channel.</param>
/// <param name="Mobile">Mobile number in E.164 form, for SMS and WhatsApp.</param>
/// <param name="Name">Display name, available to templates as <c>{{name}}</c>.</param>
public sealed record NotificationRecipient(
    Guid? UserId = null,
    string? Email = null,
    string? Mobile = null,
    string? Name = null);

/// <summary>
/// A request to tell somebody something.
/// </summary>
/// <remarks>
/// The caller names an <em>event</em>, not a message. What that event says, on which channels, in
/// which language, is a template — data an operator edits — and a caller that composed its own text
/// would be a caller whose wording could never be changed without a deploy.
/// </remarks>
/// <param name="EventKey">The event, from <see cref="NotificationEvents"/>.</param>
/// <param name="Recipient">Who to tell.</param>
/// <param name="Variables">Values substituted into the template. Every placeholder must be present.</param>
/// <param name="Channels">
/// Which channels to use, or null for every channel that has an active template for the event.
/// </param>
/// <param name="Locale">Language tag, or null for the store's default.</param>
public sealed record NotificationRequest(
    string EventKey,
    NotificationRecipient Recipient,
    IReadOnlyDictionary<string, string> Variables,
    IReadOnlyCollection<NotificationChannel>? Channels = null,
    string? Locale = null);

/// <summary>One message that the request produced.</summary>
/// <param name="MessageId">Its id in the delivery log.</param>
/// <param name="Channel">The channel it was queued on.</param>
/// <param name="Status">Queued, or already suppressed.</param>
/// <param name="Suppression">Why it was suppressed, when it was.</param>
public sealed record QueuedNotification(
    Guid MessageId,
    NotificationChannel Channel,
    NotificationStatus Status,
    NotificationSuppression Suppression);

/// <summary>
/// Queues notifications from another module.
/// </summary>
/// <remarks>
/// <para>
/// The write lands in the Notifications module's own schema, in its own scope — the same
/// arrangement <c>IAuditLogger</c> uses, and for the same reason: the caller's context may be
/// carrying unsaved work that a notification must not commit on its way past.
/// </para>
/// <para>
/// It follows that queueing is not part of the caller's transaction. A message queued for an
/// operation that then rolls back will still be sent, so the events that use this are the ones
/// where that is harmless — a one-time code for a challenge that no longer exists simply fails to
/// verify. Anything where it would not be harmless publishes an integration event instead, and is
/// notified from the outbox after the commit.
/// </para>
/// </remarks>
public interface INotifier
{
    /// <summary>
    /// Renders and queues one message per applicable channel.
    /// </summary>
    /// <remarks>
    /// Never throws because a channel is unavailable. An unconfigured provider, a disabled channel,
    /// a missing template and an opted-out recipient all produce a recorded, suppressed message —
    /// a notification that cannot be sent must not fail the operation that asked for it.
    /// </remarks>
    /// <param name="request">What to send, and to whom.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<QueuedNotification>> EnqueueAsync(
        NotificationRequest request,
        CancellationToken cancellationToken = default);
}
