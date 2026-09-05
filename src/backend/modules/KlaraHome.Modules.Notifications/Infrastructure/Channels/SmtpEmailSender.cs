using KlaraHome.Contracts.Notifications;
using KlaraHome.Contracts.Platform;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace KlaraHome.Modules.Notifications.Infrastructure.Channels;

/// <summary>
/// Email over SMTP (docs/08-integrations.md §3.3).
/// </summary>
/// <remarks>
/// <para>
/// The one channel this platform can deliver on with no paid account: Mailpit on the development
/// stack, and any SMTP host in production. That is why it is the fallback the whole degraded mode
/// rests on (ADR-017).
/// </para>
/// <para>
/// A connection per message rather than a pooled one. Message volume here is transactional — tens
/// per minute at most — and a long-lived SMTP connection has to be kept alive, re-authenticated
/// and recovered after every network hiccup, which is a great deal of machinery to save a
/// handshake nobody is waiting on.
/// </para>
/// </remarks>
/// <param name="options">Transport settings.</param>
/// <param name="settings">Reads the store name, so the sender is branded without a deploy.</param>
/// <param name="logger">Reports refusals the provider gave a reason for.</param>
internal sealed partial class SmtpEmailSender(
    IOptions<EmailOptions> options,
    IStoreSettings settings,
    ILogger<SmtpEmailSender> logger) : IChannelSender
{
    private readonly EmailOptions _options = options.Value;

    /// <inheritdoc />
    public NotificationChannel Channel => NotificationChannel.Email;

    /// <inheritdoc />
    public bool IsConfigured => _options.IsConfigured;

    /// <inheritdoc />
    public async Task<SendOutcome> SendAsync(OutboundMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!MailboxAddress.TryParse(message.Recipient, out var recipient))
        {
            // Permanent: the address will not become valid on the third attempt.
            return SendOutcome.Permanent($"'{message.Recipient}' is not a valid email address.");
        }

        var branding = await settings.GetAsync<BrandingSettings>(cancellationToken).ConfigureAwait(false);

        var mail = new MimeMessage
        {
            Subject = message.Subject ?? branding.StoreName,
        };

        mail.From.Add(new MailboxAddress(
            string.IsNullOrWhiteSpace(_options.FromName) ? branding.StoreName : _options.FromName,
            _options.FromAddress));

        mail.To.Add(string.IsNullOrWhiteSpace(message.RecipientName)
            ? recipient
            : new MailboxAddress(message.RecipientName, recipient.Address));

        // HTML with a generated plain-text alternative. A text part is not politeness: a
        // multipart message with no text alternative scores worse with every spam filter, and this
        // deployment has no sending reputation to spend.
        var builder = new BodyBuilder { HtmlBody = message.Body };
        builder.TextBody = HtmlToText(message.Body);
        mail.Body = builder.ToMessageBody();

        using var client = new SmtpClient
        {
            Timeout = _options.Smtp.TimeoutSeconds * 1000,
        };

        try
        {
            await client
                .ConnectAsync(
                    _options.Smtp.Host,
                    _options.Smtp.Port,
                    _options.Smtp.UseTls ? SecureSocketOptions.StartTlsWhenAvailable : SecureSocketOptions.None,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(_options.Smtp.UserName))
            {
                await client
                    .AuthenticateAsync(_options.Smtp.UserName, _options.Smtp.Password, cancellationToken)
                    .ConfigureAwait(false);
            }

            var response = await client.SendAsync(mail, cancellationToken).ConfigureAwait(false);
            await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);

            return SendOutcome.Accepted(mail.MessageId ?? response);
        }
        catch (SmtpCommandException exception)
        {
            // The server said no and said why. A mailbox that does not exist is permanent; a
            // mailbox that is full is not.
            var permanent = exception.StatusCode is >= SmtpStatusCode.CommandNotImplemented
                and not SmtpStatusCode.MailboxBusy
                and not SmtpStatusCode.InsufficientStorage;

            SmtpRefused(logger, exception, (int)exception.StatusCode, permanent);

            return permanent
                ? SendOutcome.Permanent(exception.Message)
                : SendOutcome.Transient(exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A connection failure, a TLS failure, a timeout. All of them are worth another try.
            return SendOutcome.Transient(exception.Message);
        }
    }

    /// <summary>
    /// A plain-text alternative from the HTML body.
    /// </summary>
    /// <remarks>
    /// Crude on purpose: tags are dropped, block boundaries become line breaks and entities are
    /// decoded. The templates this renders are short transactional messages, not newsletters, and
    /// a real HTML-to-text conversion would be another dependency to justify.
    /// </remarks>
    internal static string HtmlToText(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var text = html
            .Replace("</p>", "\n\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</div>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</h1>", "\n\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</h2>", "\n\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</li>", "\n", StringComparison.OrdinalIgnoreCase);

        var stripped = new System.Text.StringBuilder(text.Length);
        var inTag = false;

        foreach (var character in text)
        {
            switch (character)
            {
                case '<':
                    inTag = true;
                    break;
                case '>':
                    inTag = false;
                    break;
                default:
                    if (!inTag)
                    {
                        stripped.Append(character);
                    }

                    break;
            }
        }

        return System.Net.WebUtility.HtmlDecode(stripped.ToString()).Trim();
    }

    [LoggerMessage(EventId = 1520, Level = LogLevel.Warning,
        Message = "The SMTP server refused a message with status {SmtpStatus} (permanent: {SmtpPermanent})")]
    private static partial void SmtpRefused(ILogger logger, Exception exception, int smtpStatus, bool smtpPermanent);
}
