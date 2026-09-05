namespace KlaraHome.Contracts.Platform;

/// <summary>
/// A block of store configuration stored under one key in <c>platform.store_settings</c>, read as
/// a typed object, and edited as a whole in the admin UI.
/// </summary>
/// <remarks>
/// <para>
/// The section carries its own key and its own visibility as static members, so there is exactly
/// one place that answers "what is this section called, and may the storefront see it". A parallel
/// registry would be a second place, and the two would drift.
/// </para>
/// <para>
/// Every section must be usable with <c>new()</c> and must carry a complete set of defaults: a
/// deployment nobody has configured yet still has to render a storefront.
/// </para>
/// </remarks>
/// <typeparam name="TSelf">The implementing section type.</typeparam>
public interface ISettingsSection<out TSelf>
    where TSelf : ISettingsSection<TSelf>, new()
{
    /// <summary>Stable key the value is stored under. Never rename one; add a new section.</summary>
    static abstract string SectionKey { get; }

    /// <summary>
    /// Whether the storefront may read this section anonymously. Legal and support details are
    /// public because the law requires them to be published, and commerce rules are public because
    /// the storefront has to apply them.
    /// </summary>
    static abstract bool IsPublic { get; }
}

/// <summary>
/// Visual identity. Nothing here is compiled in: a second business is onboarded by changing these
/// values, not by rebuilding the product (docs/01-architecture.md §8).
/// </summary>
public sealed record BrandingSettings : ISettingsSection<BrandingSettings>
{
    /// <inheritdoc cref="ISettingsSection{TSelf}.SectionKey" />
    public static string SectionKey => "branding";

    /// <inheritdoc cref="ISettingsSection{TSelf}.IsPublic" />
    public static bool IsPublic => true;

    /// <summary>Trading name shown in the header, in page titles and in transactional messages.</summary>
    public string StoreName { get; init; } = "Klara Home";

    /// <summary>One-line positioning statement, shown under the logo and in meta descriptions.</summary>
    public string Tagline { get; init; } = "Everything for a home you love.";

    /// <summary>Media reference for the primary logo. Resolved by the Media module from Step 8.</summary>
    public string LogoRef { get; init; } = string.Empty;

    /// <summary>Media reference for the logo used on dark backgrounds.</summary>
    public string LogoDarkRef { get; init; } = string.Empty;

    /// <summary>Media reference for the favicon.</summary>
    public string FaviconRef { get; init; } = string.Empty;

    /// <summary>Brand primary colour as a CSS hex triplet. The design system consumes it at Step 30.</summary>
    public string PrimaryColor { get; init; } = "#1F2933";

    /// <summary>Brand accent colour as a CSS hex triplet.</summary>
    public string AccentColor { get; init; } = "#C08552";
}

/// <summary>
/// The legal identity that must appear on invoices, in the footer and on every product page under
/// the Consumer Protection (E-Commerce) Rules 2020 (docs/07-security-compliance.md §6).
/// </summary>
public sealed record LegalSettings : ISettingsSection<LegalSettings>
{
    /// <inheritdoc cref="ISettingsSection{TSelf}.SectionKey" />
    public static string SectionKey => "legal";

    /// <inheritdoc cref="ISettingsSection{TSelf}.IsPublic" />
    public static bool IsPublic => true;

    /// <summary>Registered name of the legal entity operating the marketplace.</summary>
    public string LegalEntityName { get; init; } = string.Empty;

    /// <summary>GSTIN of the operating entity. Fifteen characters; validated on save.</summary>
    public string Gstin { get; init; } = string.Empty;

    /// <summary>Permanent Account Number of the operating entity.</summary>
    public string Pan { get; init; } = string.Empty;

    /// <summary>Corporate Identity Number, for a company rather than a firm.</summary>
    public string Cin { get; init; } = string.Empty;

    /// <summary>Registered office address, printed on invoices.</summary>
    public PostalAddress RegisteredAddress { get; init; } = new();

    /// <summary>Address goods are dispatched from, when it differs from the registered office.</summary>
    public PostalAddress OperatingAddress { get; init; } = new();
}

/// <summary>
/// Where a customer reaches a human. The grievance officer's details are a statutory publication
/// requirement, and the acknowledgement and resolution SLAs are quoted back to customers.
/// </summary>
public sealed record SupportSettings : ISettingsSection<SupportSettings>
{
    /// <inheritdoc cref="ISettingsSection{TSelf}.SectionKey" />
    public static string SectionKey => "support";

