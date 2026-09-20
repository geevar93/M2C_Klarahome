using KlaraHome.Contracts.Notifications;

namespace KlaraHome.Modules.Notifications.Infrastructure.Seeding;

/// <summary>One template as the catalogue declares it.</summary>
/// <param name="EventKey">The event it renders.</param>
/// <param name="Channel">The channel it is written for.</param>
/// <param name="Subject">The subject line, or empty for a channel with none.</param>
/// <param name="Body">The body, with its placeholders.</param>
/// <param name="Category">The preference bucket.</param>
/// <param name="IsSensitive">Whether the rendered body may be stored.</param>
/// <param name="IsTransactional">Whether it ignores preferences.</param>
internal sealed record TemplateDescriptor(
    string EventKey,
    NotificationChannel Channel,
    string Subject,
    string Body,
    NotificationCategory Category,
    bool IsSensitive = false,
    bool IsTransactional = true);

/// <summary>
/// The messages every deployment starts with (docs/03-database-design.md §7).
/// </summary>
/// <remarks>
/// <para>
/// Declared in code and seeded on every deploy, the same arrangement the permission catalogue and
/// the feature flags use. A test asserts that every key in <see cref="NotificationEvents"/> has at
/// least one template and that every template names a declared event, so the two cannot drift.
/// </para>
/// <para>
/// The wording is placeholder-plain and says so. Aesthetics — and the HTML email design these
/// bodies would sit inside — are Step 30's; what matters now is that the values are right, the
/// placeholders match what the callers pass, and an operator can rewrite any of it without a
/// deploy.
/// </para>
/// <para>
/// <b>No SMS template carries a real DLT id</b>, because this deployment has no TRAI registration
/// (docs/08-integrations.md §7). They are seeded inactive for exactly that reason: an active SMS
/// template with no id is refused by the database constraint, and an inactive one is honest about
/// why nothing is sent.
/// </para>
/// </remarks>
internal static class DefaultTemplates
{
    /// <summary>The language the shipped templates are written in.</summary>
    public const string Locale = "en-IN";

