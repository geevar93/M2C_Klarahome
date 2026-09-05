using KlaraHome.Contracts.Notifications;
using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Notifications.Domain;

/// <summary>
/// One person's choice about one category, per channel
/// (docs/03-database-design.md §4.16, docs/04-api-specification.md §3.1).
/// </summary>
/// <remarks>
/// <para>
/// A row exists only where somebody has expressed a preference. The absence of a row is the
/// default, not "off": storing a row per user per category on registration would write six rows
/// nobody asked for and would freeze today's defaults into every account ever created.
/// </para>
/// <para>
/// <see cref="NotificationCategory.Security"/> is not stored and cannot be opted out of. A person
/// who has turned off notice of a password change has turned off the only warning they would get
/// that somebody else changed it.
/// </para>
/// </remarks>
internal sealed class NotificationPreference : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private NotificationPreference(Guid id, Guid userId, NotificationCategory category)
        : base(id)
    {
        UserId = userId;
        Category = category;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private NotificationPreference()
    {
    }

    /// <summary>Whose preference it is.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Which bucket it applies to.</summary>
    public NotificationCategory Category { get; private set; }

    /// <summary>Whether email is wanted for this category.</summary>
    public bool Email { get; private set; } = true;

    /// <summary>Whether SMS is wanted for this category.</summary>
    public bool Sms { get; private set; } = true;

    /// <summary>Whether WhatsApp is wanted for this category.</summary>
    public bool WhatsApp { get; private set; } = true;

    /// <summary>Whether in-application messages are wanted for this category.</summary>
    public bool InApp { get; private set; } = true;

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

    /// <summary>
    /// Whether a category is on by default when nobody has said otherwise.
    /// </summary>
    /// <remarks>
    /// Marketing is the only one that is off. Everything else is part of a transaction the person
    /// entered into, and India's consent rules make opt-in the only defensible default for the
    /// rest.
    /// </remarks>
    /// <param name="category">The category.</param>
    public static bool DefaultFor(NotificationCategory category)
        => category != NotificationCategory.Marketing;

    /// <summary>Records a preference for one category.</summary>
    /// <param name="userId">Whose it is.</param>
    /// <param name="category">Which bucket.</param>
    public static NotificationPreference For(Guid userId, NotificationCategory category)
    {
        var preference = new NotificationPreference(UuidV7.New(), userId, category);
        var value = DefaultFor(category);

        preference.Set(value, value, value, value);
        return preference;
    }

    /// <summary>Sets every channel at once, as the preferences screen saves them.</summary>
    /// <param name="email">Whether email is wanted.</param>
    /// <param name="sms">Whether SMS is wanted.</param>
    /// <param name="whatsApp">Whether WhatsApp is wanted.</param>
    /// <param name="inApp">Whether in-application messages are wanted.</param>
    public void Set(bool email, bool sms, bool whatsApp, bool inApp)
    {
        Email = email;
        Sms = sms;
        WhatsApp = whatsApp;
        InApp = inApp;
    }

    /// <summary>Whether this preference allows one channel.</summary>
    /// <param name="channel">The channel.</param>
    public bool Allows(NotificationChannel channel) => channel switch
    {
        NotificationChannel.Email => Email,
        NotificationChannel.Sms => Sms,
        NotificationChannel.WhatsApp => WhatsApp,
        NotificationChannel.InApp => InApp,
        _ => true,
    };
}
