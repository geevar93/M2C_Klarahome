using KlaraHome.Contracts.Notifications;
using KlaraHome.Infrastructure.Storage;
using KlaraHome.IntegrationTests.Api;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Notifications.Infrastructure.Channels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KlaraHome.IntegrationTests.Step8;

/// <summary>
/// The real API host pointed at the migrated test database, with the two things that would
/// otherwise need a bucket and a mail server replaced.
/// </summary>
/// <remarks>
/// Only the network boundary is substituted: object storage becomes a dictionary, and the channel
/// senders become recorders. Everything that decides whether an upload is accepted, what a
/// rendition URL says, whether a message is suppressed and what is written to the delivery log is
/// the implementation that runs in production.
/// </remarks>
/// <param name="connectionString">The migrated database from the collection fixture.</param>
public sealed class Step8ApiFactory(string connectionString) : KlaraHomeApiFactory
{
    /// <summary>Everything this host has "stored".</summary>
    internal InMemoryFileStorage Storage { get; } = new();

    /// <summary>Everything this host has "sent", by channel.</summary>
    internal RecordingChannelSender Email { get; } = new(NotificationChannel.Email);

    /// <summary>The SMS sender. Left unconfigured by default, which is the deployment's real state.</summary>
    internal RecordingChannelSender Sms { get; } = new(NotificationChannel.Sms) { IsConfigured = false };

    protected override IDictionary<string, string?> Settings
    {
        get
        {
            var settings = base.Settings;

            foreach (var (key, value) in KlaraHomeSchemaFixture.SharedSettings(connectionString))
            {
                settings[key] = value;
            }

            // Credentials, so IFileStorage reports itself available and the media endpoints behave
            // as they would against MinIO. The implementation behind them is the in-memory one, so
            // nothing leaves this process.
            settings["Storage:AccessKey"] = "test";
            settings["Storage:SecretKey"] = "test";
            settings["Storage:PublicBaseUrl"] = "https://cdn.example.test/media-public";

            // Configured and signed, because an unsigned imgproxy deployment is a defect and the
            // tests should be asserting the shape a real one produces.
            settings["Media:Imgproxy:BaseUrl"] = "https://img.example.test";
            settings["Media:Imgproxy:SourceBaseUrl"] = "http://minio:9000/media-public";
            settings["Media:Imgproxy:Key"] = "6465762d6b6579";
            settings["Media:Imgproxy:Salt"] = "6465762d73616c74";

            // A small cap, so the "too large" path is provable without moving ten megabytes.
            settings["Media:MaxImageBytes"] = "4096";

            // The dispatcher runs here, which it does not in the API host it is modelled on. These
            // tests are proving the queue: rendered, claimed, sent, recorded - including the retry
            // schedule. Asserting on a message that was queued and never picked up would prove the
            // first half of the pipeline and call it the whole thing.
            settings["Notifications:DispatcherEnabled"] = "true";
            settings["Notifications:PollIntervalSeconds"] = "1";
            settings["Notifications:RetryBaseSeconds"] = "1";
            settings["Notifications:MaxAttempts"] = "3";

            settings["Email:Provider"] = "smtp";
            settings["Email:FromAddress"] = "no-reply@klarahome.test";
            settings["Email:Smtp:Host"] = "mailpit";

            return settings;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IFileStorage>(Storage));

            // The real senders are removed rather than overlaid: the development SMS sender would
            // otherwise deliver to the recorder, and these tests are asserting what a deployment
            // with no SMS provider does.
            services.RemoveAll<IChannelSender>();
            services.AddSingleton<IChannelSender>(Email);
            services.AddSingleton<IChannelSender>(Sms);
            services.AddSingleton<IChannelSender, InAppSender>();
        });
    }
}
