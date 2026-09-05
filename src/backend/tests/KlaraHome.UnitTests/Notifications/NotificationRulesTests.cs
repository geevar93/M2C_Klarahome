using KlaraHome.Contracts.Notifications;
using KlaraHome.Modules.Notifications.Application;
using KlaraHome.Modules.Notifications.Domain;
using KlaraHome.Modules.Notifications.Infrastructure;
using KlaraHome.Modules.Notifications.Infrastructure.Channels;
using KlaraHome.Modules.Notifications.Infrastructure.Seeding;
using KlaraHome.Modules.Notifications.Infrastructure.Templating;

namespace KlaraHome.UnitTests.Notifications;

/// <summary>
/// The DLT rules an Indian operator applies. Every one of these exists because a non-conforming
/// SMS is <em>dropped</em> rather than rejected: the API returns success, the delivery report says
/// nothing, and the customer never receives their code.
/// </summary>
public sealed class DltRulesTests
{
    [Fact]
    public void A_template_with_no_registered_id_is_refused()
        => Assert.Equal(DltViolation.MissingTemplateId, DltRules.Validate("{{code}} is your code.", null));

    [Fact]
    public void An_id_that_is_not_digits_is_refused()
        => Assert.Equal(DltViolation.MalformedTemplateId, DltRules.Validate("body", "TEMPLATE_ID_HERE"));

    [Fact]
    public void An_id_that_is_too_short_to_be_a_registration_is_refused()
        => Assert.Equal(DltViolation.MalformedTemplateId, DltRules.Validate("body", "12345"));

    [Fact]
    public void A_nineteen_digit_registration_is_accepted()
        => Assert.Equal(DltViolation.None, DltRules.Validate("{{code}} is your code.", "1234567890123456789"));

    [Fact]
    public void A_body_longer_than_an_operator_accepts_is_refused()
    {
        var body = new string('x', DltRules.MaxBodyLength + 1);

        Assert.Equal(DltViolation.BodyTooLong, DltRules.Validate(body, "1234567890123456789"));
    }

    [Fact]
    public void More_variables_than_a_registration_may_declare_are_refused()
    {
        var body = string.Concat(Enumerable.Range(0, DltRules.MaxVariables + 1).Select(index => $"{{{{v{index}}}}}"));

        Assert.Equal(DltViolation.TooManyVariables, DltRules.Validate(body, "1234567890123456789"));
    }

    [Fact]
    public void A_variable_longer_than_thirty_characters_is_refused_at_send_time()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = new('x', DltRules.MaxVariableLength + 1),
        };

        Assert.Equal(DltViolation.VariableTooLong, DltRules.ValidateValues(values, out var offending));
        Assert.Equal("name", offending);
    }

    [Fact]
    public void Values_within_the_limit_pass()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["code"] = "493028" };

        Assert.Equal(DltViolation.None, DltRules.ValidateValues(values, out var offending));
        Assert.Null(offending);
    }

    [Fact]
    public void Every_shipped_sms_template_would_satisfy_the_length_rules_once_registered()
    {
        // The id is what is missing, not the wording: the day a DLT registration exists, these
        // templates must be usable without being rewritten.
        var smsTemplates = DefaultTemplates.All
            .Where(template => template.Channel == NotificationChannel.Sms)
            .ToList();

        Assert.NotEmpty(smsTemplates);

        foreach (var template in smsTemplates)
        {
            Assert.Equal(DltViolation.None, DltRules.Validate(template.Body, "1234567890123456789"));
        }
    }
}

/// <summary>The retry schedule the notification dispatcher follows.</summary>
public sealed class NotificationBackoffTests
{
    private static readonly NotificationOptions Options = new()
    {
        RetryBaseSeconds = 30,
        RetryMaxSeconds = 3600,
        MaxAttempts = 6,
    };

