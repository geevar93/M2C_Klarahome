using FluentValidation;
using KlaraHome.Contracts.Notifications;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Notifications.Infrastructure.Persistence;
using KlaraHome.Modules.Notifications.Infrastructure.Templating;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Notifications.Application;

/// <summary>A template, as the admin surface shows it.</summary>
/// <param name="Id">The template id.</param>
/// <param name="EventKey">The event it renders.</param>
/// <param name="Channel">The channel it is written for.</param>
/// <param name="Locale">The language tag.</param>
/// <param name="Subject">The subject line.</param>
/// <param name="Body">The body with its placeholders.</param>
/// <param name="ProviderTemplateId">The DLT id, for SMS.</param>
/// <param name="Category">The preference bucket.</param>
/// <param name="IsSensitive">Whether the rendered body is kept out of the delivery log.</param>
/// <param name="IsTransactional">Whether it ignores preferences.</param>
/// <param name="IsActive">Whether it is in use.</param>
/// <param name="Version">How many times the wording has been revised.</param>
/// <param name="Placeholders">The variables the body and subject expect.</param>
/// <param name="DltViolation">What would stop this template being sent, or null.</param>
internal sealed record TemplateResponse(
    Guid Id,
    string EventKey,
    string Channel,
    string Locale,
    string Subject,
    string Body,
    string? ProviderTemplateId,
    string Category,
    bool IsSensitive,
    bool IsTransactional,
    bool IsActive,
    int Version,
    IReadOnlyList<string> Placeholders,
    string? DltViolation);

/// <summary>Lists templates.</summary>
/// <param name="Channel">Restrict to one channel, or null.</param>
/// <param name="EventKey">Restrict to one event, or null.</param>
internal sealed record ListTemplatesQuery(string? Channel, string? EventKey) : IQuery<IReadOnlyList<TemplateResponse>>;

/// <summary>Reads one template.</summary>
/// <param name="TemplateId">The template id.</param>
internal sealed record GetTemplateQuery(Guid TemplateId) : IQuery<TemplateResponse>;

/// <summary>Rewrites one template.</summary>
/// <param name="TemplateId">The template id.</param>
/// <param name="Subject">The new subject.</param>
/// <param name="Body">The new body.</param>
/// <param name="ProviderTemplateId">The registered DLT id, for SMS.</param>
/// <param name="IsActive">Whether it stays in use.</param>
internal sealed record UpdateTemplateCommand(
    Guid TemplateId,
    string Subject,
    string Body,
    string? ProviderTemplateId,
    bool IsActive) : ICommand<TemplateResponse>;

/// <summary>Rules for a template edit.</summary>
internal sealed class UpdateTemplateCommandValidator : AbstractValidator<UpdateTemplateCommand>
{
    public UpdateTemplateCommandValidator()
    {
        RuleFor(command => command.Body)
            .NotEmpty()
            .MaximumLength(20_000);

        RuleFor(command => command.Subject)
            .MaximumLength(300);

        RuleFor(command => command.ProviderTemplateId)
            .MaximumLength(64)
            .When(command => command.ProviderTemplateId is not null);
    }
}

/// <param name="context">The Notifications data context.</param>
internal sealed class ListTemplatesQueryHandler(NotificationsDbContext context)
    : IQueryHandler<ListTemplatesQuery, IReadOnlyList<TemplateResponse>>
{
    public async Task<Result<IReadOnlyList<TemplateResponse>>> HandleAsync(
        ListTemplatesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var templates = context.Templates.AsNoTracking();

        if (Enum.TryParse<NotificationChannel>(query.Channel, ignoreCase: true, out var channel))
        {
            templates = templates.Where(template => template.Channel == channel);
        }

        if (!string.IsNullOrWhiteSpace(query.EventKey))
        {
            templates = templates.Where(template => template.EventKey == query.EventKey);
        }

        var rows = await templates
            .OrderBy(template => template.EventKey)
            .ThenBy(template => template.Channel)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<TemplateResponse> response = [.. rows.Select(TemplateProjection.ToResponse)];
        return Result.Success(response);
    }
}

/// <param name="context">The Notifications data context.</param>
internal sealed class GetTemplateQueryHandler(NotificationsDbContext context)
    : IQueryHandler<GetTemplateQuery, TemplateResponse>
{
    public async Task<Result<TemplateResponse>> HandleAsync(
        GetTemplateQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var template = await context.Templates
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == query.TemplateId, cancellationToken)
            .ConfigureAwait(false);

        return template is null
            ? Result.Failure<TemplateResponse>(NotificationErrors.TemplateNotFound)
            : Result.Success(TemplateProjection.ToResponse(template));
    }
}

