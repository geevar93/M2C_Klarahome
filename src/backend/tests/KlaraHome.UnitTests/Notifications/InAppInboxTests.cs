using KlaraHome.Contracts.Notifications;
using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Payments;
using KlaraHome.Modules.Notifications.Application;
using KlaraHome.Modules.Notifications.Domain;
using KlaraHome.Modules.Notifications.Infrastructure.Events;
using KlaraHome.Modules.Notifications.Infrastructure.Seeding;
using KlaraHome.Modules.Notifications.Infrastructure.Templating;

namespace KlaraHome.UnitTests.Notifications;

/// <summary>
/// The read marker the in-app inbox is built on.
/// </summary>
/// <remarks>
/// The whole of the inbox's write side is this one method. Everything else — the scope, the
/// keyset, the bulk update — is a query, and a query against a partitioned table is proved against
/// a database rather than here.
/// </remarks>
public sealed class NotificationReadStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_message_is_unread()
        => Assert.Null(Queue().ReadAt);

    [Fact]
    public void Reading_stamps_the_time()
    {
        var message = Queue();

        message.MarkRead(Now);

        Assert.Equal(Now, message.ReadAt);
    }

    [Fact]
    public void Reading_twice_keeps_the_first_time()
    {
        // The bulk mark-read runs over whatever is unread, and the bell calls it freely. If a
        // second read moved the timestamp, "when did they first see this" would mean "when did
        // they last open their inbox".
        var message = Queue();

        message.MarkRead(Now);
        message.MarkRead(Now.AddHours(3));

        Assert.Equal(Now, message.ReadAt);
    }

    private static NotificationMessage Queue()
        => NotificationMessage.Queue(Now, NotificationEvents.OrderPlaced, NotificationChannel.InApp, "user");
}

/// <summary>
/// The order and payment messages a shopper is told about in the application.
/// </summary>
/// <remarks>
/// These events are the reason the inbox has anything in it. The assertions here are the two ways
/// the arrangement silently breaks: an event with no template sends nothing, and a template whose
/// placeholders the handler does not supply fails one message at a time in the delivery log, where
/// nobody is looking.
/// </remarks>
public sealed class OrderAndPaymentNotificationTests
{
    /// <summary>Every event key these handlers raise.</summary>
    private static readonly string[] EventKeys =
    [
        NotificationEvents.OrderPlaced,
        NotificationEvents.OrderCancelled,
        NotificationEvents.OrderShipped,
        NotificationEvents.OrderOutForDelivery,
        NotificationEvents.OrderDelivered,
        NotificationEvents.OrderDeliveryFailed,
        NotificationEvents.PaymentCaptured,
        NotificationEvents.PaymentFailed,
        NotificationEvents.RefundProcessed,
    ];

    /// <summary>The same keys, as xUnit wants them.</summary>
    public static TheoryData<string> CustomerEventKeys => [.. EventKeys];

    [Theory]
    [MemberData(nameof(CustomerEventKeys))]
    public void Every_order_and_payment_event_has_an_in_app_template(string eventKey)
        => Assert.Contains(
            DefaultTemplates.All,
            template => template.EventKey == eventKey && template.Channel == NotificationChannel.InApp);

    [Fact]
    public void The_order_placed_template_renders_from_what_the_handler_supplies()
        => AssertRenders(NotificationEvents.OrderPlaced, OrderNotificationHandlers.VariablesFor(Placed()));

    [Fact]
    public void The_cancellation_template_renders_from_what_the_handler_supplies()
        => AssertRenders(NotificationEvents.OrderCancelled, OrderNotificationHandlers.VariablesFor(Cancelled(null)));