    /// <summary>Every template this platform ships.</summary>
    public static IReadOnlyList<TemplateDescriptor> All { get; } =
    [
        new(
            NotificationEvents.OtpLogin,
            NotificationChannel.Sms,
            string.Empty,
            "{{code}} is your {{storeName}} verification code. It expires in {{minutes}} minutes. "
            + "Do not share it with anyone.",
            NotificationCategory.Security,
            IsSensitive: true),

        new(
            NotificationEvents.OtpLogin,
            NotificationChannel.Email,
            "Your {{storeName}} sign-in code",
            "<p>Your sign-in code is <strong>{{code}}</strong>.</p>"
            + "<p>It expires in {{minutes}} minutes. If you did not ask for it, you can ignore this "
            + "message — nobody can sign in without it.</p>",
            NotificationCategory.Security,
            IsSensitive: true),

        new(
            NotificationEvents.OtpMobileVerification,
            NotificationChannel.Sms,
            string.Empty,
            "{{code}} is your {{storeName}} code to confirm this mobile number. It expires in "
            + "{{minutes}} minutes.",
            NotificationCategory.Security,
            IsSensitive: true),

        new(
            NotificationEvents.EmailVerification,
            NotificationChannel.Email,
            "Confirm your email address",
            "<p>Confirm this address to finish setting up your {{storeName}} account.</p>"
            + "<p><a href=\"{{link}}\">Confirm my email address</a></p>"
            + "<p>The link expires in {{minutes}} minutes.</p>",
            NotificationCategory.Security,
            IsSensitive: true),

        new(
            NotificationEvents.PasswordReset,
            NotificationChannel.Email,
            "Reset your {{storeName}} password",
            "<p>Somebody asked to reset the password for this account.</p>"
            + "<p><a href=\"{{link}}\">Choose a new password</a></p>"
            + "<p>The link expires in {{minutes}} minutes. If it was not you, nothing has changed "
            + "and you can ignore this message.</p>",
            NotificationCategory.Security,
            IsSensitive: true),

        new(
            NotificationEvents.PasswordChanged,
            NotificationChannel.Email,
            "Your {{storeName}} password was changed",
            "<p>The password on your account was changed on {{changedAt}}.</p>"
            + "<p>If that was not you, contact us at {{supportEmail}} straight away — somebody else "
            + "may have access to your account.</p>",
            NotificationCategory.Security),

        new(
            NotificationEvents.TemporaryPasswordIssued,
            NotificationChannel.Email,
            "A temporary password has been issued for your account",
            "<p>An administrator has issued a temporary password for your {{storeName}} account.</p>"
            + "<p>You will be asked to choose a new one the first time you sign in, and every "
            + "session you had open has been signed out.</p>"
            + "<p>If you did not expect this, contact {{supportEmail}}.</p>",
            NotificationCategory.Security),

        new(
            NotificationEvents.AccountCreated,
            NotificationChannel.Email,
            "Welcome to {{storeName}}",
            "<p>Hello {{name}},</p>"
            + "<p>Your {{storeName}} account is ready.</p>",
            NotificationCategory.Security),

        new(
            NotificationEvents.ChannelTest,
            NotificationChannel.Email,
            "{{storeName}} test message",
            "<p>This is a test message sent from the {{storeName}} admin console at {{sentAt}}.</p>"
            + "<p>If you are reading it, the email channel works.</p>",
            NotificationCategory.Security),

        new(
            NotificationEvents.ChannelTest,
            NotificationChannel.Sms,
            string.Empty,
            "This is a test message from {{storeName}}, sent at {{sentAt}}.",
            NotificationCategory.Security),

        // Step 21. The two stock alerts are Marketing and not transactional, which is the whole
        // reason the distinction exists on a template: nobody ordered anything, somebody asked to be
        // told, and asking to be told about one product is not consent to be sent campaigns. A
        // recipient who has opted out of Marketing gets a suppressed row rather than a message.
        new(
            NotificationEvents.BackInStock,
            NotificationChannel.Email,
            "{{productName}} is back in stock",
            "<p>Hello {{name}},</p>"
            + "<p><strong>{{productName}}</strong> is available again at {{storeName}}, at "
            + "{{price}}.</p>"
            + "<p>Stock is limited and we cannot hold it for you.</p>",
            NotificationCategory.Marketing,
            IsTransactional: false),

        new(
            NotificationEvents.PriceDrop,
            NotificationChannel.Email,
            "{{productName}} has come down in price",
            "<p>Hello {{name}},</p>"
            + "<p><strong>{{productName}}</strong> is now {{price}}, down from {{previousPrice}}.</p>",
            NotificationCategory.Marketing,
            IsTransactional: false),

        new(
            NotificationEvents.QuestionAnswered,
            NotificationChannel.Email,
            "Your question about {{productName}} has been answered",
            "<p>Hello {{name}},</p>"
            + "<p>You asked: {{question}}</p>"
            + "<p>{{answeredBy}} replied: {{answer}}</p>",
            NotificationCategory.Orders),

        new(
            NotificationEvents.ReportReady,
            NotificationChannel.Email,
            "{{reportName}} for {{periodLabel}}",
            "<p>The scheduled report <strong>{{reportName}}</strong> for {{periodLabel}} has been "
            + "produced.</p>"
            + "<p>It covers {{rowCount}} row(s) and can be downloaded from the admin console for "
            + "{{expiryHours}} hour(s): {{downloadUrl}}</p>",
            NotificationCategory.Vendor),

        // The order, shipping and payment messages. In-app only, deliberately: this deployment has
        // never sent a shopper an email about an order, and seeding an email template here would
        // start doing so on the next deploy — a behaviour change nobody asked for, addressed to
        // every customer at once. The channel a store adds next is a template an operator writes,
        // which is the whole reason the wording lives in the database.
        //
        // The four shipping ones are NOT transactional, so the Shipping preference governs them.
        // That is the point of the distinction: somebody who has turned off tracking chatter has
        // turned off tracking chatter, and still gets told that their money moved.
        new(
            NotificationEvents.OrderPlaced,
            NotificationChannel.InApp,
            "Order {{orderNumber}} placed",
            "We have your order {{orderNumber}} for {{total}}. We will tell you as each part of it "
            + "is dispatched.",
            NotificationCategory.Orders),

        new(
            NotificationEvents.OrderCancelled,
            NotificationChannel.InApp,
            "Part of order {{orderNumber}} was cancelled",
            "{{cancelledTotal}} of order {{orderNumber}} has been cancelled. {{reason}} Anything "
            + "already paid for it goes back to the way you paid.",
            NotificationCategory.Orders),

        new(
            NotificationEvents.OrderShipped,
            NotificationChannel.InApp,
            "Order {{orderNumber}} is on its way",
            "{{subOrderNumber}} has been handed to the courier. You can follow it from your orders "
            + "page.",
            NotificationCategory.Shipping,
            IsTransactional: false),

        new(
            NotificationEvents.OrderOutForDelivery,
            NotificationChannel.InApp,
            "Order {{orderNumber}} is out for delivery",
            "{{subOrderNumber}} is with the delivery agent and should reach you today.",
            NotificationCategory.Shipping,
            IsTransactional: false),

        new(
            NotificationEvents.OrderDelivered,
            NotificationChannel.InApp,
            "Order {{orderNumber}} was delivered",
            "{{subOrderNumber}} has been delivered. If something is not right, you can start a "
            + "return from your orders page.",
            NotificationCategory.Shipping,
            IsTransactional: false),

        new(
            NotificationEvents.OrderDeliveryFailed,
            NotificationChannel.InApp,
            "We could not deliver order {{orderNumber}}",
            "A delivery attempt for {{subOrderNumber}} did not succeed. {{reason}} The courier will "
            + "try again.",
            NotificationCategory.Shipping,
            IsTransactional: false),

        new(
            NotificationEvents.PaymentCaptured,
            NotificationChannel.InApp,
            "Payment received for order {{orderNumber}}",
            "We have received {{amount}} for order {{orderNumber}}.",
            NotificationCategory.Payments),

        new(
            NotificationEvents.PaymentFailed,
            NotificationChannel.InApp,
            "Payment for order {{orderNumber}} did not go through",
            "{{reason}} The order is still held — you can try paying again from your orders page.",
            NotificationCategory.Payments),

        new(
            NotificationEvents.RefundProcessed,
            NotificationChannel.InApp,
            "{{amount}} refunded for order {{orderNumber}}",
            "{{amount}} has gone back to the way you paid for order {{orderNumber}}. Banks usually "
            + "take three to five working days to show it.",
            NotificationCategory.Payments),
    ];
}
