using KlaraHome.Contracts.Notifications;
using KlaraHome.Infrastructure.Persistence.Seeding;
using KlaraHome.Modules.Notifications.Domain;
using KlaraHome.Modules.Notifications.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace KlaraHome.Modules.Notifications.Infrastructure.Seeding;

/// <summary>
/// Creates the template rows every deployment needs, and reasserts the properties an operator must
/// not change.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent in a particular way, and the split is the point. A template that does not exist is
/// created with the shipped wording. A template that <em>does</em> exist keeps its wording — an
/// operator's edits survive every release — but has its category, sensitivity and transactional
/// flag reasserted, because those three decide whether a message can be opted out of and whether
/// its text may be stored. Letting a copy-editing screen change them would turn a wording change
/// into a privacy change.
/// </para>
/// <para>
/// <b>In Production, SMS templates are seeded inactive.</b> An active one needs a DLT id this
/// deployment does not have, and an unregistered SMS is <em>dropped</em> by an Indian operator
/// rather than rejected — so registering one is a deliberate act in the admin surface rather than
/// something a deploy can do (docs/08-integrations.md §3.1). Outside Production they are seeded
/// active, because there is no operator to drop anything: the message goes to the development mail
/// catcher, and an inactive template would make a local mobile sign-in impossible (ADR-017).
/// </para>
/// </remarks>
/// <param name="context">The Notifications data context.</param>
/// <param name="environment">Decides whether an SMS template may start active.</param>
internal sealed class NotificationTemplateSeeder(
    NotificationsDbContext context,
    IHostEnvironment environment) : IDataSeeder
{
    /// <inheritdoc />
    public string Name => "Notifications.Templates";

    /// <inheritdoc />
    public int Order => 40;

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var existing = await context.Templates
            .Where(template => template.Locale == DefaultTemplates.Locale)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var descriptor in DefaultTemplates.All)
        {
            var template = existing.FirstOrDefault(candidate =>
                string.Equals(candidate.EventKey, descriptor.EventKey, StringComparison.Ordinal)
                && candidate.Channel == descriptor.Channel);

            if (template is null)
            {
                template = NotificationTemplate.Declare(
                    descriptor.EventKey,
                    descriptor.Channel,
                    DefaultTemplates.Locale,
                    descriptor.Subject,
                    descriptor.Body,
                    descriptor.Category);

                // In Production an SMS template cannot start active: without a DLT id an Indian
                // operator drops the message rather than rejecting it, so registering one has to be
                // a deliberate act in the admin surface rather than something a deploy can do.
                // Outside Production there is no operator - the message goes to the mail catcher -
                // and leaving these inactive would make a local mobile sign-in impossible, which is
                // exactly what ADR-017 promised it would not.
                if (descriptor.Channel == NotificationChannel.Sms && environment.IsProduction())
                {
                    template.Revise(descriptor.Subject, descriptor.Body, providerTemplateId: null, isActive: false);
                }

                context.Templates.Add(template);
            }

            template.Classify(descriptor.Category, descriptor.IsSensitive, descriptor.IsTransactional);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