    [Theory]
    [InlineData("Shipped", NotificationEvents.OrderShipped)]
    [InlineData("OutForDelivery", NotificationEvents.OrderOutForDelivery)]
    [InlineData("Delivered", NotificationEvents.OrderDelivered)]
    [InlineData("DeliveryFailed", NotificationEvents.OrderDeliveryFailed)]
    public void Each_notifiable_status_renders_its_own_template(string status, string eventKey)
    {
        Assert.Equal(eventKey, OrderNotificationHandlers.NotifiableStatuses[status]);

        AssertRenders(eventKey, OrderNotificationHandlers.VariablesFor(StatusChanged(status)));
    }

    [Theory]
    [InlineData("Confirmed")]
    [InlineData("Processing")]
    [InlineData("Packed")]
    [InlineData("RtoInitiated")]
    [InlineData("Closed")]
    public void A_state_the_shopper_has_no_use_for_produces_nothing(string status)
        => Assert.False(OrderNotificationHandlers.NotifiableStatuses.ContainsKey(status));

    [Fact]
    public void The_capture_template_renders_from_what_the_handler_supplies()
        => AssertRenders(
            NotificationEvents.PaymentCaptured,
            PaymentNotificationHandlers.VariablesFor(Captured()));

    [Fact]
    public void The_failure_template_renders_from_what_the_handler_supplies()
        => AssertRenders(
            NotificationEvents.PaymentFailed,
            PaymentNotificationHandlers.VariablesFor(Failed(null)));

    [Fact]
    public void The_refund_template_renders_from_what_the_handler_supplies()
        => AssertRenders(
            NotificationEvents.RefundProcessed,
            PaymentNotificationHandlers.VariablesFor(Refunded()));

    [Fact]
    public void A_payment_failure_with_no_reason_still_says_something()
    {
        // The template's sentence starts with {{reason}}. An empty one would render a message that
        // opens mid-sentence, which is worse than a generic line.
        var variables = PaymentNotificationHandlers.VariablesFor(Failed(null));

        Assert.False(string.IsNullOrWhiteSpace(variables["reason"]));
    }

    [Fact]
    public void A_cancellation_with_no_reason_renders_without_a_gap()
    {
        // The opposite choice, and deliberately: this template's {{reason}} sits between two
        // complete sentences, so nothing is the right thing to say when there is no reason.
        var variables = OrderNotificationHandlers.VariablesFor(Cancelled(null));

        Assert.Equal(string.Empty, variables["reason"]);
    }

    [Fact]
    public void A_reason_the_domain_gave_is_passed_through_trimmed()
        => Assert.Equal(
            "Out of stock",
            OrderNotificationHandlers.VariablesFor(Cancelled("  Out of stock  "))["reason"]);

    [Fact]
    public void The_shipping_updates_honour_the_preference_and_the_money_ones_do_not()
    {
        // The distinction the preference centre is for. Somebody who has turned off tracking
        // chatter has not turned off being told that they were charged.
        foreach (var eventKey in new[]
                 {
                     NotificationEvents.OrderShipped,
                     NotificationEvents.OrderOutForDelivery,
                     NotificationEvents.OrderDelivered,
                     NotificationEvents.OrderDeliveryFailed,
                 })
        {
            Assert.False(InApp(eventKey).IsTransactional);
            Assert.Equal(NotificationCategory.Shipping, InApp(eventKey).Category);
        }

        foreach (var eventKey in new[]
                 {
                     NotificationEvents.OrderPlaced,
                     NotificationEvents.OrderCancelled,
                     NotificationEvents.PaymentCaptured,
                     NotificationEvents.PaymentFailed,
                     NotificationEvents.RefundProcessed,
                 })
        {
            Assert.True(InApp(eventKey).IsTransactional);
        }
    }

    [Fact]
    public void No_order_or_payment_message_is_sent_by_email_yet()
    {
        // This deployment has never emailed a shopper about an order. Seeding an email template
        // would start doing so on the next deploy, addressed to every customer at once - a
        // behaviour change that must be somebody's decision rather than a side effect of this one.
        var emailed = EventKeys
            .Where(eventKey => DefaultTemplates.All.Any(template =>
                template.EventKey == eventKey && template.Channel == NotificationChannel.Email))
            .ToList();

        Assert.Empty(emailed);
    }

