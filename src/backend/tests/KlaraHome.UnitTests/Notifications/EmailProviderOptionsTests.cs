using KlaraHome.Modules.Notifications.Infrastructure;

namespace KlaraHome.UnitTests.Notifications;

/// <summary>
/// The email provider switch: <c>none</c> sends nothing, <c>smtp</c> needs a host and a sender, and
/// a misspelling is refused rather than silently stopping all email.
/// </summary>
public sealed class EmailProviderOptionsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("none")]
    [InlineData("None")]
    [InlineData("smtp")]
    [InlineData("SMTP")]
    public void A_known_provider_is_accepted(string provider)
        => Assert.True(new EmailOptions { Provider = provider }.IsKnownProvider);

    [Theory]
    [InlineData("smpt")]
    [InlineData("log")]
    [InlineData("ses")]
    public void An_unknown_provider_is_refused(string provider)
        => Assert.False(new EmailOptions { Provider = provider }.IsKnownProvider);

    [Theory]
    [InlineData("")]
    [InlineData("none")]
    [InlineData("None")]
    public void No_provider_means_email_cannot_be_sent_even_when_a_host_and_sender_are_present(string provider)
    {
        var options = new EmailOptions
        {
            Provider = provider,
            FromAddress = "noreply@klarahome.in",
            Smtp = { Host = "smtp.example.test" },
        };

        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void Smtp_with_a_host_and_a_sender_is_configured()
    {
        var options = new EmailOptions
        {
            Provider = "smtp",
            FromAddress = "noreply@klarahome.in",
            Smtp = { Host = "smtp.example.test" },
        };

        Assert.True(options.IsConfigured);
    }

    [Fact]
    public void Smtp_without_a_host_is_not_configured()
        => Assert.False(new EmailOptions { Provider = "smtp", FromAddress = "noreply@klarahome.in" }.IsConfigured);
}