    /// <inheritdoc cref="ISettingsSection{TSelf}.IsPublic" />
    public static bool IsPublic => true;

    /// <summary>Customer support mailbox.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Customer support telephone number in E.164 form.</summary>
    public string Phone { get; init; } = string.Empty;

    /// <summary>WhatsApp business number in E.164 form, when one is offered.</summary>
    public string WhatsApp { get; init; } = string.Empty;

    /// <summary>Human-readable opening hours, for example "Mon-Sat, 9am-7pm IST".</summary>
    public string Hours { get; init; } = "Mon-Sat, 9am-7pm IST";

    /// <summary>Name of the published grievance officer.</summary>
    public string GrievanceOfficerName { get; init; } = string.Empty;

    /// <summary>Grievance officer's mailbox.</summary>
    public string GrievanceOfficerEmail { get; init; } = string.Empty;

    /// <summary>Grievance officer's telephone number in E.164 form.</summary>
    public string GrievanceOfficerPhone { get; init; } = string.Empty;

    /// <summary>Hours within which a complaint is acknowledged. The statutory maximum is 48.</summary>
    public int AcknowledgementHours { get; init; } = 48;

    /// <summary>Days within which a complaint is resolved. The statutory maximum is one month.</summary>
    public int ResolutionDays { get; init; } = 30;
}

/// <summary>
/// Locale, currency and timezone. Read by every formatter in the system; nothing assumes India,
/// it reads this.
/// </summary>
public sealed record LocalizationSettings : ISettingsSection<LocalizationSettings>
{
    /// <inheritdoc cref="ISettingsSection{TSelf}.SectionKey" />
    public static string SectionKey => "localization";

    /// <inheritdoc cref="ISettingsSection{TSelf}.IsPublic" />
    public static bool IsPublic => true;

    /// <summary>BCP-47 locale used when the caller expresses no preference.</summary>
    public string Locale { get; init; } = "en-IN";

    /// <summary>ISO 4217 currency the catalogue is priced in.</summary>
    public string CurrencyCode { get; init; } = "INR";

    /// <summary>IANA timezone used to render a stored UTC instant for a human.</summary>
    public string TimeZone { get; init; } = "Asia/Kolkata";

    /// <summary>Country the store operates in, ISO 3166-1 alpha-2.</summary>
    public string CountryCode { get; init; } = "IN";
}

/// <summary>
/// The business rules a customer feels: how long they have to return something, whether cash on
/// delivery is offered and up to what value, when shipping stops being charged.
/// </summary>
public sealed record CommerceSettings : ISettingsSection<CommerceSettings>
{
    /// <inheritdoc cref="ISettingsSection{TSelf}.SectionKey" />
    public static string SectionKey => "commerce";

    /// <inheritdoc cref="ISettingsSection{TSelf}.IsPublic" />
    public static bool IsPublic => true;

    /// <summary>Days after delivery within which a return may be raised.</summary>
    public int ReturnWindowDays { get; init; } = 7;

    /// <summary>Hours after placement within which a customer may cancel without asking.</summary>
    public int CancellationWindowHours { get; init; } = 24;

    /// <summary>Whether cash on delivery is offered at all.</summary>
    public bool CodEnabled { get; init; } = true;

    /// <summary>Maximum order value payable by cash on delivery, in the store currency.</summary>
    public decimal CodOrderValueLimit { get; init; } = 5000m;

    /// <summary>Order value at or above which shipping is free, in the store currency.</summary>
    public decimal FreeShippingThreshold { get; init; } = 999m;

    /// <summary>Maximum units of one listing a single cart line may hold.</summary>
    public int MaxQuantityPerLine { get; init; } = 10;
}

/// <summary>An Indian postal address, in the shape invoices and shipping labels need.</summary>
public sealed record PostalAddress
{
    /// <summary>First address line: building, flat, street.</summary>
    public string Line1 { get; init; } = string.Empty;

    /// <summary>Second address line: area, landmark.</summary>
    public string Line2 { get; init; } = string.Empty;

    /// <summary>City or town.</summary>
    public string City { get; init; } = string.Empty;

    /// <summary>State or union territory name, as published in <c>platform.states</c>.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>GST state code, for example <c>27</c> for Maharashtra. Drives place of supply.</summary>
    public string StateCode { get; init; } = string.Empty;

    /// <summary>Six-digit PIN code.</summary>
    public string Pincode { get; init; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2 country code.</summary>
    public string CountryCode { get; init; } = "IN";
}
