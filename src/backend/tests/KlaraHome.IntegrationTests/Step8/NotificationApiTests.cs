using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.Contracts.Notifications;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Notifications.Infrastructure.Channels;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.IntegrationTests.Step8;

/// <summary>
/// Step 8's second and third acceptance criteria: a templated message is dispatched and logged,
/// failures retry with backoff, and a channel that cannot deliver says so rather than failing
/// quietly (ADR-017).
/// </summary>
/// <remarks>
/// The dispatcher runs in this host, so these drive the real queue: rendered, claimed with
/// <c>FOR UPDATE SKIP LOCKED</c>, sent, recorded.
/// </remarks>
[Collection(KlaraHomeSchema.CollectionName)]
public sealed class NotificationApiTests(KlaraHomeSchemaFixture fixture) : Step8TestBase(fixture)
{
    [Fact]
    public async Task A_templated_email_is_queued_dispatched_and_logged()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var address = NewEmail("dispatch");

        var response = await admin.PostAsJsonAsync(
            "/api/v1/admin/notifications/test",
            new { channel = "Email", to = address },
            Cancellation);

        response.EnsureSuccessStatusCode();

        var queued = (await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation))
            .EnumerateArray()
            .Single();

        Assert.Equal("Queued", queued.GetProperty("status").GetString());

        var id = queued.GetProperty("messageId").GetString()!;
        var logged = await WaitForStatusAsync(admin, id, "Sent");

        var sent = Factory.Email.Sent.Single(candidate => candidate.Recipient == address);

        // The template rendered: the store name came from settings, not from the caller.
        Assert.Contains(KlaraHomeSchemaFixture.TenantName, sent.Body, StringComparison.Ordinal);
        Assert.Contains(KlaraHomeSchemaFixture.TenantName, sent.Subject!, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", sent.Body, StringComparison.Ordinal);

        Assert.Equal(NotificationEvents.ChannelTest, logged.GetProperty("eventKey").GetString());
        Assert.Equal(1, logged.GetProperty("attempts").GetInt32());

        // §5: personal data is masked in an admin list.
        Assert.NotEqual(address, logged.GetProperty("recipient").GetString());
        Assert.Contains("@klarahome.test", logged.GetProperty("recipient").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_channel_with_no_provider_is_suppressed_rather_than_failed()
    {
        SkipWithoutDocker();

        // The state of an unfunded deployment: the channel exists, the template exists, and there
        // is nobody to hand the message to.
        Factory.Email.IsConfigured = false;

        try
        {
            var admin = await SignedInAdministratorAsync();

            var response = await admin.PostAsJsonAsync(
                "/api/v1/admin/notifications/test",
                new { channel = "Email", to = NewEmail("unprovisioned") },
                Cancellation);

            response.EnsureSuccessStatusCode();

            var message = (await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation))
                .EnumerateArray()
                .Single();

            Assert.Equal("Suppressed", message.GetProperty("status").GetString());
            Assert.Equal("NoProvider", message.GetProperty("suppression").GetString());

            var id = message.GetProperty("messageId").GetString();
            var logged = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/notifications/{id}", Cancellation);

            // Nobody was asked, so nothing is waiting to retry and nothing is an incident.
            Assert.Equal(0, logged.GetProperty("attempts").GetInt32());
            Assert.Equal(JsonValueKind.Null, logged.GetProperty("nextAttemptAt").ValueKind);
            Assert.Equal(JsonValueKind.Null, logged.GetProperty("error").ValueKind);
        }
        finally
        {
            Factory.Email.IsConfigured = true;
        }
    }

    [Fact]
    public async Task An_sms_is_suppressed_because_this_deployment_has_no_sms_account()
    {
        SkipWithoutDocker();

        // The real state of the deployment (docs/08-integrations.md §7): the template exists and is
        // active, and there is no provider to hand it to. Recorded, not dropped, and not retried.
        var admin = await SignedInAdministratorAsync();

        var response = await admin.PostAsJsonAsync(
            "/api/v1/admin/notifications/test",
            new { channel = "Sms", to = "+919876500001" },
            Cancellation);

        response.EnsureSuccessStatusCode();

        var message = (await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation))
            .EnumerateArray()
            .Single();

        Assert.Equal("Suppressed", message.GetProperty("status").GetString());
        Assert.Equal("NoProvider", message.GetProperty("suppression").GetString());
        Assert.Empty(Factory.Sms.Sent);
    }

    [Fact]
    public async Task A_one_time_code_is_sent_inline_and_stored_nowhere()
    {
        SkipWithoutDocker();

        // The Step 7 debt, closed. A sensitive message is rendered, handed to a provider within the
        // request — it cannot wait for a poll, and its body cannot be kept for one — and the row
        // that records the attempt carries neither the body nor any variable's value (ADR-017).
        var address = NewEmail("otp");

        using var scope = Factory.Services.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<INotifier>();

        var results = await notifier.EnqueueAsync(
            new NotificationRequest(
                NotificationEvents.OtpLogin,
                new NotificationRecipient(Email: address),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["storeName"] = "Klara Home",
                    ["minutes"] = "5",
                    ["code"] = "493028",
                },
                [NotificationChannel.Email]),
            Cancellation);

        var queued = results.Single();
        Assert.Equal(NotificationStatus.Sent, queued.Status);

        // It did reach the provider, in full.
        var sent = Factory.Email.Sent.Single(candidate => candidate.Recipient == address);
        Assert.Contains("493028", sent.Body, StringComparison.Ordinal);

        var admin = await SignedInAdministratorAsync();
        var logged = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/v1/admin/notifications/{queued.MessageId}",
            Cancellation);

        // And it is not in the row.
        Assert.Equal(JsonValueKind.Null, logged.GetProperty("subject").ValueKind);
        Assert.DoesNotContain("493028", logged.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_message_with_no_recipient_is_suppressed_rather_than_thrown()
    {
        SkipWithoutDocker();

        // A notification that cannot be sent must never fail the operation that asked for it.
        using var scope = Factory.Services.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<INotifier>();

        var results = await notifier.EnqueueAsync(
            new NotificationRequest(
                NotificationEvents.AccountCreated,
                new NotificationRecipient(Mobile: "+919876500002"),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["storeName"] = "Klara Home",
                    ["name"] = "Asha",
                },
                [NotificationChannel.Email]),
            Cancellation);

        var message = results.Single();

        Assert.Equal(NotificationStatus.Suppressed, message.Status);
        Assert.Equal(NotificationSuppression.NoRecipient, message.Suppression);
    }

    [Fact]
    public async Task A_missing_template_value_fails_the_message_and_names_what_was_missing()
    {
        SkipWithoutDocker();

        using var scope = Factory.Services.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<INotifier>();

        var results = await notifier.EnqueueAsync(
            new NotificationRequest(
                NotificationEvents.AccountCreated,
                new NotificationRecipient(Email: NewEmail("incomplete")),
                new Dictionary<string, string>(StringComparer.Ordinal) { ["storeName"] = "Klara Home" },
                [NotificationChannel.Email]),
            Cancellation);

        var message = results.Single();
        Assert.Equal(NotificationStatus.Failed, message.Status);

        var admin = await SignedInAdministratorAsync();
        var logged = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/v1/admin/notifications/{message.MessageId}",
            Cancellation);

        Assert.Contains("name", logged.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_transient_refusal_is_retried_and_then_given_up_on()
    {
        SkipWithoutDocker();

        Factory.Email.Outcome = SendOutcome.Transient("connection refused");

        try
        {
            var admin = await SignedInAdministratorAsync();

            var response = await admin.PostAsJsonAsync(
                "/api/v1/admin/notifications/test",
                new { channel = "Email", to = NewEmail("refused") },
                Cancellation);

            response.EnsureSuccessStatusCode();

            var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation))
                .EnumerateArray()
                .Single()
                .GetProperty("messageId")
                .GetString()!;

            // Three attempts at a one-second base, then the budget is spent.
            var logged = await WaitForStatusAsync(admin, id, "Failed", TimeSpan.FromSeconds(30));

            Assert.Equal(3, logged.GetProperty("attempts").GetInt32());
            Assert.Equal("connection refused", logged.GetProperty("error").GetString());
            Assert.Equal(JsonValueKind.Null, logged.GetProperty("nextAttemptAt").ValueKind);

            // An operator can put it back in the queue once the provider is fixed.
            Factory.Email.Outcome = null;

            var retried = await admin.PostAsync(
                new Uri($"/api/v1/admin/notifications/{id}/retry", UriKind.Relative),
                content: null,
                Cancellation);

            Assert.Equal(HttpStatusCode.OK, retried.StatusCode);

            var requeued = await retried.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
            Assert.Equal("Queued", requeued.GetProperty("status").GetString());
            Assert.Equal(0, requeued.GetProperty("attempts").GetInt32());

            await WaitForStatusAsync(admin, id, "Sent");
        }
        finally
        {
            Factory.Email.Outcome = null;
        }
    }

    [Fact]
    public async Task A_permanent_refusal_is_not_retried()
    {
        SkipWithoutDocker();

        // Retrying a rejected recipient five times wastes an hour and tells nobody anything new.
        Factory.Email.Outcome = SendOutcome.Permanent("mailbox does not exist");

        try
        {
            var admin = await SignedInAdministratorAsync();

            var response = await admin.PostAsJsonAsync(
                "/api/v1/admin/notifications/test",
                new { channel = "Email", to = NewEmail("rejected") },
                Cancellation);

            response.EnsureSuccessStatusCode();

            var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation))
                .EnumerateArray()
                .Single()
                .GetProperty("messageId")
                .GetString()!;

            var logged = await WaitForStatusAsync(admin, id, "Failed");

            Assert.Equal(1, logged.GetProperty("attempts").GetInt32());
        }
        finally
        {
            Factory.Email.Outcome = null;
        }
    }

    [Fact]
    public async Task A_sensitive_message_cannot_be_re_queued_because_there_is_nothing_to_re_send()
    {
        SkipWithoutDocker();

        Factory.Email.Outcome = SendOutcome.Transient("connection refused");

        try
        {
            using var scope = Factory.Services.CreateScope();
            var notifier = scope.ServiceProvider.GetRequiredService<INotifier>();

            var results = await notifier.EnqueueAsync(
                new NotificationRequest(
                    NotificationEvents.OtpLogin,
                    new NotificationRecipient(Email: NewEmail("otp-retry")),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["storeName"] = "Klara Home",
                        ["minutes"] = "5",
                        ["code"] = "111222",
                    },
                    [NotificationChannel.Email]),
                Cancellation);

            var admin = await SignedInAdministratorAsync();

            var retried = await admin.PostAsync(
                new Uri($"/api/v1/admin/notifications/{results.Single().MessageId}/retry", UriKind.Relative),
                content: null,
                Cancellation);

            // The person asks for a new code instead, which is the only correct answer.
            Assert.Equal(HttpStatusCode.UnprocessableEntity, retried.StatusCode);

            var problem = await retried.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
            Assert.Equal("NOTIFICATION_NOT_RETRYABLE", problem.GetProperty("code").GetString());
        }
        finally
        {
            Factory.Email.Outcome = null;
        }
    }

    [Fact]
    public async Task An_sms_template_cannot_be_activated_without_its_dlt_registration()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        var templates = await admin.GetFromJsonAsync<JsonElement>(
            "/api/v1/admin/notification-templates?channel=Sms",
            Cancellation);

        var template = templates.EnumerateArray()
            .First(candidate => candidate.GetProperty("eventKey").GetString() == NotificationEvents.OtpMobileVerification);

        var id = template.GetProperty("id").GetString();

        // Outside Production the template is active - there is no operator to drop anything - but
        // the missing registration is still reported rather than left to be discovered.
        Assert.Equal("MissingTemplateId", template.GetProperty("dltViolation").GetString());

        var refused = await admin.PutAsJsonAsync(
            $"/api/v1/admin/notification-templates/{id}",
            new
            {
                subject = string.Empty,
                body = template.GetProperty("body").GetString(),
                providerTemplateId = (string?)null,
                isActive = true,
            },
            Cancellation);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);

        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("NOTIFICATION_DLT_INVALID", problem.GetProperty("code").GetString());

        var accepted = await admin.PutAsJsonAsync(
            $"/api/v1/admin/notification-templates/{id}",
            new
            {
                subject = string.Empty,
                body = template.GetProperty("body").GetString(),
                providerTemplateId = "1234567890123456789",
                isActive = true,
            },
            Cancellation);

        accepted.EnsureSuccessStatusCode();

        var updated = await accepted.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.True(updated.GetProperty("isActive").GetBoolean());
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("dltViolation").ValueKind);
        Assert.True(updated.GetProperty("version").GetInt32() > template.GetProperty("version").GetInt32());

        // Put it back, so the collection's shared database is left as it was found. Deactivating is
        // the only way to drop the id again: the editor refuses an active template without one.
        await admin.PutAsJsonAsync(
            $"/api/v1/admin/notification-templates/{id}",
            new
            {
                subject = string.Empty,
                body = template.GetProperty("body").GetString(),
                providerTemplateId = (string?)null,
                isActive = false,
            },
            Cancellation);

        await admin.PutAsJsonAsync(
            $"/api/v1/admin/notification-templates/{id}",
            new
            {
                subject = string.Empty,
                body = template.GetProperty("body").GetString(),
                providerTemplateId = (string?)null,
                isActive = template.GetProperty("isActive").GetBoolean(),
            },
            Cancellation);
    }

    [Fact]
    public async Task The_shipped_templates_are_seeded_and_classified()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var templates = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/notification-templates", Cancellation);
        var all = templates.EnumerateArray().ToList();

        Assert.NotEmpty(all);

        foreach (var key in NotificationEvents.All)
        {
            Assert.Contains(all, template => template.GetProperty("eventKey").GetString() == key);
        }

        var otp = all.Single(template =>
            template.GetProperty("eventKey").GetString() == NotificationEvents.OtpLogin
            && template.GetProperty("channel").GetString() == "Email");

        Assert.True(otp.GetProperty("isSensitive").GetBoolean());
        Assert.Equal("Security", otp.GetProperty("category").GetString());
        Assert.Contains("code", otp.GetProperty("placeholders").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_read_the_delivery_log()
    {
        SkipWithoutDocker();

        var response = await CreateClient().GetAsync(
            new Uri("/api/v1/admin/notifications", UriKind.Relative),
            Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Polls the delivery log until a message reaches a status, or fails saying what it reached.
    /// </summary>
    /// <remarks>
    /// The queue is drained by a background loop, so a test that asserted immediately would be
    /// asserting on a race. Polling the log is also exactly what an operator does.
    /// </remarks>
    private static async Task<JsonElement> WaitForStatusAsync(
        HttpClient admin,
        string messageId,
        string status,
        TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(15));
        var last = default(JsonElement);

        while (DateTimeOffset.UtcNow < deadline)
        {
            last = await admin.GetFromJsonAsync<JsonElement>(
                $"/api/v1/admin/notifications/{messageId}",
                Cancellation);

            if (string.Equals(last.GetProperty("status").GetString(), status, StringComparison.Ordinal))
            {
                return last;
            }

            await Task.Delay(250, Cancellation);
        }

        Assert.Fail(
            $"Message {messageId} never reached '{status}'. Last seen: "
            + $"{last.GetProperty("status").GetString()}, attempts {last.GetProperty("attempts").GetInt32()}, "
            + $"error {last.GetProperty("error")}.");

        return last;
    }
}
