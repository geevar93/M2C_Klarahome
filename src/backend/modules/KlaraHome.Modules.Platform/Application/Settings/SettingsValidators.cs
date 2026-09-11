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

        // A key that is not a real CSS custom property name would be silently ignored by
        // `style.setProperty`, which is worse than refusing the save — the operator would believe a
        // re-theme had happened and it had not. Bounded to keep the document small: the token sheet
        // in docs/10-design-system.md names on the order of sixty custom properties in total.
        RuleForEach(branding => branding.ThemeTokens.Keys)
            .Matches(ThemeTokenKeyPattern)
            .WithMessage("A theme token name must be a CSS custom property, for example --color-primary.");

        RuleForEach(branding => branding.ThemeTokens.Values).MaximumLength(200);

        RuleFor(branding => branding.ThemeTokens)
            .Must(tokens => tokens.Count <= 100)
            .WithMessage("At most one hundred theme token overrides.");
    }

    /// <summary>A CSS custom property name: two leading hyphens, then lower-kebab-case.</summary>
    internal const string ThemeTokenKeyPattern = "^--[a-z][a-z0-9-]*$";
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

/// <summary>
/// Rules for the delivery area (ADR-018).
/// </summary>
/// <remarks>
/// The rule worth having is the last one: an enabled policy with no allow rule of any kind matches
/// nothing, which would stop the store selling to anybody — silently, and looking exactly like a
/// courier outage. It is refused here with a message that says what would happen, because the screen
/// this is edited in is the only place anybody would find out.
/// </remarks>
internal sealed class DeliveryCoverageSettingsValidator : AbstractValidator<DeliveryCoverageSettings>
{
    public DeliveryCoverageSettingsValidator()
    {
        RuleFor(coverage => coverage.Message).MaximumLength(300);

        RuleForEach(coverage => coverage.AllowedCities)
            .NotEmpty()
            .MaximumLength(120)
            .WithMessage("A city name must not be blank.");

        RuleForEach(coverage => coverage.AllowedPincodePrefixes)
            .Matches("^[1-9][0-9]{0,5}$")
            .WithMessage("A PIN-code prefix is one to six digits and must not start with zero — for example 500.");

        RuleForEach(coverage => coverage.AllowedPincodes)
            .Matches("^[1-9][0-9]{5}$")
            .WithMessage("A PIN code must be six digits and must not start with zero.");

        RuleForEach(coverage => coverage.BlockedPincodes)
            .Matches("^[1-9][0-9]{5}$")
            .WithMessage("A PIN code must be six digits and must not start with zero.");

        RuleFor(coverage => coverage)
            .Must(coverage => coverage.AllowedCities.Count > 0
                              || coverage.AllowedPincodePrefixes.Count > 0
                              || coverage.AllowedPincodes.Count > 0)
            .When(coverage => coverage.Enabled)
            .WithName(nameof(DeliveryCoverageSettings.AllowedPincodePrefixes))
            .WithMessage(
                "Delivery coverage is on with no city, prefix or PIN code allowed, which would refuse "
                + "every order. Add an area, or turn coverage off to deliver nationally.");
    }
}

