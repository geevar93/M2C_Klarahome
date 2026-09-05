using FluentValidation;
using KlaraHome.Contracts.Platform;

namespace KlaraHome.Modules.Platform.Application.Settings;

/// <summary>
/// The rules a settings section must satisfy before it is stored.
/// </summary>
/// <remarks>
/// <para>
/// Every rule allows an empty value. A freshly installed deployment has not been given a GSTIN yet,
/// and refusing to save the branding section because the legal section is blank would make the
/// product impossible to configure in the order an operator actually works.
/// </para>
/// <para>
/// What is rejected is a value that is <em>present and wrong</em>: a malformed GSTIN reaches an
/// invoice, and an invoice with a malformed GSTIN is a compliance failure rather than a typo.
/// </para>
/// </remarks>
internal sealed class BrandingSettingsValidator : AbstractValidator<BrandingSettings>
{
    /// <summary>A CSS hex triplet, three or six digits.</summary>
    internal const string HexColorPattern = "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$";

    public BrandingSettingsValidator()
    {
        RuleFor(branding => branding.StoreName)
            .NotEmpty().WithMessage("A store name is required; it appears in the header and in every email.")
            .MaximumLength(120);

        RuleFor(branding => branding.Tagline).MaximumLength(200);
        RuleFor(branding => branding.LogoRef).MaximumLength(500);
        RuleFor(branding => branding.LogoDarkRef).MaximumLength(500);
        RuleFor(branding => branding.FaviconRef).MaximumLength(500);

        RuleFor(branding => branding.PrimaryColor)
            .Matches(HexColorPattern).WithMessage("Primary colour must be a hex triplet, for example #1F2933.");

        RuleFor(branding => branding.AccentColor)
            .Matches(HexColorPattern).WithMessage("Accent colour must be a hex triplet, for example #C08552.");
    }
}

/// <summary>Rules for the legal identity printed on invoices.</summary>
internal sealed class LegalSettingsValidator : AbstractValidator<LegalSettings>
{
    /// <summary>Two-digit state code, a PAN, an entity number, the literal Z, and a checksum.</summary>
    internal const string GstinPattern = "^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][0-9A-Z]Z[0-9A-Z]$";

    /// <summary>Five letters, four digits, one letter.</summary>
    internal const string PanPattern = "^[A-Z]{5}[0-9]{4}[A-Z]$";

    /// <summary>Listed/unlisted marker, industry code, state, year, ownership and registration number.</summary>
    internal const string CinPattern = "^[LU][0-9]{5}[A-Z]{2}[0-9]{4}[A-Z]{3}[0-9]{6}$";

    public LegalSettingsValidator()
    {
        RuleFor(legal => legal.LegalEntityName).MaximumLength(200);

        RuleFor(legal => legal.Gstin)
            .Matches(GstinPattern)
            .When(legal => !string.IsNullOrWhiteSpace(legal.Gstin))
            .WithMessage("GSTIN must be 15 characters, for example 27AAAAA0000A1Z5.");

        RuleFor(legal => legal.Pan)
            .Matches(PanPattern)
            .When(legal => !string.IsNullOrWhiteSpace(legal.Pan))
            .WithMessage("PAN must be 10 characters, for example AAAAA0000A.");

        RuleFor(legal => legal.Cin)
            .Matches(CinPattern)
            .When(legal => !string.IsNullOrWhiteSpace(legal.Cin))
            .WithMessage("CIN must be 21 characters, for example U74999MH2020PTC123456.");

        RuleFor(legal => legal.RegisteredAddress).SetValidator(new PostalAddressValidator());
        RuleFor(legal => legal.OperatingAddress).SetValidator(new PostalAddressValidator());
    }
}

/// <summary>Rules for the published support and grievance contacts.</summary>
internal sealed class SupportSettingsValidator : AbstractValidator<SupportSettings>
{
    /// <summary>E.164: a leading plus, then up to fifteen digits.</summary>
    internal const string E164Pattern = "^\\+[1-9][0-9]{7,14}$";

