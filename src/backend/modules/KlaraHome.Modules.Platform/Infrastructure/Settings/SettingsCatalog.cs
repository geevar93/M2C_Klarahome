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

        // Added by Step 10: the buy-box rule is a commercial decision the operator makes, and
        // docs/03-database-design.md §4.4 puts it here rather than in the Catalog module's own
        // configuration. Not public - a shopper is shown the winning offer, never the reason.
        new SettingsSectionDescriptor<BuyBoxSettings>(),

        // Added by Step 12: the COD fee, the tax on delivery, the loyalty rate and the store-credit
        // ceiling are all commercial choices, and docs/03-database-design.md §4.6 puts them here
        // rather than in the Pricing module's configuration. Not public - a shopper sees the fee on
        // their quote, never the rule behind it.
        new SettingsSectionDescriptor<PricingSettings>(),

        // Added by Step 15: when a refund needs a second signature, and whether a cancelled order
        // is refunded without anybody asking, are governance decisions the business revisits -
        // usually after an incident - and they must not need a deployment
        // (docs/07-security-compliance.md 4). Not public: the approval threshold is the shape of an
        // abuse.
        new SettingsSectionDescriptor<PaymentSettings>(),

        // Added by Step 16A: where the store is willing to deliver (ADR-018). Public, because the
        // storefront has to be able to say so on a product page and because the refusal message is
        // the operator's own words. It is deliberately not a shipping zone - a zone prices a parcel,
        // and an unpriced destination and an undecided one must not look the same.
        new SettingsSectionDescriptor<DeliveryCoverageSettings>(),

        // Added by Step 17: who pays to send goods back, what is approved without a human, where the
        // money goes and what becomes of the goods. Every one of them is a policy a business
        // revisits after a month of returns costs more than it budgeted for, so none is
        // configuration. Not public - how long a shopper has is on `commerce` and is public; the
        // rules behind an approval are the operator's.
        new SettingsSectionDescriptor<ReturnsSettings>(),

        // Added by Step 18: how often a seller is settled, what the platform keeps, and at what
        // rates tax is collected and deducted at source. The statutory rates are here rather than in
        // configuration for the reason the rest of this list is — a rate changes by a notification in
        // the Gazette, and a deployment that needed a rebuild to follow one would file a wrong
        // return. Not public: what the platform charges a seller is between the platform and that
        // seller.
        new SettingsSectionDescriptor<SettlementSettings>(),

        // Added by Step 19: how the storefront finds things — the ranking weights, the price bands,
        // the fuzzy threshold and whether queries are logged. Public, because the storefront applies
        // several of them itself before the first response arrives, and settings rather than
        // configuration because "our results feel stale" is a complaint a merchandiser answers on a
        // Tuesday afternoon rather than in a release.
        new SettingsSectionDescriptor<SearchSettings>(),

        // Added by Step 20: the canonical host, whether this deployment wants to be indexed at all,
        // the robots directives and the organisation identity a structured-data graph names. Public,
        // because the storefront renders the canonical tag, the robots meta and the JSON-LD itself
        // during server-side rendering. Settings rather than configuration because a staging site
        // that has leaked into an index is a problem measured in minutes, not in releases.
        new SettingsSectionDescriptor<SeoSettings>(),
    ];

    private static readonly Dictionary<string, ISettingsSectionDescriptor> ByKey =
        Sections.ToDictionary(section => section.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>The descriptor for a key, or null when no section claims it.</summary>
    /// <param name="key">The section key.</param>
    public static ISettingsSectionDescriptor? Find(string key)
        => ByKey.GetValueOrDefault(key ?? string.Empty);
}