/// <param name="context">The Notifications data context.</param>
/// <param name="audit">Records the change; wording customers see is worth an audit entry.</param>
internal sealed class UpdateTemplateCommandHandler(NotificationsDbContext context, IAuditLogger audit)
    : ICommandHandler<UpdateTemplateCommand, TemplateResponse>
{
    /// <summary>The action recorded in the audit trail for a template edit.</summary>
    public const string AuditAction = "notifications.template.updated";

    /// <summary>The entity type recorded against that action.</summary>
    public const string AuditEntityType = "NotificationTemplate";

    public async Task<Result<TemplateResponse>> HandleAsync(
        UpdateTemplateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var template = await context.Templates
            .FirstOrDefaultAsync(candidate => candidate.Id == command.TemplateId, cancellationToken)
            .ConfigureAwait(false);

        if (template is null)
        {
            return Result.Failure<TemplateResponse>(NotificationErrors.TemplateNotFound);
        }

        // An SMS that would be dropped by the operator is refused here instead, where the reason is
        // visible to the person editing it rather than to nobody.
        if (template.Channel == NotificationChannel.Sms && command.IsActive)
        {
            var violation = DltRules.Validate(command.Body, command.ProviderTemplateId);

            if (violation != DltViolation.None)
            {
                return Result.Failure<TemplateResponse>(NotificationErrors.Dlt(violation));
            }
        }

        var before = new { template.Subject, template.Body, template.ProviderTemplateId, template.IsActive };

        template.Revise(command.Subject, command.Body, command.ProviderTemplateId, command.IsActive);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit
            .RecordAsync(
                new AuditEntry
                {
                    Action = AuditAction,
                    EntityType = AuditEntityType,
                    EntityId = template.Id.ToString(),
                    Before = before,
                    After = new { template.Subject, template.Body, template.ProviderTemplateId, template.IsActive },
                },
                cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(TemplateProjection.ToResponse(template));
    }
}

/// <summary>Maps a template onto its response.</summary>
internal static class TemplateProjection
{
    /// <summary>Builds the admin-facing response.</summary>
    /// <param name="template">The template.</param>
    public static TemplateResponse ToResponse(Domain.NotificationTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var violation = template.Channel == NotificationChannel.Sms
            ? DltRules.Validate(template.Body, template.ProviderTemplateId)
            : DltViolation.None;

        return new TemplateResponse(
            template.Id,
            template.EventKey,
            template.Channel.ToString(),
            template.Locale,
            template.Subject,
            template.Body,
            template.ProviderTemplateId,
            template.Category.ToString(),
            template.IsSensitive,
            template.IsTransactional,
            template.IsActive,
            template.Version,
            [.. TemplateRenderer.PlaceholdersIn(template.Body)
                .Concat(TemplateRenderer.PlaceholdersIn(template.Subject))
                .Distinct(StringComparer.Ordinal)],
            violation == DltViolation.None ? null : violation.ToString());
    }
}

/// <summary>The refusals this module returns.</summary>
internal static class NotificationErrors
{
    /// <summary>No template with that id.</summary>
    public static readonly Error TemplateNotFound =
        Error.NotFound("NOTIFICATION_TEMPLATE_NOT_FOUND", "No such notification template.");

    /// <summary>No message with that id.</summary>
    public static readonly Error MessageNotFound =
        Error.NotFound("NOTIFICATION_MESSAGE_NOT_FOUND", "No such notification.");

    /// <summary>The message is not in a state a retry makes sense from.</summary>
    public static readonly Error NotRetryable = Error.Validation(
        "NOTIFICATION_NOT_RETRYABLE",
        "Only a failed or suppressed message can be re-queued.");

    /// <summary>An SMS template that an Indian operator would drop.</summary>
    /// <param name="violation">Which rule it broke.</param>
    public static Error Dlt(DltViolation violation) => Error.Validation(
        "NOTIFICATION_DLT_INVALID",
        violation switch
        {
            DltViolation.MissingTemplateId =>
                "An active SMS template needs its registered DLT template id. Without one the "
                + "operator drops the message silently rather than rejecting it.",
            DltViolation.MalformedTemplateId =>
                "That does not look like a DLT template id. They are 10 to 25 digits.",
            DltViolation.BodyTooLong =>
                $"An SMS template may be at most {DltRules.MaxBodyLength} characters.",
            DltViolation.TooManyVariables =>
                $"An SMS template may carry at most {DltRules.MaxVariables} variables.",
            _ => "A DLT variable may be at most 30 characters.",
        });
}
