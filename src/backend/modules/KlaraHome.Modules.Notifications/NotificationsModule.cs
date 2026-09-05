using KlaraHome.Contracts.Notifications;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Options;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Persistence.Seeding;
using KlaraHome.Modules.Notifications.Endpoints;
using KlaraHome.Modules.Notifications.Infrastructure;
using KlaraHome.Modules.Notifications.Infrastructure.Channels;
using KlaraHome.Modules.Notifications.Infrastructure.Persistence;
using KlaraHome.Modules.Notifications.Infrastructure.Seeding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KlaraHome.Modules.Notifications;

/// <summary>
/// Templated, queued, retried delivery on every channel — and an honest record of what could not
/// be delivered (ADR-017).
/// </summary>
/// <remarks>
/// <para>
/// This is the module that retires <c>LoggingOtpDispatcher</c>. A one-time code is now rendered
/// from an editable template, delivered inline because it cannot be stored, and recorded with its
/// body redacted — rather than written to the application log, which
/// <c>07-security-compliance.md</c> §3 forbids outright.
/// </para>
/// <para>
/// It ships with no SMS and no WhatsApp adapter, because this deployment has no account for either
/// (<c>08-integrations.md</c> §7). Those channels are not broken and not stubbed: every message on
/// them is recorded as <c>Suppressed / NoProvider</c>, which is a queryable statement of what the
/// platform tried to say and could not. Email works, over SMTP, because SMTP needs no paid account.
/// </para>
/// </remarks>
public sealed class NotificationsModule : IModule
{
    /// <summary>The Postgres schema this module owns.</summary>
    public const string SchemaName = "notifications";

    /// <inheritdoc />
    public string Name => "Notifications";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <summary>
    /// After Platform, Identity and Media. It reads settings and flags, records who edited a
    /// template, and is called by Identity to deliver a code.
    /// </summary>
    public int Order => 40;

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<NotificationsDbContext>(configuration, this);
        services.AddValidatedOptions<NotificationOptions>(configuration, NotificationOptions.SectionName);
        services.AddValidatedOptions<EmailOptions>(configuration, EmailOptions.SectionName);
        services.AddValidatedOptions<SmsOptions>(configuration, SmsOptions.SectionName);

        services.AddSingleton<IFeatureFlagSource, NotificationFeatureFlagSource>();

        services.AddScoped<ChannelRouter>();
        services.AddScoped<INotifier, Notifier>();

        AddChannelSenders(services, configuration);

        services.AddDataSeeder<NotificationTemplateSeeder>();
        services.AddHostedService<NotificationDispatcher>();
    }

    /// <summary>
    /// Registers one sender per channel this deployment can actually use.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A channel with no sender is not a missing registration to work around — it is the answer.
    /// The router reports it as unavailable and every message on it is recorded as
    /// <c>Suppressed / NoProvider</c>.
    /// </para>
    /// <para>
    /// <b>The development SMS route is bound to the environment, not to a setting.</b> Outside
    /// Production, and only when no real SMS provider is configured, an SMS is delivered to the
    /// mail catcher so a developer can complete an OTP sign-in. Production registers nothing for
    /// SMS. Getting this wrong would send one customer's sign-in code to an operations mailbox, so
    /// it is decided here, once, from <see cref="IHostEnvironment"/> rather than from a flag
    /// somebody could set.
    /// </para>
    /// </remarks>
    private static void AddChannelSenders(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<SmtpEmailSender>();
        services.AddScoped<IChannelSender>(provider => provider.GetRequiredService<SmtpEmailSender>());

        // The delivery log is the inbox, so this channel is always available and carries nothing.
        services.AddScoped<IChannelSender, InAppSender>();

        var sms = configuration.GetSection(SmsOptions.SectionName).Get<SmsOptions>() ?? new SmsOptions();
        var environment = configuration[HostDefaults.EnvironmentKey];
        var isProduction = string.Equals(environment, Environments.Production, StringComparison.OrdinalIgnoreCase);

        if (!sms.IsConfigured && !isProduction)
        {
            services.AddScoped<IChannelSender, DevelopmentSmsSender>();
        }
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapStoreNotificationEndpoints();
        endpoints.MapAdminNotificationEndpoints();
    }
}