    /// <summary>Renders the shipped in-app template and asserts nothing was left unsubstituted.</summary>
    private static void AssertRenders(string eventKey, Dictionary<string, string> variables)
    {
        var template = InApp(eventKey);

        var body = TemplateRenderer.Render(template.Body, variables, escapeHtml: false);
        var subject = TemplateRenderer.Render(template.Subject, variables, escapeHtml: false);

        Assert.True(body.IsSuccess, $"{eventKey} body is missing: {string.Join(", ", body.MissingVariables)}");
        Assert.True(
            subject.IsSuccess,
            $"{eventKey} subject is missing: {string.Join(", ", subject.MissingVariables)}");
    }

    private static TemplateDescriptor InApp(string eventKey)
        => DefaultTemplates.All.Single(template =>
            template.EventKey == eventKey && template.Channel == NotificationChannel.InApp);

    private static OrderPlaced Placed()
        => new(
            Guid.NewGuid(),
            "KH-1001",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "PendingPayment",
            "Prepaid",
            1299.00m,
            1299.00m,
            "INR",
            [Guid.NewGuid()],
            DateTimeOffset.UtcNow);

    private static SubOrderCancelled Cancelled(string? reason)
        => new(
            Guid.NewGuid(),
            "KH-1001",
            Guid.NewGuid(),
            "KH-1001-1",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "customer",
            reason,
            IsPartial: false,
            WasConfirmed: true,
            499.00m,
            "INR",
            []);

    private static SubOrderStatusChanged StatusChanged(string toStatus)
        => new(
            Guid.NewGuid(),
            "KH-1001",
            Guid.NewGuid(),
            "KH-1001-1",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Confirmed",
            toStatus,
            toStatus,
            "vendor",
            null);

    private static PaymentCaptured Captured()
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "KH-1001",
            Guid.NewGuid(),
            "razorpay",
            "upi",
            "pay_123",
            1299.00m,
            "INR",
            DateTimeOffset.UtcNow);

    private static PaymentFailed Failed(string? reason)
        => new(Guid.NewGuid(), Guid.NewGuid(), "KH-1001", Guid.NewGuid(), "GW_02", reason, DateTimeOffset.UtcNow);

    private static RefundProcessed Refunded()
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "KH-1001",
            null,
            null,
            Guid.NewGuid(),
            499.00m,
            "INR",
            "rfnd_123",
            DateTimeOffset.UtcNow);
}

/// <summary>
/// How an amount is written into a message a shopper reads.
/// </summary>
public sealed class MoneyFormattingTests
{
    [Fact]
    public void Rupees_get_the_symbol_and_the_Indian_grouping()
        => Assert.Equal("₹1,29,999.00", Money.Format(129999.00m, "INR"));

    [Fact]
    public void A_currency_this_store_does_not_trade_in_keeps_its_code()
        => Assert.Equal("USD 1,299.00", Money.Format(1299.00m, "USD"));

    [Fact]
    public void A_missing_code_does_not_leave_a_leading_space()
        => Assert.Equal("1,299.00", Money.Format(1299.00m, null));
}

/// <summary>
/// What the inbox response says a message is about.
/// </summary>
public sealed class InboxCategoryTests
{
    [Fact]
    public void A_shipped_message_is_categorised_as_shipping()
        => Assert.Equal("Shipping", InboxProjection.CategoryFor(NotificationEvents.OrderShipped));

    [Fact]
    public void A_refund_is_categorised_as_payments()
        => Assert.Equal("Payments", InboxProjection.CategoryFor(NotificationEvents.RefundProcessed));

    [Fact]
    public void A_key_an_operator_added_a_template_for_is_uncategorised_rather_than_wrong()
        => Assert.Null(InboxProjection.CategoryFor("vendor.something.an-operator-invented"));
}