    [Fact]
    public void The_delay_doubles_with_each_attempt()
    {
        var first = NotificationDispatcher.BackoffFor(1, Options, new Random(1));
        var second = NotificationDispatcher.BackoffFor(2, Options, new Random(1));
        var third = NotificationDispatcher.BackoffFor(3, Options, new Random(1));

        Assert.True(second > first);
        Assert.True(third > second);
    }

    [Fact]
    public void The_delay_is_capped()
    {
        var late = NotificationDispatcher.BackoffFor(20, Options, new Random(1));

        Assert.True(late <= TimeSpan.FromSeconds(Options.RetryMaxSeconds));
    }

    [Fact]
    public void Jitter_keeps_the_delay_inside_the_top_quarter_of_its_window()
    {
        // The jitter matters more than the doubling: without it, a backlog released by a
        // recovering SMTP host all retries in the same second.
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            var window = Math.Min(Options.RetryBaseSeconds * Math.Pow(2, attempt - 1), Options.RetryMaxSeconds);

            for (var run = 0; run < 20; run++)
            {
                var delay = NotificationDispatcher.BackoffFor(attempt, Options, Random.Shared);

                Assert.InRange(delay.TotalSeconds, window * 0.75, window);
            }
        }
    }

    [Fact]
    public void Two_messages_do_not_get_the_same_delay()
    {
        var delays = Enumerable
            .Range(0, 50)
            .Select(_ => NotificationDispatcher.BackoffFor(3, Options, Random.Shared).Ticks)
            .Distinct()
            .Count();

        Assert.True(delays > 40, $"Expected the jitter to spread the retries; got {delays} distinct delays of 50.");
    }
}

/// <summary>The delivery log is an operator's screen, so it masks what §5 requires it to mask.</summary>
public sealed class NotificationMaskingTests
{
    [Theory]
    [InlineData("asha.kumar@example.com", "as********@example.com")]
    [InlineData("a@example.com", "a*@example.com")]
    public void An_email_address_keeps_its_domain_and_loses_its_local_part(string input, string expected)
        => Assert.Equal(expected, NotificationLogProjection.Mask(input));

    [Fact]
    public void A_mobile_number_keeps_only_its_last_four_digits()
        => Assert.Equal("*********6789", NotificationLogProjection.Mask("+919876543210"[..9] + "6789"));

    [Fact]
    public void A_short_value_is_masked_entirely()
        => Assert.Equal("****", NotificationLogProjection.Mask("1234"));

    [Fact]
    public void An_empty_recipient_masks_to_nothing()
        => Assert.Equal(string.Empty, NotificationLogProjection.Mask(string.Empty));
}

/// <summary>Preferences: what a person may switch off, and what they may not.</summary>
public sealed class NotificationPreferenceTests
{
    [Fact]
    public void Marketing_is_the_only_category_that_is_off_by_default()
    {
        foreach (var category in Enum.GetValues<NotificationCategory>())
        {
            var expected = category != NotificationCategory.Marketing;

            Assert.Equal(expected, NotificationPreference.DefaultFor(category));
        }
    }

    [Fact]
    public void Security_is_not_offered_as_a_choice()
    {
        // Somebody who has turned off notice of a password change has turned off the only warning
        // they would get that someone else changed it.
        Assert.DoesNotContain(
            PreferenceCatalogue.All,
            entry => entry.Category == NotificationCategory.Security);
    }

    [Fact]
    public void A_new_preference_starts_at_the_category_default()
    {
        var marketing = NotificationPreference.For(Guid.NewGuid(), NotificationCategory.Marketing);
        var orders = NotificationPreference.For(Guid.NewGuid(), NotificationCategory.Orders);

        Assert.False(marketing.Allows(NotificationChannel.Email));
        Assert.True(orders.Allows(NotificationChannel.Email));
    }