    public SupportSettingsValidator()
    {
        RuleFor(support => support.Email)
            .EmailAddress()
            .When(support => !string.IsNullOrWhiteSpace(support.Email));

        RuleFor(support => support.Phone)
            .Matches(E164Pattern)
            .When(support => !string.IsNullOrWhiteSpace(support.Phone))
            .WithMessage("Phone must be in E.164 form, for example +919876543210.");

        RuleFor(support => support.WhatsApp)
            .Matches(E164Pattern)
            .When(support => !string.IsNullOrWhiteSpace(support.WhatsApp))
            .WithMessage("WhatsApp number must be in E.164 form, for example +919876543210.");

        RuleFor(support => support.Hours).MaximumLength(120);
        RuleFor(support => support.GrievanceOfficerName).MaximumLength(120);

        RuleFor(support => support.GrievanceOfficerEmail)
            .EmailAddress()
            .When(support => !string.IsNullOrWhiteSpace(support.GrievanceOfficerEmail));

        RuleFor(support => support.GrievanceOfficerPhone)
            .Matches(E164Pattern)
            .When(support => !string.IsNullOrWhiteSpace(support.GrievanceOfficerPhone))
            .WithMessage("Grievance officer phone must be in E.164 form.");

        // The Consumer Protection (E-Commerce) Rules 2020 cap these, so the setting may tighten
        // them but never loosen them past what is published to customers.
        RuleFor(support => support.AcknowledgementHours)
            .InclusiveBetween(1, 48)
            .WithMessage("A complaint must be acknowledged within 48 hours.");

        RuleFor(support => support.ResolutionDays)
            .InclusiveBetween(1, 30)
            .WithMessage("A complaint must be resolved within one month.");
    }
}

/// <summary>Rules for locale, currency and timezone.</summary>
internal sealed class LocalizationSettingsValidator : AbstractValidator<LocalizationSettings>
{
    public LocalizationSettingsValidator()
    {
        RuleFor(localization => localization.Locale)
            .Matches("^[a-z]{2}(-[A-Z]{2})?$")
            .WithMessage("Locale must be a BCP-47 tag, for example en-IN.");

        RuleFor(localization => localization.CurrencyCode)
            .Matches("^[A-Z]{3}$")
            .WithMessage("Currency must be an ISO 4217 alphabetic code, for example INR.");

        RuleFor(localization => localization.CountryCode)
            .Matches("^[A-Z]{2}$")
            .WithMessage("Country must be an ISO 3166-1 alpha-2 code, for example IN.");

        RuleFor(localization => localization.TimeZone)
            .Matches(IanaTimeZonePattern)
            .WithMessage("Time zone must be an IANA identifier, for example Asia/Kolkata.");
    }

    /// <summary>
    /// An IANA zone name: <c>Area/Location</c>, optionally with a further sub-location.
    /// </summary>
    /// <remarks>
    /// The shape is checked rather than the value being looked up in
    /// <see cref="TimeZoneInfo"/>. The product builds with <c>InvariantGlobalization</c>, so a
    /// Windows development machine has no IANA database to look the name up in while the Linux
    /// container it deploys to does — and a validation rule whose answer depends on the developer's
    /// operating system is worse than no rule. A name that is well formed but unknown fails where
    /// it is used, loudly, with the name in the message.
    /// </remarks>
    internal const string IanaTimeZonePattern = "^(UTC|[A-Za-z][A-Za-z0-9+_-]*(?:/[A-Za-z0-9+._-]+){1,2})$";
}

/// <summary>Rules for the customer-facing commerce policies.</summary>
internal sealed class CommerceSettingsValidator : AbstractValidator<CommerceSettings>
{
    public CommerceSettingsValidator()
    {
        RuleFor(commerce => commerce.ReturnWindowDays).InclusiveBetween(0, 365);
        RuleFor(commerce => commerce.CancellationWindowHours).InclusiveBetween(0, 720);
        RuleFor(commerce => commerce.CodOrderValueLimit).InclusiveBetween(0m, 1_000_000m);
        RuleFor(commerce => commerce.FreeShippingThreshold).InclusiveBetween(0m, 1_000_000m);
        RuleFor(commerce => commerce.MaxQuantityPerLine).InclusiveBetween(1, 999);

        RuleFor(commerce => commerce.CodOrderValueLimit)
            .GreaterThan(0m)
            .When(commerce => commerce.CodEnabled)
            .WithMessage("A cash-on-delivery limit of zero disables COD; turn COD off instead of setting it to zero.");
    }
}

/// <summary>Rules shared by every address a settings section carries.</summary>
internal sealed class PostalAddressValidator : AbstractValidator<PostalAddress>
{
    public PostalAddressValidator()
    {
        RuleFor(address => address.Line1).MaximumLength(200);
        RuleFor(address => address.Line2).MaximumLength(200);
        RuleFor(address => address.City).MaximumLength(120);
        RuleFor(address => address.State).MaximumLength(100);

        RuleFor(address => address.StateCode)
            .Matches("^[0-9]{2}$")
            .When(address => !string.IsNullOrWhiteSpace(address.StateCode))
            .WithMessage("State code must be the two-digit GST code, for example 27 for Maharashtra.");

        RuleFor(address => address.Pincode)
            .Matches("^[1-9][0-9]{5}$")
            .When(address => !string.IsNullOrWhiteSpace(address.Pincode))
            .WithMessage("PIN code must be six digits and must not start with zero.");

        RuleFor(address => address.CountryCode)
            .Matches("^[A-Z]{2}$")
            .WithMessage("Country must be an ISO 3166-1 alpha-2 code, for example IN.");
    }
}