/// <summary>
/// Rules for the returns policy (Step 17).
/// </summary>
/// <remarks>
/// Four of these fields are words from a closed list, and a typo in any of them would be discovered
/// only when a return reached the step that reads it — a QC disposition that matches nothing would
/// leave goods in a state the inventory seam refuses. They are checked here, at the one screen where
/// somebody can still fix them.
/// </remarks>
internal sealed class ReturnsSettingsValidator : AbstractValidator<ReturnsSettings>
{
    public ReturnsSettingsValidator()
    {
        RuleFor(returns => returns.AutoApproveBelow).InclusiveBetween(0m, 1_000_000m);
        RuleFor(returns => returns.ReturnShippingFee).InclusiveBetween(0m, 100_000m);
        RuleFor(returns => returns.MaxEvidenceFiles).InclusiveBetween(0, 20);
        RuleFor(returns => returns.PickupSlaDays).InclusiveBetween(1, 90);

        RuleFor(returns => returns.DefaultShippingPayer)
            .Must(payer => ReturnShippingPayers.All.Contains(payer, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Who pays for a return must be one of: {string.Join(", ", ReturnShippingPayers.All)}.");

        RuleFor(returns => returns.DefaultRefundMode)
            .Must(mode => RefundModes.All.Contains(mode, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"A refund mode must be one of: {string.Join(", ", RefundModes.All)}.");

        RuleFor(returns => returns.DefaultPassedDisposition)
            .Must(disposition => ReturnDispositions.All.Contains(disposition, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"A disposition must be one of: {string.Join(", ", ReturnDispositions.All)}.");

        RuleFor(returns => returns.DefaultFailedDisposition)
            .Must(disposition => ReturnDispositions.All.Contains(disposition, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"A disposition must be one of: {string.Join(", ", ReturnDispositions.All)}.");

        // Store credit is the only refund a cash-on-delivery order can have without a bank transfer,
        // so defaulting to a wallet that is switched off would leave those returns with nowhere for
        // the money to go.
        RuleFor(returns => returns.AllowWalletRefunds)
            .Equal(true)
            .When(returns => string.Equals(returns.DefaultRefundMode, RefundModes.Wallet, StringComparison.OrdinalIgnoreCase))
            .WithMessage("Store credit is the default refund mode, so it cannot also be turned off.");

        // A fee nobody is charged is a fee that will surprise somebody later.
        RuleFor(returns => returns.ReturnShippingFee)
            .Equal(0m)
            .When(returns => !string.Equals(
                returns.DefaultShippingPayer,
                ReturnShippingPayers.Customer,
                StringComparison.OrdinalIgnoreCase))
            .WithMessage(
                "A return shipping fee is only charged when the customer pays. Set the payer to "
                + "'customer', or leave the fee at zero.");
    }
}

/// <summary>
/// Rules for the <c>settlements</c> section (Step 18).
/// </summary>
/// <remarks>
/// The rates are bounded rather than fixed. A statutory rate is what a notification says it is, and
/// a validator that hard-coded half a per cent would refuse the day the Gazette changed it — so the
/// bounds are wide enough for any plausible rate and narrow enough to catch the operator who typed
/// a percentage where a fraction belonged.
/// </remarks>
internal sealed class SettlementSettingsValidator : AbstractValidator<SettlementSettings>
{
    public SettlementSettingsValidator()
    {
        RuleFor(settlement => settlement.Frequency)
            .Must(frequency => SettlementFrequencies.All.Contains(frequency, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"A settlement frequency must be one of: {string.Join(", ", SettlementFrequencies.All)}.");

        RuleFor(settlement => settlement.WeekStartDay).InclusiveBetween(1, 7);
        RuleFor(settlement => settlement.HoldDays).InclusiveBetween(0, 90);

        RuleFor(settlement => settlement.MinimumPayoutAmount).InclusiveBetween(0m, 1_000_000m);
        RuleFor(settlement => settlement.PayoutApprovalThreshold).InclusiveBetween(0m, 100_000_000m);

        RuleFor(settlement => settlement.PlatformFeePercent).InclusiveBetween(0m, 100m);
        RuleFor(settlement => settlement.PlatformFeeFixed).InclusiveBetween(0m, 100_000m);
        RuleFor(settlement => settlement.PlatformServiceGstRate).InclusiveBetween(0m, 50m);
        RuleFor(settlement => settlement.PaymentGatewayFeePercent).InclusiveBetween(0m, 25m);

        RuleFor(settlement => settlement.TcsRatePercent).InclusiveBetween(0m, 10m);
        RuleFor(settlement => settlement.TdsRatePercent).InclusiveBetween(0m, 30m);
        RuleFor(settlement => settlement.TdsRateWithoutPanPercent).InclusiveBetween(0m, 30m);
        RuleFor(settlement => settlement.TdsAnnualThreshold).InclusiveBetween(0m, 100_000_000m);

        // A rate of zero with the deduction switched on is a return that will be filed as nil, and
        // it is far likelier to be a half-finished edit than a deliberate choice. Turning the
        // deduction off is how a deployment says it is not collecting.
        RuleFor(settlement => settlement.TcsRatePercent)
            .GreaterThan(0m)
            .When(settlement => settlement.TcsEnabled)
            .WithMessage("Tax collected at source is switched on, so its rate cannot be zero.");

        RuleFor(settlement => settlement.TdsRatePercent)
            .GreaterThan(0m)
            .When(settlement => settlement.TdsEnabled)
            .WithMessage("Tax deducted at source is switched on, so its rate cannot be zero.");

        // Charging a fee that is set to nothing is a fee that will surprise a seller the day
        // somebody fills the number in, and leaving a number in while the switch is off is a rate
        // that looks live on the settings screen and is not.
        RuleFor(settlement => settlement.PaymentGatewayFeePercent)
            .GreaterThan(0m)
            .When(settlement => settlement.ChargeGatewayFeeToVendor)
            .WithMessage("The gateway fee is charged to sellers, so its rate cannot be zero.");

        RuleFor(settlement => settlement.PaymentGatewayFeePercent)
            .Equal(0m)
            .When(settlement => !settlement.ChargeGatewayFeeToVendor)
            .WithMessage(
                "The gateway fee is absorbed by the platform. Switch the charge on, or leave the "
                + "rate at zero.");
    }
}

/// <summary>
/// Rules for the <c>search</c> section (Step 19).
/// </summary>
/// <remarks>
/// The four weights are bounded but not constrained against each other. The score is a weighted sum,
/// so only their ratios matter and there is no combination of non-negative numbers that is wrong —
/// a store that wants results ordered purely by sales sets every other weight to zero, and that is a
/// merchandising choice rather than a mistake.
/// </remarks>
internal sealed class SearchSettingsValidator : AbstractValidator<SearchSettings>
{
    public SearchSettingsValidator()
    {
        RuleFor(search => search.MinimumQueryLength).InclusiveBetween(1, 10);
        RuleFor(search => search.MaxQueryLength).InclusiveBetween(20, 500);
        RuleFor(search => search.MaxFacetValues).InclusiveBetween(1, 200);
        RuleFor(search => search.SuggestionLimit).InclusiveBetween(1, 50);

        RuleFor(search => search.DefaultSort)
            .Must(SearchSorts.Contains)
            .WithMessage($"A sort must be one of: {string.Join(", ", SearchSorts.All)}.");

        // Below about 0.2 a search for one product returns the catalogue, and above about 0.5 the
        // fuzzy pass stops correcting the typos it exists for. The bounds are wider than that on
        // purpose — a store with very short product names genuinely needs a higher threshold.
        RuleFor(search => search.FuzzyThreshold).InclusiveBetween(0.05m, 0.95m);

        RuleFor(search => search.RelevanceWeight).InclusiveBetween(0m, 100m);
        RuleFor(search => search.PopularityWeight).InclusiveBetween(0m, 100m);
        RuleFor(search => search.RatingWeight).InclusiveBetween(0m, 100m);
        RuleFor(search => search.AvailabilityBoost).InclusiveBetween(0m, 100m);

        // Every weight at zero is not a preference, it is a scoring function that returns the same
        // number for every row — and the results would then come back in whatever order the database
        // found them, which looks exactly like a broken index.
        RuleFor(search => search)
            .Must(search => search.RelevanceWeight
                            + search.PopularityWeight
                            + search.RatingWeight
                            + search.AvailabilityBoost > 0m)
            .WithName("weights")
            .WithMessage("At least one ranking weight must be greater than zero.");

        RuleFor(search => search.PriceBands)
            .NotNull()
            .Must(bands => bands.Count <= 20)
            .WithMessage("A price filter may have at most twenty bands.");

        RuleFor(search => search.PriceBands)
            .Must(bands => bands.All(bound => bound > 0m))
            .When(search => search.PriceBands is not null)
            .WithMessage("Every price band bound must be greater than zero.");

        // Ascending, because the bands are read as ranges between consecutive bounds and an
        // out-of-order list would produce a band that cannot contain anything.
        RuleFor(search => search.PriceBands)
            .Must(bands => bands.Zip(bands.Skip(1)).All(pair => pair.Second > pair.First))
            .When(search => search.PriceBands is not null)
            .WithMessage("Price band bounds must be listed in ascending order, each above the last.");
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

/// <summary>
/// Rules for the <c>seo</c> section (Step 20).
/// </summary>
/// <remarks>
/// The base URL is the only field with real teeth, and it earns them: every absolute URL this
/// platform emits — the sitemap's <c>loc</c>, the canonical tag, the JSON-LD identifiers — is built
/// from it, so a value with a trailing slash or a missing scheme produces thousands of malformed
/// URLs rather than one. It is still allowed to be blank, because a deployment nobody has pointed at
/// a domain yet has to be able to save the rest of the section.
/// </remarks>
internal sealed class SeoSettingsValidator : AbstractValidator<SeoSettings>
{
    public SeoSettingsValidator()
    {
        RuleFor(seo => seo.CanonicalBaseUrl)
            .Matches(@"^https?://[^/\s]+$")
            .When(seo => !string.IsNullOrWhiteSpace(seo.CanonicalBaseUrl))
            .WithMessage("The canonical base URL must be a scheme and host with no trailing slash, "
                         + "for example https://www.example.in.");

        RuleFor(seo => seo.TitleTemplate)
            .NotEmpty()
            .MaximumLength(200)
            .Must(template => template.Contains("{title}", StringComparison.Ordinal))
            .WithMessage("The title template must contain {title}.");

        RuleFor(seo => seo.DefaultMetaDescription).MaximumLength(400);
        RuleFor(seo => seo.OrganizationName).MaximumLength(200);
        RuleFor(seo => seo.RobotsExtra).MaximumLength(4_000);

        RuleFor(seo => seo.TwitterCardType)
            .Must(card => card is "summary" or "summary_large_image")
            .WithMessage("The card type must be summary or summary_large_image.");

        // Well inside the sitemap protocol's fifty thousand, and low enough that one page can still
        // be generated inside a single request.
        RuleFor(seo => seo.SitemapPageSize).InclusiveBetween(100, 50_000);

        RuleFor(seo => seo.DisallowedPaths)
            .NotNull()
            .Must(paths => paths.Count <= 100)
            .WithMessage("At most one hundred disallowed paths.");

        // A robots directive without a leading slash matches nothing, silently. It is the single
        // most common way a robots.txt ends up not doing what its author believed.
        RuleFor(seo => seo.DisallowedPaths)
            .Must(paths => paths.All(path => path.StartsWith('/')))
            .When(seo => seo.DisallowedPaths is not null)
            .WithMessage("Every disallowed path must begin with a slash.");

        RuleFor(seo => seo.SocialProfileUrls)
            .NotNull()
            .Must(urls => urls.Count <= 20)
            .WithMessage("At most twenty social profiles.");

        RuleFor(seo => seo.SocialProfileUrls)
            .Must(urls => urls.All(url => Uri.TryCreate(url, UriKind.Absolute, out var parsed)
                                          && parsed.Scheme is "http" or "https"))
            .When(seo => seo.SocialProfileUrls is not null)
            .WithMessage("Every social profile must be an absolute http or https URL.");
    }
}
