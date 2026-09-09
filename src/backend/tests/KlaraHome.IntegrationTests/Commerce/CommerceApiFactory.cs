using KlaraHome.Contracts.Notifications;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Storage;
using KlaraHome.IntegrationTests.Api;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.IntegrationTests.Identity;
using KlaraHome.IntegrationTests.Step8;
using KlaraHome.Modules.Notifications.Infrastructure.Channels;
using KlaraHome.Modules.Payments.Infrastructure.Gateway;
using KlaraHome.Modules.Shipping.Infrastructure.Courier;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The whole API — all eighteen modules — pointed at the migrated test database, with only the
/// four things that would otherwise need a bucket, a mail server, a payment gateway or a courier
/// account replaced.
/// </summary>
/// <remarks>
/// <para>
/// This is the host every Step 29 commerce test runs against, and the substitutions stop at the
/// network boundary on purpose. Everything that decides whether a vendor may be activated, whether
/// stock may be reserved, what a price works out to, which order transitions are legal, whether a
/// refund needs a second approver and what gets written to the ledger is the implementation that
/// runs in production. A test that passes here is a statement about the product, not about a mock.
/// </para>
/// <para>
/// The background processors are off by default. Several of them poll, and a test that asserts on
/// a queue a timer might or might not have drained yet is a test that fails one run in twenty for
/// a reason nobody can reproduce. The tests that need a processor resolve it and run one pass,
/// which asserts the same behaviour and asserts it deterministically.
/// </para>
/// </remarks>
/// <param name="connectionString">The migrated database from the collection fixture.</param>
internal sealed class CommerceApiFactory(string connectionString) : KlaraHomeApiFactory
{
    /// <summary>The key id protected columns are written under by default.</summary>
    public const string FirstKeyId = "commerce-1";

    /// <summary>The key id a rotation moves to. Registered from the start, current only on request.</summary>
    public const string SecondKeyId = "commerce-2";

    /// <summary>A fixed 32-byte AES key. Fixed so two hosts in one test can read each other's rows.</summary>
    private const string FirstKey = "a2xhcmFob21lLWNvbW1lcmNlLXRlc3RzLWtleS0wMSE=";

    /// <summary>The key a rotation moves to. Different bytes, same length.</summary>
    private const string SecondKey = "a2xhcmFob21lLWNvbW1lcmNlLXRlc3RzLWtleS0wMiE=";

    /// <summary>Everything this host has "stored".</summary>
    public InMemoryFileStorage Storage { get; } = new();

    /// <summary>Everything this host has "emailed".</summary>
    public RecordingChannelSender Email { get; } = new(NotificationChannel.Email);

    /// <summary>Everything this host has "texted". Configured, unlike Step 8's.</summary>
    public RecordingChannelSender Sms { get; } = new(NotificationChannel.Sms);

    /// <summary>The gateway, at the boundary.</summary>
    public FakePaymentProvider Gateway { get; } = new();

    /// <summary>The courier, at the boundary.</summary>
    public FakeShippingProvider Courier { get; } = new();

    /// <summary>
    /// A payout rail, at the boundary. Registered but not selected by default — <c>Payouts:Provider</c>
    /// stays blank unless a test opts in through <see cref="Overrides"/>, which is what keeps the
    /// honest-adapter behaviour provable against the real default.
    /// </summary>
    public FakePayoutProvider Payouts { get; } = new();

    /// <summary>The one-time codes this host has sent.</summary>
    public CapturingOtpDispatcher Otp { get; } = new();

    /// <summary>
    /// Feature flags this host pins, by key. Empty means the seeded defaults.
    /// </summary>
    /// <remarks>
    /// Flags are rows in the Platform schema shared by every test in the collection, so turning one
    /// off through the admin API would make each test depend on what the last one left behind. This
    /// overlays the reader instead and leaves the real evaluator in place for every key it does not
    /// name.
    /// </remarks>
    public Dictionary<string, bool> Features { get; } = new(StringComparer.Ordinal);