    [Fact]
    public void Each_channel_is_answered_separately()
    {
        var preference = NotificationPreference.For(Guid.NewGuid(), NotificationCategory.Orders);
        preference.Set(email: true, sms: false, whatsApp: false, inApp: true);

        Assert.True(preference.Allows(NotificationChannel.Email));
        Assert.False(preference.Allows(NotificationChannel.Sms));
        Assert.False(preference.Allows(NotificationChannel.WhatsApp));
        Assert.True(preference.Allows(NotificationChannel.InApp));
    }
}

/// <summary>The declared events and the templates that render them must not drift apart.</summary>
public sealed class NotificationCatalogueTests
{
    [Fact]
    public void Every_declared_event_has_at_least_one_template()
    {
        var missing = NotificationEvents.All
            .Where(key => !DefaultTemplates.All.Any(template =>
                string.Equals(template.EventKey, key, StringComparison.Ordinal)))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "An event with no template is a notification that is silently not sent. Missing: "
            + string.Join(", ", missing));
    }

    [Fact]
    public void Every_template_names_a_declared_event()
    {
        var undeclared = DefaultTemplates.All
            .Where(template => !NotificationEvents.Contains(template.EventKey))
            .Select(template => template.EventKey)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            undeclared.Count == 0,
            "A template for an event nobody raises is wording an operator can edit and never see. "
            + "Undeclared: " + string.Join(", ", undeclared));
    }

    [Fact]
    public void No_two_templates_claim_the_same_event_and_channel()
    {
        var duplicates = DefaultTemplates.All
            .GroupBy(template => (template.EventKey, template.Channel))
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key.EventKey}/{group.Key.Channel}")
            .ToList();

        Assert.True(duplicates.Count == 0, "Duplicated: " + string.Join(", ", duplicates));
    }

    [Fact]
    public void Every_template_carrying_a_one_time_code_is_marked_sensitive()
    {
        // Sensitivity is what keeps the rendered body out of the database. A template that
        // substitutes {{code}} or {{link}} and is not marked would store the secret.
        var leaking = DefaultTemplates.All
            .Where(template => !template.IsSensitive)
            .Where(template => TemplateRenderer.PlaceholdersIn(template.Body)
                .Concat(TemplateRenderer.PlaceholdersIn(template.Subject))
                .Any(name => name is "code" or "link"))
            .Select(template => $"{template.EventKey}/{template.Channel}")
            .ToList();

        Assert.True(
            leaking.Count == 0,
            "These templates substitute a secret but are not marked sensitive, so their rendered "
            + "body would be stored: " + string.Join(", ", leaking));
    }

    [Fact]
    public void Every_channel_that_costs_money_is_gated_by_a_flag()
    {
        Assert.NotNull(NotificationFeatures.FlagFor(NotificationChannel.Email));
        Assert.NotNull(NotificationFeatures.FlagFor(NotificationChannel.Sms));
        Assert.NotNull(NotificationFeatures.FlagFor(NotificationChannel.WhatsApp));

        // In-application messages have no provider, no cost and no third party, so there would be
        // nothing for an operator to decide.
        Assert.Null(NotificationFeatures.FlagFor(NotificationChannel.InApp));
    }

    [Fact]
    public void Every_declared_flag_ships_on()
    {
        // The same rule the Identity flags follow: this is what the product does when it is fully
        // provisioned, and turning one off should be a decision an operator makes.
        Assert.All(NotificationFeatures.All, flag => Assert.True(flag.Enabled));
    }
}

