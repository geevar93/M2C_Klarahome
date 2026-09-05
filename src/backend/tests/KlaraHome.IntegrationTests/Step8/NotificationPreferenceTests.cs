using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Step8;

/// <summary>
/// The account surface <c>04-api-specification.md</c> §3.1 lists and Step 7 deliberately left to
/// this module, which owns the channels the preferences select between.
/// </summary>
[Collection(KlaraHomeSchema.CollectionName)]
public sealed class NotificationPreferenceTests(KlaraHomeSchemaFixture fixture) : Step8TestBase(fixture)
{
    [Fact]
    public async Task Every_category_is_listed_even_though_nobody_has_chosen_yet()
    {
        SkipWithoutDocker();

        // A response listing nothing would read as "you receive nothing", which is the opposite of
        // the truth for somebody who has never opened the page.
        var admin = await SignedInAdministratorAsync();

        var preferences = await admin.GetFromJsonAsync<JsonElement>(
            "/api/v1/store/me/notification-preferences",
            Cancellation);

        var categories = preferences.GetProperty("categories").EnumerateArray().ToList();

        Assert.Equal(5, categories.Count);
        Assert.DoesNotContain(categories, entry => entry.GetProperty("category").GetString() == "Security");

        var marketing = categories.Single(entry => entry.GetProperty("category").GetString() == "Marketing");
        var orders = categories.Single(entry => entry.GetProperty("category").GetString() == "Orders");

        Assert.False(marketing.GetProperty("email").GetBoolean());
        Assert.True(orders.GetProperty("email").GetBoolean());
    }

    [Fact]
    public async Task Only_the_channels_this_deployment_can_deliver_on_are_offered()
    {
        SkipWithoutDocker();

        // A toggle for a channel with no provider is a switch that changes nothing.
        var admin = await SignedInAdministratorAsync();

        var preferences = await admin.GetFromJsonAsync<JsonElement>(
            "/api/v1/store/me/notification-preferences",
            Cancellation);

        var available = preferences.GetProperty("availableChannels")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToList();

        Assert.Contains("Email", available);
        Assert.Contains("InApp", available);
        Assert.DoesNotContain("Sms", available);
        Assert.DoesNotContain("WhatsApp", available);
    }

    [Fact]
    public async Task A_choice_is_saved_and_read_back()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        var response = await admin.PutAsJsonAsync(
            "/api/v1/store/me/notification-preferences",
            new { category = "Shipping", email = false, sms = false, whatsApp = false, inApp = true },
            Cancellation);

        response.EnsureSuccessStatusCode();

        var saved = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        var shipping = saved.GetProperty("categories")
            .EnumerateArray()
            .Single(entry => entry.GetProperty("category").GetString() == "Shipping");

        Assert.False(shipping.GetProperty("email").GetBoolean());
        Assert.True(shipping.GetProperty("inApp").GetBoolean());

        var reread = await admin.GetFromJsonAsync<JsonElement>(
            "/api/v1/store/me/notification-preferences",
            Cancellation);

        Assert.False(reread.GetProperty("categories")
            .EnumerateArray()
            .Single(entry => entry.GetProperty("category").GetString() == "Shipping")
            .GetProperty("email")
            .GetBoolean());

        // Leave the shared database as it was found.
        await admin.PutAsJsonAsync(
            "/api/v1/store/me/notification-preferences",
            new { category = "Shipping", email = true, sms = true, whatsApp = true, inApp = true },
            Cancellation);
    }

    [Fact]
    public async Task Security_messages_cannot_be_switched_off()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        var response = await admin.PutAsJsonAsync(
            "/api/v1/store/me/notification-preferences",
            new { category = "Security", email = false, sms = false, whatsApp = false, inApp = false },
            Cancellation);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task An_anonymous_caller_has_no_preferences_to_read()
    {
        SkipWithoutDocker();

        var response = await CreateClient().GetAsync(
            new Uri("/api/v1/store/me/notification-preferences", UriKind.Relative),
            Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
