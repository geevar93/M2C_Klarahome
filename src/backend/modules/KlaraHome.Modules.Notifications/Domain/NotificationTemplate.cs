using KlaraHome.Contracts.Notifications;
using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Notifications.Domain;

/// <summary>
/// What one event says on one channel in one language
/// (docs/03-database-design.md §4.16).
/// </summary>
/// <remarks>
/// <para>
/// Templates are data, not code. An operator edits the wording without a deploy, which is the
/// whole reason a caller names an event rather than composing a message.
/// </para>
/// <para>
/// The seeder reasserts the <em>existence</em> of a template on every deploy but never its body,
/// so an edit survives the next release. What it does reassert is
/// <see cref="Category"/>, <see cref="IsSensitive"/> and <see cref="IsTransactional"/> — those three
/// decide whether a message may be opted out of and whether its text may be stored, and letting an
/// operator change them through a wording screen would turn a copy edit into a privacy change.
/// </para>
/// </remarks>
internal sealed class NotificationTemplate : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private NotificationTemplate(
        Guid id,
        string eventKey,
        NotificationChannel channel,
        string locale,
        string subject,
        string body,
        NotificationCategory category)
        : base(id)
    {
        EventKey = Guard.NotNullOrWhiteSpace(eventKey);
        Channel = channel;
        Locale = Guard.NotNullOrWhiteSpace(locale);
        Subject = subject ?? string.Empty;
        Body = Guard.NotNullOrWhiteSpace(body);
        Category = category;
        IsActive = true;
        Version = 1;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private NotificationTemplate()
    {
        EventKey = string.Empty;
        Locale = string.Empty;
        Subject = string.Empty;
        Body = string.Empty;
    }

    /// <summary>The event this template renders, from <see cref="NotificationEvents"/>.</summary>
    public string EventKey { get; private set; }

    /// <summary>Which channel it is written for. The same event reads very differently by SMS.</summary>
    public NotificationChannel Channel { get; private set; }

    /// <summary>IETF language tag, for example <c>en-IN</c>.</summary>
    public string Locale { get; private set; }

    /// <summary>Subject line. Empty for a channel that has none.</summary>
    public string Subject { get; private set; }

    /// <summary>The body, with <c>{{placeholder}}</c> substitutions.</summary>
    public string Body { get; private set; }

    /// <summary>
    /// The DLT template id registered with TRAI, for an Indian SMS.
    /// </summary>
    /// <remarks>
    /// Without it an SMS is silently dropped by the operator — not rejected, dropped — which is
    /// the single most common way an Indian SMS integration appears to work and does not. An SMS
    /// template with no id is therefore refused before it reaches a provider.
    /// </remarks>
    public string? ProviderTemplateId { get; private set; }

    /// <summary>The preference bucket this belongs to.</summary>
    public NotificationCategory Category { get; private set; }

    /// <summary>Whether the rendered body may be stored. False for anything carrying a one-time code.</summary>
    public bool IsSensitive { get; private set; }

    /// <summary>
    /// Whether this message is sent regardless of preferences. True for everything that is part of
    /// a transaction the recipient asked for; false only for marketing.
    /// </summary>
    public bool IsTransactional { get; private set; } = true;

    /// <summary>Whether it is used. An inactive template renders nothing and suppresses nothing.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Increments on every edit, so a delivery log entry can say which wording went out.</summary>
    public int Version { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; private set; }

    /// <summary>Declares a template.</summary>
    /// <param name="eventKey">The event it renders.</param>
    /// <param name="channel">The channel it is written for.</param>
    /// <param name="locale">The language tag.</param>
    /// <param name="subject">The subject line, or empty.</param>
    /// <param name="body">The body with its placeholders.</param>
    /// <param name="category">The preference bucket.</param>
    public static NotificationTemplate Declare(
        string eventKey,
        NotificationChannel channel,
        string locale,
        string subject,
        string body,
        NotificationCategory category)
        => new(UuidV7.New(), eventKey, channel, locale, subject, body, category);

    /// <summary>Sets the properties the seeder owns and an operator must not change.</summary>
    /// <param name="category">The preference bucket.</param>
    /// <param name="sensitive">Whether the rendered body may be stored.</param>
    /// <param name="transactional">Whether it ignores preferences.</param>
    public void Classify(NotificationCategory category, bool sensitive, bool transactional)
    {
        Category = category;
        IsSensitive = sensitive;
        IsTransactional = transactional;
    }

    /// <summary>Replaces the wording. Bumps <see cref="Version"/>.</summary>
    /// <param name="subject">The new subject.</param>
    /// <param name="body">The new body.</param>
    /// <param name="providerTemplateId">The DLT id, for SMS.</param>
    /// <param name="isActive">Whether it stays in use.</param>
    public void Revise(string subject, string body, string? providerTemplateId, bool isActive)
    {
        Subject = subject ?? string.Empty;
        Body = Guard.NotNullOrWhiteSpace(body);
        ProviderTemplateId = string.IsNullOrWhiteSpace(providerTemplateId) ? null : providerTemplateId.Trim();
        IsActive = isActive;
        Version++;
    }

    /// <summary>Sets the DLT id without touching the wording, which is how one is first registered.</summary>
    /// <param name="providerTemplateId">The registered id.</param>
    public void RegisterProviderTemplate(string? providerTemplateId)
        => ProviderTemplateId = string.IsNullOrWhiteSpace(providerTemplateId) ? null : providerTemplateId.Trim();
}
