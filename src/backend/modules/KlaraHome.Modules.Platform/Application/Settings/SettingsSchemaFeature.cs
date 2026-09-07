using FluentValidation;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Platform.Infrastructure.Settings;
using KlaraHome.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Platform.Application.Settings;

/// <summary>One field of a settings section, as the store-settings form reads it.</summary>
/// <param name="Name">
/// The JSON path from the section's root, dotted for a nested object — <c>organisation.name</c>.
/// </param>
/// <param name="Kind">What kind of control to draw: text, integer, number, boolean, choice, and so on.</param>
/// <param name="IsRequired">Whether an empty value is refused.</param>
/// <param name="IsList">Whether the value is an array of <see cref="Kind"/>.</param>
/// <param name="Minimum">The smallest number accepted, when there is a bound.</param>
/// <param name="Maximum">The largest number accepted, when there is a bound.</param>
/// <param name="MaxLength">The longest text accepted, when there is a limit.</param>
/// <param name="Pattern">The regular expression a text value must match, when there is one.</param>
/// <param name="Choices">The words accepted, for a choice field.</param>
internal sealed record SettingsFieldSchema(
    string Name,
    string Kind,
    bool IsRequired,
    bool IsList,
    decimal? Minimum,
    decimal? Maximum,
    int? MaxLength,
    string? Pattern,
    IReadOnlyList<string>? Choices);

/// <summary>One settings section's shape.</summary>
/// <param name="Key">The section key, matching the value document's.</param>
/// <param name="IsPublic">Whether the storefront may read it anonymously.</param>
/// <param name="Fields">Its fields, in declaration order.</param>
internal sealed record SettingsSectionSchema(
    string Key,
    bool IsPublic,
    IReadOnlyList<SettingsFieldSchema> Fields);

/// <summary>The shape of every settings section.</summary>
/// <param name="Sections">The sections, in the order the admin UI shows them.</param>
internal sealed record SettingsSchemaResponse(IReadOnlyList<SettingsSectionSchema> Sections);

/// <summary>Reads the shape and the rules of every settings section.</summary>
internal sealed record GetSettingsSchemaQuery : IQuery<SettingsSchemaResponse>;

/// <summary>
/// Serves the schema the settings records and their validators already encode.
/// </summary>
/// <remarks>
/// The same arrangement as the CMS block types, and for the same reason: the store-settings screen
/// renders a control per leaf of the value document, so a schema compiled into the admin app would
/// drift from the validator that judges what the form produces — and the operator would meet the
/// difference as a refusal with nothing on screen explaining it (Step 28B, deliverable 10).
/// </remarks>
/// <param name="services">Resolves each section's validator, where it has one.</param>
internal sealed class GetSettingsSchemaQueryHandler(IServiceProvider services)
    : IQueryHandler<GetSettingsSchemaQuery, SettingsSchemaResponse>
{
    public Task<Result<SettingsSchemaResponse>> HandleAsync(
        GetSettingsSchemaQuery query,
        CancellationToken cancellationToken)
    {
        var sections = new List<SettingsSectionSchema>(SettingsCatalog.Sections.Count);

        foreach (var descriptor in SettingsCatalog.Sections)
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(descriptor.SectionType);
            var validator = services.GetService(validatorType) as IValidator;

            sections.Add(new SettingsSectionSchema(
                descriptor.Key,
                descriptor.IsPublic,
                SettingsSchemaReader.Describe(descriptor.SectionType, validator)));
        }

        return Task.FromResult(Result.Success(new SettingsSchemaResponse(sections)));
    }
}
