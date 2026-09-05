using KlaraHome.Contracts.Notifications;
using KlaraHome.Contracts.Platform;

namespace KlaraHome.Modules.Notifications.Infrastructure;

/// <summary>
/// The feature flags the Notifications module reads (ADR-017).
/// </summary>
/// <remarks>
/// <para>
/// One per channel, and they answer a different question from "is a provider configured". A
/// deployment turns a channel <em>off</em> when it does not want those messages sent even though
/// it could send them — during a migration, or while an SMS bill is being investigated. A channel
/// with no provider is suppressed whether the flag is on or not.
/// </para>
/// <para>
/// All three ship on, for the same reason the Identity flags do: that is what the product does
/// when it is fully provisioned, and turning one off should be a decision an operator makes and
/// the audit trail records.
/// </para>
/// </remarks>
internal static class NotificationFeatures
{
    /// <summary>Gates every outbound email.</summary>
    public const string Email = "notifications.email";

    /// <summary>Gates every outbound SMS.</summary>
    public const string Sms = "notifications.sms";

    /// <summary>Gates every outbound WhatsApp message.</summary>
    public const string WhatsApp = "notifications.whatsapp";

    /// <summary>Every flag this module declares.</summary>
    public static IReadOnlyList<FeatureFlagDeclaration> All { get; } =
    [
        new(Email, true, "Send transactional email. Needs an SMTP host or an email provider."),
        new(Sms, true, "Send transactional SMS. Needs a DLT-registered Indian SMS provider."),
        new(WhatsApp, true, "Send WhatsApp messages. Needs a WhatsApp Business provider."),
    ];

    /// <summary>The flag gating a channel, or null for a channel that has none.</summary>
    /// <param name="channel">The channel.</param>
    /// <remarks>
    /// In-application messages have no flag: there is no provider, no cost and no third party, so
    /// there would be nothing for an operator to decide.
    /// </remarks>
    public static string? FlagFor(NotificationChannel channel) => channel switch
    {
        NotificationChannel.Email => Email,
        NotificationChannel.Sms => Sms,
        NotificationChannel.WhatsApp => WhatsApp,
        _ => null,
    };
}

/// <summary>Publishes this module's flags to the Platform seeder that owns the table.</summary>
internal sealed class NotificationFeatureFlagSource : IFeatureFlagSource
{
    /// <inheritdoc />
    public string Module => "Notifications";

    /// <inheritdoc />
    public IReadOnlyList<FeatureFlagDeclaration> Flags => NotificationFeatures.All;
}
