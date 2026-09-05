using System.Globalization;
using KlaraHome.Contracts.Notifications;
using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Identity.Domain;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Identity.Infrastructure.Access;

/// <summary>
/// Delivers a one-time code through the Notifications module.
/// </summary>
/// <remarks>
/// <para>
/// This replaces <c>LoggingOtpDispatcher</c>, which wrote codes to the application log —
/// precisely what <c>07-security-compliance.md</c> §3 forbids — and was the largest piece of debt
/// Step 7 left behind.
/// </para>
/// <para>
/// Nothing about the code is stored on the way out. Every template these events map to is marked
/// sensitive, which means the notifier renders it, hands it to the provider inline and records the
/// message with its body and its variables redacted (ADR-017). The plaintext exists in memory, for
/// the duration of one request, and nowhere else — the challenge row has only its hash.
/// </para>
/// <para>
/// The two modules are joined by <c>INotifier</c> in <c>KlaraHome.Contracts</c>, not by a project
/// reference: Identity does not know the Notifications module exists, and the Notifications module
/// does not know what an OTP is.
/// </para>
/// </remarks>
/// <param name="notifier">The published notification entry point.</param>
/// <param name="settings">Supplies the store name every template greets the recipient with.</param>
/// <param name="options">Supplies the code and link lifetimes the message quotes.</param>
internal sealed class NotificationOtpDispatcher(
    INotifier notifier,
    IStoreSettings settings,
    IOptions<AuthOptions> options) : IOtpDispatcher
{
    /// <inheritdoc />
    public async Task DispatchAsync(
        string destination,
        OtpChannel channel,
        OtpPurpose purpose,
        string code,
        CancellationToken cancellationToken)
    {
        var branding = await settings.GetAsync<BrandingSettings>(cancellationToken).ConfigureAwait(false);
        var otp = options.Value.Otp;

        // A link-bearing purpose quotes the link lifetime, a code-bearing one the code lifetime.
        // Quoting the wrong number is the kind of small lie that generates support tickets.
        var minutes = purpose is OtpPurpose.VerifyEmail or OtpPurpose.PasswordReset
            ? otp.LinkLifetimeMinutes
            : otp.LifetimeMinutes;

        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["storeName"] = branding.StoreName,
            ["minutes"] = minutes.ToString(CultureInfo.InvariantCulture),
            ["code"] = code,
            ["link"] = code,
        };

        var recipient = channel == OtpChannel.Email
            ? new NotificationRecipient(Email: destination)
            : new NotificationRecipient(Mobile: destination);

        // The channel is named explicitly rather than left to the templates. The Identity module
        // decided which channel it issued the challenge on and stored that decision on the
        // challenge row; letting the template set decide would send a code by email to somebody
        // who asked for one by SMS.
        await notifier
            .EnqueueAsync(
                new NotificationRequest(EventKeyFor(purpose), recipient, variables, [ChannelFor(channel)]),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Maps an OTP purpose onto the notification event that renders it.</summary>
    /// <param name="purpose">What the code proves.</param>
    private static string EventKeyFor(OtpPurpose purpose) => purpose switch
    {
        OtpPurpose.Login => NotificationEvents.OtpLogin,
        OtpPurpose.VerifyMobile => NotificationEvents.OtpMobileVerification,
        OtpPurpose.VerifyEmail => NotificationEvents.EmailVerification,
        OtpPurpose.PasswordReset => NotificationEvents.PasswordReset,
        _ => NotificationEvents.OtpLogin,
    };

    /// <summary>Maps the challenge's channel onto the notification channel.</summary>
    /// <param name="channel">How the challenge was issued.</param>
    private static NotificationChannel ChannelFor(OtpChannel channel) => channel switch
    {
        OtpChannel.Email => NotificationChannel.Email,
        OtpChannel.WhatsApp => NotificationChannel.WhatsApp,
        _ => NotificationChannel.Sms,
    };
}
