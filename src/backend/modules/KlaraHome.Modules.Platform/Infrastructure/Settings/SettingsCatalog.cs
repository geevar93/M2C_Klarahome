using System.Text.Json;
using FluentValidation;
using KlaraHome.Contracts.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Platform.Infrastructure.Settings;

/// <summary>
/// What the module knows about one settings section without knowing its type at the call site.
/// </summary>
/// <remarks>
/// The admin surface edits sections by key: it receives a key and a JSON document and has to
/// resolve, validate and store them. The typed reader path resolves the same section by CLR type.
/// A descriptor is the one object both paths go through, so a section cannot be readable one way
/// and unknown the other.
/// </remarks>
internal interface ISettingsSectionDescriptor
{
    /// <summary>The key the section is stored under.</summary>
    string Key { get; }

    /// <summary>Whether the storefront may read it anonymously.</summary>
    bool IsPublic { get; }

    /// <summary>The section's CLR type.</summary>
    Type SectionType { get; }

    /// <summary>The section's defaults, serialised.</summary>
    string SerializeDefaults();

    /// <summary>
    /// Parses an inbound admin document strictly, rejecting properties the section does not
    /// declare. A misspelled field in a settings PUT would otherwise be accepted and silently
    /// dropped, and the operator would believe they had changed something.
    /// </summary>
    /// <param name="json">The document to parse.</param>
    /// <param name="value">The parsed section.</param>
    /// <param name="error">Why it could not be parsed.</param>
    bool TryParse(string json, out object? value, out string? error);

    /// <summary>Runs the section's own validation rules.</summary>
    /// <param name="services">Resolves the section's validator, if it has one.</param>
    /// <param name="value">The parsed section.</param>
    IReadOnlyDictionary<string, IReadOnlyList<string>> Validate(IServiceProvider services, object value);

    /// <summary>Serialises a parsed section for storage.</summary>
    /// <param name="value">The parsed section.</param>
    string Serialize(object value);
}

/// <inheritdoc />
/// <typeparam name="TSection">The section this descriptor describes.</typeparam>
internal sealed class SettingsSectionDescriptor<TSection> : ISettingsSectionDescriptor
    where TSection : class, ISettingsSection<TSection>, new()
{
    /// <inheritdoc />
    public string Key => TSection.SectionKey;

    /// <inheritdoc />
    public bool IsPublic => TSection.IsPublic;

    /// <inheritdoc />
    public Type SectionType => typeof(TSection);

    /// <inheritdoc />
    public string SerializeDefaults() => JsonSerializer.Serialize(new TSection(), SettingsJson.Storage);

    /// <inheritdoc />
    public bool TryParse(string json, out object? value, out string? error)
    {
        try
        {
            value = JsonSerializer.Deserialize<TSection>(json, SettingsJson.Inbound);
            error = value is null ? "The document must be a JSON object." : null;
            return value is not null;
        }
        catch (JsonException exception)
        {
            value = null;
            error = exception.Message;
            return false;
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Validate(IServiceProvider services, object value)
    {
        ArgumentNullException.ThrowIfNull(services);

        var validator = services.GetService<IValidator<TSection>>();
        if (validator is null)
        {
            return SettingsJson.NoErrors;
        }

        var result = validator.Validate((TSection)value);

        return result.IsValid
            ? SettingsJson.NoErrors
            : result.Errors
                .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<string>)[.. group.Select(failure => failure.ErrorMessage)],
                    StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public string Serialize(object value) => JsonSerializer.Serialize((TSection)value, SettingsJson.Storage);
}

/// <summary>Serialisation settings for the two directions a section travels.</summary>
internal static class SettingsJson
{
    /// <summary>
    /// How a section is stored and read back. camelCase, like every API payload, so the document in
    /// the column is the document the admin UI sends and receives and an operator reading it in
    /// <c>psql</c> sees the same field names as the developer reading the network tab. Unknown
    /// properties are ignored on this path, so a value written by a newer build can still be read
    /// by an older one during a rollback.
    /// </summary>
    public static readonly JsonSerializerOptions Storage = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    /// <summary>
    /// How an inbound admin document is parsed: as above, except that anything the section does not
    /// declare is an error rather than a silent no-op.
    /// </summary>
    public static readonly JsonSerializerOptions Inbound = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>The empty validation result, allocated once.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoErrors =
        System.Collections.ObjectModel.ReadOnlyDictionary<string, IReadOnlyList<string>>.Empty;
}

/// <summary>
/// Every settings section the platform has. Adding a section means adding its record to
/// <c>KlaraHome.Contracts</c> and one line here — the seeder, the admin surface, the public store
/// config and the typed reader all follow from this list.
/// </summary>
internal static class SettingsCatalog
{
    /// <summary>The sections, in the order the admin UI shows them.</summary>
    public static IReadOnlyList<ISettingsSectionDescriptor> Sections { get; } =
    [
        new SettingsSectionDescriptor<BrandingSettings>(),
        new SettingsSectionDescriptor<LegalSettings>(),
        new SettingsSectionDescriptor<SupportSettings>(),
        new SettingsSectionDescriptor<LocalizationSettings>(),
        new SettingsSectionDescriptor<CommerceSettings>(),
    ];

    private static readonly Dictionary<string, ISettingsSectionDescriptor> ByKey =
        Sections.ToDictionary(section => section.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>The descriptor for a key, or null when no section claims it.</summary>
    /// <param name="key">The section key.</param>
    public static ISettingsSectionDescriptor? Find(string key)
        => ByKey.GetValueOrDefault(key ?? string.Empty);
}