/// <summary>The message state machine, which is where "suppressed" earns its keep.</summary>
public sealed class NotificationMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_message_is_queued_and_due_immediately()
    {
        var message = NotificationMessage.Queue(Now, "test", NotificationChannel.Email, "a@b.test");

        Assert.Equal(NotificationStatus.Queued, message.Status);
        Assert.Equal(Now, message.NextAttemptAt);
        Assert.True(message.IsPending);
    }

    [Fact]
    public void A_sensitive_message_keeps_neither_its_subject_nor_its_body()
    {
        var message = NotificationMessage.Queue(Now, "otp", NotificationChannel.Sms, "+919000000000");

        message.Describe("Your code", "493028 is your code.", "{\"code\":\"[redacted]\"}", sensitive: true);

        Assert.Null(message.Subject);
        Assert.Null(message.Body);
        Assert.DoesNotContain("493028", message.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public void An_ordinary_message_keeps_what_was_sent()
    {
        var message = NotificationMessage.Queue(Now, "order", NotificationChannel.Email, "a@b.test");

        message.Describe("Order confirmed", "<p>Thanks</p>", "{}", sensitive: false);

        Assert.Equal("Order confirmed", message.Subject);
        Assert.Equal("<p>Thanks</p>", message.Body);
    }

    [Fact]
    public void A_suppressed_message_stops_being_pending_and_records_why()
    {
        var message = NotificationMessage.Queue(Now, "otp", NotificationChannel.Sms, "+919000000000");

        message.Suppress(NotificationSuppression.NoProvider);

        Assert.Equal(NotificationStatus.Suppressed, message.Status);
        Assert.Equal(NotificationSuppression.NoProvider, message.Suppression);
        Assert.Null(message.NextAttemptAt);
        Assert.False(message.IsPending);
    }

    [Fact]
    public void An_attempt_is_counted_when_it_starts_not_when_it_ends()
    {
        // So a message that reliably kills the process still consumes its budget.
        var message = NotificationMessage.Queue(Now, "order", NotificationChannel.Email, "a@b.test");

        message.BeginAttempt();

        Assert.Equal(1, message.Attempts);
        Assert.Equal(NotificationStatus.Sending, message.Status);
        Assert.Null(message.NextAttemptAt);
    }

    [Fact]
    public void A_retry_returns_the_message_to_the_queue_with_a_due_time()
    {
        var message = NotificationMessage.Queue(Now, "order", NotificationChannel.Email, "a@b.test");
        message.BeginAttempt();

        message.Retry(Now.AddSeconds(30), "connection refused");

        Assert.Equal(NotificationStatus.Queued, message.Status);
        Assert.Equal(Now.AddSeconds(30), message.NextAttemptAt);
        Assert.Equal("connection refused", message.Error);
    }

    [Fact]
    public void Requeueing_clears_the_attempt_count_and_the_suppression()
    {
        var message = NotificationMessage.Queue(Now, "order", NotificationChannel.Email, "a@b.test");
        message.BeginAttempt();
        message.Suppress(NotificationSuppression.ChannelDisabled);

        message.Requeue(Now.AddMinutes(1));

        Assert.Equal(NotificationStatus.Queued, message.Status);
        Assert.Equal(NotificationSuppression.None, message.Suppression);
        Assert.Equal(0, message.Attempts);
    }

    [Fact]
    public void A_provider_error_is_truncated_rather_than_rejected_by_the_column()
    {
        var message = NotificationMessage.Queue(Now, "order", NotificationChannel.Email, "a@b.test");

        message.Failed(new string('x', 5000));

        Assert.Equal(2000, message.Error!.Length);
    }
}

/// <summary>The plain-text alternative the SMTP sender builds from an HTML body.</summary>
public sealed class HtmlToTextTests
{
    [Fact]
    public void Tags_are_dropped_and_block_boundaries_become_line_breaks()
    {
        var text = SmtpEmailSender.HtmlToText("<p>Hello Asha,</p><p>Your order shipped.</p>");

        Assert.Contains("Hello Asha,", text, StringComparison.Ordinal);
        Assert.Contains("Your order shipped.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<p>", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Entities_are_decoded()
        => Assert.Equal("Salt & Pepper", SmtpEmailSender.HtmlToText("<p>Salt &amp; Pepper</p>"));

    [Fact]
    public void A_line_break_survives_as_a_line_break()
        => Assert.Contains('\n', SmtpEmailSender.HtmlToText("one<br>two"));
}