    /// <summary>Settings this host overrides on top of the defaults below.</summary>
    /// <remarks>
    /// Applied last, so a test that needs one number different — a shorter reservation TTL, a
    /// smaller page cap — sets it here rather than needing its own factory.
    /// </remarks>
    public Dictionary<string, string?> Overrides { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Extra registrations a single test needs, applied after everything else.
    /// </summary>
    /// <remarks>
    /// The escape hatch for the handful of criteria that are about what a module does when a
    /// collaborator <em>throws</em>. Nothing real can be made to throw on demand, and a compensating
    /// release that only ever runs on a well-formed refusal is precisely the bug those criteria exist
    /// to catch. It is empty for every other host, so the rule above — the substitutions stop at the
    /// network boundary — still describes what these tests run against.
    /// </remarks>
    public List<Action<IServiceCollection>> Overlays { get; } = [];

    protected override IDictionary<string, string?> Settings
    {
        get
        {
            var settings = base.Settings;

            foreach (var (key, value) in KlaraHomeSchemaFixture.SharedSettings(connectionString))
            {
                settings[key] = value;
            }

            // The column-encryption key ring. Without it a seller's bank account cannot be stored at
            // all — the module refuses with a named 503 rather than writing a number in clear — so
            // every onboarding test would stop at the same place for a reason unrelated to what it
            // is proving. Two keys, because rotation with an overlapping window is itself a
            // criterion: a row written under the first must still decrypt once the second is current.
            settings["Encryption:CurrentKeyId"] = FirstKeyId;
            settings[$"Encryption:Keys:{FirstKeyId}"] = FirstKey;
            settings[$"Encryption:Keys:{SecondKeyId}"] = SecondKey;

            // Storage and media: credentials so IFileStorage reports itself available and the media
            // endpoints behave as they would against MinIO. The implementation behind them is the
            // in-memory one, so nothing leaves this process.
            settings["Storage:AccessKey"] = "test";
            settings["Storage:SecretKey"] = "test";
            settings["Storage:PublicBaseUrl"] = "https://cdn.example.test/media-public";
            settings["Media:Imgproxy:BaseUrl"] = "https://img.example.test";
            settings["Media:Imgproxy:SourceBaseUrl"] = "http://minio:9000/media-public";
            settings["Media:Imgproxy:Key"] = "6465762d6b6579";
            settings["Media:Imgproxy:Salt"] = "6465762d73616c74";

            // Notifications are queued but not dispatched by a timer. A test that needs one sent
            // runs the dispatcher itself.
            settings["Notifications:DispatcherEnabled"] = "false";
            settings["Email:Provider"] = "smtp";
            settings["Email:FromAddress"] = "no-reply@klarahome.test";
            settings["Email:Smtp:Host"] = "mailpit";

            // The gateway is selected by name, and the name has to be the one the fake answers to,
            // or the registry would hand back the cash-on-delivery provider for a prepaid order.
            settings["Payments:Provider"] = PaymentProviders.Razorpay;
            settings["Payments:EventProcessorEnabled"] = "false";
            settings["Payments:ReconciliationEnabled"] = "false";
            settings["Payments:SettlementIngestionEnabled"] = "false";

            // The courier, likewise. ADR-018: this key is what a deployment switches courier with.
            settings["Shipping:Provider"] = ShippingProviders.Shiprocket;
            settings["Shipping:EventProcessorEnabled"] = "false";
            settings["Shipping:TrackingPollEnabled"] = "false";
            settings["Shipping:ServiceabilityRefreshEnabled"] = "false";

            foreach (var (key, value) in Overrides)
            {
                settings[key] = value;
            }

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
            services.Replace(ServiceDescriptor
                .Singleton<Modules.Identity.Infrastructure.Access.IOtpDispatcher>(Otp));

            services.RemoveAll<IChannelSender>();
            services.AddSingleton<IChannelSender>(Email);
            services.AddSingleton<IChannelSender>(Sms);
            services.AddSingleton<IChannelSender, InAppSender>();

            // The aggregator is replaced; the manual adapter beside it is the real one, because the
            // fallback it provides is exactly what several of these tests exist to prove.
            services.RemoveAll<IShippingProvider>();
            services.AddSingleton<IShippingProvider>(Courier);
            services.AddSingleton<IShippingProvider, ManualShippingProvider>();

            // Likewise: the gateway is replaced, cash on delivery is the real provider.
            services.RemoveAll<IPaymentProvider>();
            services.AddSingleton<IPaymentProvider>(Gateway);
            services.AddSingleton<IPaymentProvider, InternalCodPaymentProvider>();

            // Added alongside the real UnconfiguredPayoutProvider, never replacing it. The registry
            // picks whichever Payouts:Provider names, and the default configuration names neither.
            services.AddSingleton<Modules.Settlements.Infrastructure.Payouts.IPayoutProvider>(Payouts);

            OverrideFeatureFlags(services, Features);

            foreach (var overlay in Overlays)
            {
                overlay(services);
            }
        });
    }

    /// <summary>
    /// Wraps whatever <see cref="IFeatureFlags"/> the module registered, answering from the pinned
    /// map first and delegating everything else.
    /// </summary>
    /// <remarks>
    /// The same technique <c>IdentityApiFactory</c> uses, and for the same reason. All three
    /// descriptor shapes are handled because which one a module happens to use is not something a
    /// test host should depend on.
    /// </remarks>
    private static void OverrideFeatureFlags(IServiceCollection services, IReadOnlyDictionary<string, bool> pinned)
    {
        var registered = services.LastOrDefault(service => service.ServiceType == typeof(IFeatureFlags))
                         ?? throw new InvalidOperationException("No IFeatureFlags is registered.");

        services.Remove(registered);

        services.Add(ServiceDescriptor.Describe(
            typeof(IFeatureFlags),
            provider => new OverriddenFeatureFlags(Original(provider, registered), pinned),
            registered.Lifetime));
    }

    private static IFeatureFlags Original(IServiceProvider provider, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is IFeatureFlags instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return (IFeatureFlags)descriptor.ImplementationFactory(provider);
        }

        return (IFeatureFlags)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);
    }
}
