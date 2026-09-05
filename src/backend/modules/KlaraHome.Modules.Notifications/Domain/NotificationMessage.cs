using KlaraHome.Contracts.Notifications;
using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Notifications.Domain;

/// <summary>
/// One attempt to tell one person one thing — the delivery log
/// (docs/03-database-design.md §4.16).
/// </summary>
/// <remarks>
/// <para>
/// The row exists whether or not anything was sent. A message that could not be delivered because
/// no provider is configured is recorded as <see cref="NotificationStatus.Suppressed"/> with the
/// reason, which is what makes "what did we fail to tell people" a query rather than a guess
/// (ADR-017).
/// </para>
/// <para>
/// The table is partitioned monthly by <see cref="CreatedAt"/> (§8), so the row carries no
/// <c>xmin</c> concurrency token — PostgreSQL cannot return a system column from a partitioned
/// table. Concurrency is handled pessimistically instead: the dispatcher claims rows with
/// <c>FOR UPDATE SKIP LOCKED</c>, so two workers never hold the same one.
/// </para>
/// </remarks>
internal sealed class NotificationMessage : AggregateRoot<Guid>, ITenantScoped, IPartitioned
{
    private NotificationMessage(
        Guid id,
        DateTimeOffset createdAt,
        string eventKey,
        NotificationChannel channel,
        string recipient)
        : base(id)
    {
        CreatedAt = createdAt;
        EventKey = Guard.NotNullOrWhiteSpace(eventKey);
        Channel = channel;
        Recipient = recipient ?? string.Empty;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private NotificationMessage()
    {
        EventKey = string.Empty;
        Recipient = string.Empty;
    }

    /// <summary>When it was queued. Part of the primary key, because the table is partitioned on it.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The event that produced it.</summary>
    public string EventKey { get; private set; }

    /// <summary>The channel it was queued on.</summary>
    public NotificationChannel Channel { get; private set; }

    /// <summary>The template used, when one was found.</summary>
    public Guid? TemplateId { get; private set; }

    /// <summary>Which version of that template's wording went out.</summary>
    public int? TemplateVersion { get; private set; }

    /// <summary>The account notified, when the recipient had one.</summary>
    public Guid? UserId { get; private set; }

    /// <summary>The address it was sent to: an email address, or a number in E.164 form.</summary>
    public string Recipient { get; private set; }

    /// <summary>The rendered subject. Null for a channel with none, and for a sensitive message.</summary>
    public string? Subject { get; private set; }

    /// <summary>
    /// The rendered body — <b>null for a sensitive template</b>, which is what keeps a one-time
    /// code out of the database as well as out of the log (ADR-017).
    /// </summary>
    public string? Body { get; private set; }

    /// <summary>
    /// The variables the template was rendered with, as <c>jsonb</c>. Values are redacted for a
    /// sensitive template; the names are kept, because knowing <em>which</em> variables a message
    /// carried is diagnostic and knowing their values is a leak.
    /// </summary>
    public string? Payload { get; private set; }

    /// <summary>Where it got to.</summary>
    public NotificationStatus Status { get; private set; } = NotificationStatus.Queued;

    /// <summary>Why nobody was asked, when nobody was.</summary>
    public NotificationSuppression Suppression { get; private set; } = NotificationSuppression.None;

    /// <summary>The provider's own id for the message, for reconciling a delivery receipt.</summary>
    public string? ProviderMessageId { get; private set; }

    /// <summary>How many times delivery has been attempted.</summary>
    public int Attempts { get; private set; }

    /// <summary>When the dispatcher should next try. Null once the message is in a terminal state.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <summary>The last failure, kept so a stuck message can be diagnosed without re-sending it.</summary>
    public string? Error { get; private set; }

    /// <summary>When a provider accepted it.</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>When a provider confirmed it arrived.</summary>
    public DateTimeOffset? DeliveredAt { get; private set; }

    /// <summary>The correlation id of the request that caused it.</summary>
    public string? CorrelationId { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Whether the dispatcher still has work to do on this message.</summary>
    public bool IsPending => Status is NotificationStatus.Queued or NotificationStatus.Sending;

    /// <summary>Creates a message in the queued state.</summary>
    /// <param name="createdAt">Now, from the sanctioned clock.</param>
    /// <param name="eventKey">The event.</param>
    /// <param name="channel">The channel.</param>
    /// <param name="recipient">The address.</param>
    public static NotificationMessage Queue(
        DateTimeOffset createdAt,
        string eventKey,
        NotificationChannel channel,
        string recipient)
        => new(UuidV7.New(), createdAt, eventKey, channel, recipient)
        {
            NextAttemptAt = createdAt,
        };

    /// <summary>Records which template produced the content.</summary>
    /// <param name="templateId">The template's id.</param>
    /// <param name="version">Its version at the time of rendering.</param>
    public void RenderedFrom(Guid templateId, int version)
    {
        TemplateId = templateId;
        TemplateVersion = version;
    }

    /// <summary>Attaches the rendered content and the variables it was rendered from.</summary>
    /// <param name="subject">The rendered subject, or null.</param>
    /// <param name="body">The rendered body.</param>
    /// <param name="payload">The variables as a JSON document, already redacted if need be.</param>
    /// <param name="sensitive">Whether the content may be stored at all.</param>
    public void Describe(string? subject, string body, string? payload, bool sensitive)
    {
        Subject = sensitive ? null : subject;
        Body = sensitive ? null : body;
        Payload = payload;
    }

    /// <summary>Notes the account and the causing request.</summary>
    /// <param name="userId">The recipient's account, when they have one.</param>
    /// <param name="correlationId">The causing request's correlation id.</param>
    public void Attribute(Guid? userId, string? correlationId)
    {
        UserId = userId;
        CorrelationId = correlationId;
    }

    /// <summary>Claims the message for one dispatch attempt.</summary>
    /// <remarks>
    /// The attempt is counted here rather than on the outcome, so a dispatcher that dies mid-send
    /// still consumes budget — otherwise a message that reliably kills the process would be retried
    /// for ever.
    /// </remarks>
    public void BeginAttempt()
    {
        Status = NotificationStatus.Sending;
        Attempts++;
        NextAttemptAt = null;
    }

    /// <summary>Records that a provider accepted it.</summary>
    /// <param name="at">When.</param>
    /// <param name="providerMessageId">The provider's id for it.</param>
    public void Sent(DateTimeOffset at, string? providerMessageId)
    {
        Status = NotificationStatus.Sent;
        SentAt = at;
        ProviderMessageId = providerMessageId;
        Error = null;
        NextAttemptAt = null;
    }

    /// <summary>Records a delivery receipt.</summary>
    /// <param name="at">When the provider says it arrived.</param>
    public void Delivered(DateTimeOffset at)
    {
        Status = NotificationStatus.Delivered;
        DeliveredAt = at;
    }

    /// <summary>Schedules another attempt after a transient failure.</summary>
    /// <param name="nextAttemptAt">When to try again.</param>
    /// <param name="error">What went wrong.</param>
    public void Retry(DateTimeOffset nextAttemptAt, string error)
    {
        Status = NotificationStatus.Queued;
        NextAttemptAt = nextAttemptAt;
        Error = Truncate(error);
    }

    /// <summary>Gives up.</summary>
    /// <param name="error">What went wrong.</param>
    /// <param name="bounced">Whether the recipient's server rejected it permanently.</param>
    public void Failed(string error, bool bounced = false)
    {
        Status = bounced ? NotificationStatus.Bounced : NotificationStatus.Failed;
        Error = Truncate(error);
        NextAttemptAt = null;
    }

    /// <summary>Records that nobody was asked, and why.</summary>
    /// <param name="reason">Why not.</param>
    public void Suppress(NotificationSuppression reason)
    {
        Status = NotificationStatus.Suppressed;
        Suppression = reason;
        NextAttemptAt = null;
    }

    /// <summary>Puts a terminally failed message back in the queue, for the admin retry action.</summary>
    /// <param name="at">When to try again — now, normally.</param>
    public void Requeue(DateTimeOffset at)
    {
        Status = NotificationStatus.Queued;
        Suppression = NotificationSuppression.None;
        NextAttemptAt = at;
        Attempts = 0;
        Error = null;
    }

    /// <summary>
    /// A provider error can be an entire SMTP conversation. It is cut to the column width here
    /// rather than left to the database, which would reject the update and lose the outcome.
    /// </summary>
    private static string Truncate(string value)
        => string.IsNullOrEmpty(value) ? string.Empty : value.Length <= 2000 ? value : value[..2000];
}
