using System.Globalization;
using KlaraHome.Contracts.Notifications;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Notifications.Infrastructure.Channels;

/// <summary>
/// The development SMS sink: an SMS is delivered as an email to Mailpit (ADR-017).
/// </summary>
/// <remarks>
/// <para>
/// This is what retired <c>LoggingOtpDispatcher</c> rather than relocating it. A developer needs
/// to complete a mobile-OTP sign-in locally; the code must not reach a log file or a database
/// column; and there is no free SMS route. Delivering the text to the development mail catcher
/// satisfies all three, and exercises the real path — render, queue, dispatch, record — rather
/// than a shortcut that only exists here.
/// </para>
/// <para>
/// <b>It is registered only where <c>IHostEnvironment.IsProduction()</c> is false, and only when no
/// real SMS provider is configured.</b> In production the SMS channel has no sender at all and
/// every message is recorded as <c>Suppressed / NoProvider</c>. A test asserts this registration is
/// absent from a Production container, because a mistake here would send one customer's sign-in
/// code to the operations mailbox.
/// </para>
/// </remarks>
/// <param name="email">The SMTP sender the text is handed to.</param>
/// <param name="logger">Announces, once, that SMS is going to the mail catcher.</param>
internal sealed partial class DevelopmentSmsSender(
    SmtpEmailSender email,
    ILogger<DevelopmentSmsSender> logger) : IChannelSender
{
    private int _announced;

    /// <inheritdoc />
    public NotificationChannel Channel => NotificationChannel.Sms;

    /// <inheritdoc />
    public bool IsConfigured => email.IsConfigured;

    /// <summary>
    /// False. This is a mail catcher, not an Indian operator: there is nothing to register a
    /// template with, and applying the DLT rule here would make a local sign-in impossible — which
    /// is the one thing this route exists to make possible.
    /// </summary>
    public bool RequiresProviderTemplate => false;

    /// <inheritdoc />
    public async Task<SendOutcome> SendAsync(OutboundMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (Interlocked.Exchange(ref _announced, 1) == 0)
        {
            SmsRoutedToMail(logger);
        }

        // Addressed to a mailbox derived from the number, so Mailpit's list reads like an inbox per
        // recipient and two developers testing different numbers do not have to guess which message
        // is theirs. The domain is invalid by design (RFC 2606) so nothing can escape the catcher.
        var address = string.Create(
            CultureInfo.InvariantCulture,
            $"{Digits(message.Recipient)}@sms.invalid");

        var envelope = new OutboundMessage(
            address,
            message.Recipient,
            $"[SMS to {message.Recipient}] {message.EventKey}",
            $"<pre>{System.Net.WebUtility.HtmlEncode(message.Body)}</pre>",
            message.ProviderTemplateId,
            message.EventKey);

        var outcome = await email.SendAsync(envelope, cancellationToken).ConfigureAwait(false);

        // A failure to reach the mail catcher is a development-environment fault and is reported
        // as transient, so the dispatcher's retry path is exercised locally too.
        return outcome;
    }

    /// <summary>Reduces a number to digits, so it is usable as the local part of an address.</summary>
    private static string Digits(string recipient)
    {
        var digits = new string([.. recipient.Where(char.IsAsciiDigit)]);
        return digits.Length == 0 ? "unknown" : digits;
    }

    [LoggerMessage(EventId = 1521, Level = LogLevel.Warning,
        Message = "No SMS provider is configured. Outside Production, SMS is delivered to the mail "
                  + "catcher instead, addressed to <digits>@sms.invalid. Production suppresses these "
                  + "messages rather than rerouting them.")]
    private static partial void SmsRoutedToMail(ILogger logger);
}

/// <summary>
/// The in-application channel, which has no provider because the delivery log <em>is</em> the
/// inbox.
/// </summary>
/// <remarks>
/// Storing the row is the delivery, so this sender does nothing and succeeds. It exists so the
/// router has one uniform way to ask a channel to carry a message, rather than a special case that
/// every caller has to know about. The surface that reads these back belongs to the account area
/// and is not in Step 8's deliverables.
/// </remarks>
internal sealed class InAppSender : IChannelSender
{
    /// <inheritdoc />
    public NotificationChannel Channel => NotificationChannel.InApp;

    /// <inheritdoc />
    public bool IsConfigured => true;

    /// <inheritdoc />
    public Task<SendOutcome> SendAsync(OutboundMessage message, CancellationToken cancellationToken)
        => Task.FromResult(SendOutcome.Accepted());
}
