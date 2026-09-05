using System.Text.Json;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Platform.Infrastructure.Settings;
using KlaraHome.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Platform.Application.Settings;

/// <summary>One settings section as the admin surface sees it.</summary>
/// <param name="Key">The section key.</param>
/// <param name="IsPublic">Whether the storefront may read it anonymously.</param>
/// <param name="Value">The section's current value.</param>
internal sealed record SettingsSectionResponse(string Key, bool IsPublic, JsonElement Value);

/// <summary>Every settings section, in the order the admin UI shows them.</summary>
/// <param name="Sections">The sections.</param>
internal sealed record StoreSettingsResponse(IReadOnlyList<SettingsSectionResponse> Sections);

/// <summary>Reads every settings section, stored value or defaults.</summary>
internal sealed record GetStoreSettingsQuery : IQuery<StoreSettingsResponse>;

/// <summary>Replaces one settings section, as a whole.</summary>
/// <param name="Key">The section key.</param>
/// <param name="Value">The new value. Must be a JSON object matching the section's shape.</param>
internal sealed record UpdateStoreSettingCommand(string Key, JsonElement Value)
    : ICommand<SettingsSectionResponse>;

/// <summary>Reads the sections through the settings service, so the cache serves both paths.</summary>
/// <param name="settings">The settings service.</param>
internal sealed class GetStoreSettingsQueryHandler(StoreSettingsService settings)
    : IQueryHandler<GetStoreSettingsQuery, StoreSettingsResponse>
{
    public async Task<Result<StoreSettingsResponse>> HandleAsync(
        GetStoreSettingsQuery query,
        CancellationToken cancellationToken)
    {
        var sections = new List<SettingsSectionResponse>(SettingsCatalog.Sections.Count);

        foreach (var descriptor in SettingsCatalog.Sections)
        {
            var document = await settings.GetDocumentAsync(descriptor, cancellationToken).ConfigureAwait(false);
            sections.Add(new SettingsSectionResponse(
                descriptor.Key,
                descriptor.IsPublic,
                JsonElements.Parse(document)));
        }

        return new StoreSettingsResponse(sections);
    }
}

/// <summary>
/// Validates and stores one section, then records the change in the audit trail.
/// </summary>
/// <remarks>
/// The audit entry is written after the save, with the document the section actually held before
/// the call rather than a value reconstructed from the request. That is the difference between an
/// audit trail and a log of what somebody intended.
/// </remarks>
/// <param name="settings">The settings service.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="services">Resolves the section's validator.</param>
internal sealed class UpdateStoreSettingCommandHandler(
    StoreSettingsService settings,
    IAuditLogger audit,
    IServiceProvider services) : ICommandHandler<UpdateStoreSettingCommand, SettingsSectionResponse>
{
    /// <summary>The action recorded in the audit trail for a settings change.</summary>
    public const string AuditAction = "platform.settings.updated";

    /// <summary>The entity type recorded against that action.</summary>
    public const string AuditEntityType = "StoreSetting";

    public async Task<Result<SettingsSectionResponse>> HandleAsync(
        UpdateStoreSettingCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var descriptor = SettingsCatalog.Find(command.Key);
        if (descriptor is null)
        {
            return Error.NotFound(
                "SETTINGS_SECTION_UNKNOWN",
                $"There is no settings section named '{command.Key}'.");
        }

        if (!descriptor.TryParse(command.Value.GetRawText(), out var parsed, out var parseError))
        {
            return Error.Validation(
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    [descriptor.Key] = [parseError ?? "The document could not be read."],
                },
                "SETTINGS_DOCUMENT_INVALID",
                $"The '{descriptor.Key}' document does not match the shape of that section.");
        }

        var fieldErrors = descriptor.Validate(services, parsed!);
        if (fieldErrors.Count > 0)
        {
            return Error.Validation(fieldErrors);
        }

        var document = descriptor.Serialize(parsed!);
        var before = await settings.ReplaceAsync(descriptor, document, cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AuditEntityType,
                EntityId = descriptor.Key,
                Before = JsonElements.Parse(before),
                After = JsonElements.Parse(document),
                ActorType = AuditActorType.StaffUser,
            },
            cancellationToken).ConfigureAwait(false);

        return new SettingsSectionResponse(descriptor.Key, descriptor.IsPublic, JsonElements.Parse(document));
    }
}

/// <summary>Turns a stored document into a detached <see cref="JsonElement"/>.</summary>
/// <remarks>
/// A <see cref="JsonDocument"/> holds a pooled buffer and has to be disposed; the element it hands
/// out is only valid while it lives. Cloning inside a <c>using</c> is the one correct combination,
/// and it is easy enough to get wrong that it is written once here.
/// </remarks>
internal static class JsonElements
{
    /// <summary>Parses JSON into an element that outlives the document it came from.</summary>
    /// <param name="json">The document text.</param>
    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
