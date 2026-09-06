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

/// <summary>
/// How the storefront chooses which seller's offer a shopper sees first when several sell the same
/// variant (docs/03-database-design.md §4.4).
/// </summary>
/// <remarks>
/// A section rather than a constant because the answer is commercial, not technical: a marketplace
/// competing on price ranks by landed price, one competing on service ranks by dispatch SLA, and
/// the operator changes their mind about that far more often than the code is deployed. The
/// criteria are applied in the declared order, each one breaking the tie the previous left.
/// </remarks>
public sealed record BuyBoxSettings : ISettingsSection<BuyBoxSettings>
{
    /// <inheritdoc cref="ISettingsSection{TSelf}.SectionKey" />
    public static string SectionKey => "buy-box";

    /// <inheritdoc cref="ISettingsSection{TSelf}.IsPublic" />
    public static bool IsPublic => false;

    /// <summary>
    /// The ranking criteria, most significant first. Unknown names are ignored, and an empty or
    /// exhausted list falls back to the oldest offer, which is stable and never arbitrary.
    /// </summary>
    public IReadOnlyList<string> Criteria { get; init; } =
    [
        BuyBoxCriteria.LandedPrice,
        BuyBoxCriteria.VendorRating,
        BuyBoxCriteria.DispatchSla,
        BuyBoxCriteria.StockAvailability,
    ];

    /// <summary>
    /// Whether an offer whose seller has no rating yet is ranked as average rather than worst.
    /// </summary>
    /// <remarks>
    /// On by default. Ranking an unrated seller last is a marketplace no new seller can ever get a
    /// first sale on, which is the classic cold-start failure of a buy box.
    /// </remarks>
    public bool TreatUnratedAsAverage { get; init; } = true;
}

/// <summary>
/// The commercial levers of the price and tax engine (docs/03-database-design.md §4.6).
/// </summary>
/// <remarks>
/// <para>
/// A settings section rather than module configuration, on the split every module here draws: what
/// a shopkeeper decides lives where a shopkeeper can change it, and what the platform decides lives
/// in <c>appsettings</c>. A cash-on-delivery fee, a loyalty rate and how much of an order may be
/// paid in store credit are all commercial choices, and they change more often than the product is
/// deployed.
/// </para>
/// <para>
/// Not public. A shopper is shown a COD fee on their quote, never the rule that produced it, and
/// publishing the wallet ceiling would be publishing the shape of an abuse.
/// </para>
/// </remarks>
public sealed record PricingSettings : ISettingsSection<PricingSettings>
{
    /// <inheritdoc cref="ISettingsSection{TSelf}.SectionKey" />
    public static string SectionKey => "pricing";

    /// <inheritdoc cref="ISettingsSection{TSelf}.IsPublic" />
    public static bool IsPublic => false;

    /// <summary>
    /// The handling fee added to a cash-on-delivery order, inclusive of GST. Zero offers COD free.
    /// </summary>
    public decimal CodHandlingFee { get; init; }

    /// <summary>
    /// The GST percentage on the delivery charge. Eighteen is the rate for a courier service; a
    /// store that treats delivery as part of a composite supply sets it to the goods' rate.
    /// </summary>
    public decimal ShippingTaxRate { get; init; } = 18m;

    /// <summary>
    /// Whether the amount payable is rounded to a whole rupee, with the difference shown as a
    /// rounding line. On, which is what section 170 of the CGST Act requires.
    /// </summary>
    public bool RoundToNearestRupee { get; init; } = true;

    /// <summary>
    /// The percentage of a completed order credited back as loyalty, or zero for no programme.
    /// Accrual is written by Orders when an order completes; this is the rate it uses.
    /// </summary>
    public decimal LoyaltyAccrualPercent { get; init; }

    /// <summary>The most loyalty one order may earn, or zero for no cap.</summary>
    public decimal LoyaltyMaxAccrualPerOrder { get; init; }

    /// <summary>
    /// The most of one order that may be paid in store credit, as a percentage. A hundred lets a
    /// shopper pay entirely in credit; a lower figure keeps some cash in every transaction, which
    /// is the usual answer where credit is issued as a refund.
    /// </summary>
    public decimal WalletMaxRedeemPercent { get; init; } = 100m;

    /// <summary>How many days issued credit lasts, or zero for credit that does not lapse.</summary>
    public int WalletExpiryDays { get; init; }
}

/// <summary>
/// The governance levers on money going back out (docs/07-security-compliance.md §4).
/// </summary>
/// <remarks>
/// <para>
/// A settings section rather than module configuration, on the split every module here draws. When
/// a refund needs a second signature, and whether a cancelled order is refunded without anybody
/// asking, are decisions the business owns and revisits — usually after an incident — and they must
/// not need a deployment. The cadences, retry budgets and skew windows around them stay in
/// <c>appsettings</c>, where a shopkeeper cannot reach them.
/// </para>
/// <para>
/// Not public. Publishing the value above which a human has to look at a refund would be publishing
/// the shape of an abuse.
/// </para>
/// </remarks>
public sealed record PaymentSettings : ISettingsSection<PaymentSettings>
{
    /// <inheritdoc cref="ISettingsSection{TSelf}.SectionKey" />
    public static string SectionKey => "payments";

    /// <inheritdoc cref="ISettingsSection{TSelf}.IsPublic" />
    public static bool IsPublic => false;

    /// <summary>
    /// The value at or below which a refund is approved by the person raising it, in the store
    /// currency. Anything above it waits for a second signature.
    /// </summary>
    /// <remarks>
    /// Zero means every refund needs two people, which is the correct setting for a business that
    /// has just had an incident and the wrong one for a support desk handling fifty small refunds a
    /// day. The default sits where a mistake is embarrassing rather than material.
    /// </remarks>
    public decimal RefundApprovalThreshold { get; init; } = 5000m;

    /// <summary>
    /// Whether cancelling an order that has been paid for raises the refund automatically.
    /// </summary>
    /// <remarks>
    /// On. A shopper whose order was cancelled after they paid is owed their money without having
    /// to ask for it, and the alternative - a queue somebody works through - is how refunds come to
    /// take a fortnight. The approval threshold still applies to what this raises.
    /// </remarks>
    public bool AutoRefundOnCancellation { get; init; } = true;

    /// <summary>The speed automatic refunds are sent at, where the rail offers a choice.</summary>
    /// <remarks>
    /// Normal, because instant refunds carry a per-transaction fee and the shopper is not waiting at
    /// a counter. An operator can send a specific refund faster from the admin.
    /// </remarks>
    public bool PreferInstantRefunds { get; init; }

    /// <summary>
    /// Days a courier has to remit cash they collected before it is flagged as overdue.
    /// </summary>
    /// <remarks>
    /// Not enforced against anybody - it is what the overdue-cash report filters on, which is the
    /// only lever a marketplace has over money sitting in a courier's account.
    /// </remarks>
    public int CodRemittanceGraceDays { get; init; } = 7;
}

/// <summary>The criteria names <see cref="BuyBoxSettings.Criteria"/> accepts.</summary>
public static class BuyBoxCriteria
{
    /// <summary>Cheapest offer price first. The default first criterion.</summary>
    public const string LandedPrice = "landed-price";

    /// <summary>Best-rated seller first.</summary>
    public const string VendorRating = "vendor-rating";

    /// <summary>Fastest promised dispatch first.</summary>
    public const string DispatchSla = "dispatch-sla";

    /// <summary>Offers that can actually be shipped today, first.</summary>
    public const string StockAvailability = "stock-availability";

    /// <summary>Every name this platform understands.</summary>
    public static readonly IReadOnlyList<string> All = [LandedPrice, VendorRating, DispatchSla, StockAvailability];
}

/// <summary>
/// Where this store is willing to deliver (ADR-018).
/// </summary>
/// <remarks>
/// <para>
/// <b>Not the same question as serviceability.</b> Whether a courier can reach a PIN code is the
/// courier's answer, cached in <c>shipping.serviceability_cache</c> and refreshed nightly. This is
/// the shopkeeper's answer: the area they have decided to trade in. An address must pass both, and
/// the two refuse with different codes, because one is a fact about India's logistics and the other
/// is a decision an operator can reverse in this screen.
/// </para>
/// <para>
/// It is a settings section rather than a shipping zone with no rate, because a zone is a pricing
/// construct: its absence is indistinguishable from a misconfigured rate card, and it would refuse
/// the order at the last possible moment rather than on the product page.
/// </para>
/// <para>
/// Public, because the storefront has to be able to say where the store delivers before a shopper
/// types a PIN code, and because the refusal message is the operator's own words.
/// </para>
/// </remarks>
public sealed record DeliveryCoverageSettings : ISettingsSection<DeliveryCoverageSettings>
{
    /// <inheritdoc cref="ISettingsSection{TSelf}.SectionKey" />
    public static string SectionKey => "delivery-coverage";

    /// <inheritdoc cref="ISettingsSection{TSelf}.IsPublic" />
    public static bool IsPublic => true;

    /// <summary>
    /// Whether deliveries are restricted at all.
    /// </summary>
    /// <remarks>
    /// On, and restricted to Hyderabad, because that is where this store trades. Turning it off
    /// restores national trading in one edit, which is what a business that outgrows one city does.
    /// </remarks>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Cities delivered to, matched case-insensitively against the PIN code's city.
    /// </summary>
    /// <remarks>
    /// Secunderabad is listed beside Hyderabad because the postal service treats it as a separate
    /// city and a delivery operation does not.
    /// </remarks>
    public IReadOnlyList<string> AllowedCities { get; init; } = ["Hyderabad", "Secunderabad"];

    /// <summary>
    /// PIN-code prefixes delivered to. <c>500</c> is Hyderabad.
    /// </summary>
    /// <remarks>
    /// The neighbouring <c>501</c> and <c>502</c> ranges — Rangareddy and Sangareddy, the outer
    /// districts of the metropolitan region — are excluded deliberately and are one edit away.
    /// "Hyderabad" means different things to a postal service and to a delivery operation, and
    /// guessing the larger meaning would have the store promising deliveries nobody has agreed to
    /// make.
    /// </remarks>
    public IReadOnlyList<string> AllowedPincodePrefixes { get; init; } = ["500"];

    /// <summary>Individual PIN codes delivered to, whatever the city and prefix rules say.</summary>
    public IReadOnlyList<string> AllowedPincodes { get; init; } = [];

    /// <summary>PIN codes never delivered to. A block beats every allow rule.</summary>
    public IReadOnlyList<string> BlockedPincodes { get; init; } = [];

    /// <summary>What a shopper outside the area is told, in the operator's own words.</summary>
    public string Message { get; init; } = "We currently deliver within Hyderabad only.";
}

/// <summary>
/// How this store handles goods coming back.
/// </summary>
/// <remarks>
/// <para>
/// Not public, and the distinction matters. How long a shopper has is public and lives on
/// <see cref="CommerceSettings.ReturnWindowDays"/>, where a product page and a listing disclosure
/// both read it; <em>who pays</em>, <em>what is approved without a human</em> and <em>what happens
/// to the goods</em> are the operator's rules and are none of a shopper's business.
/// </para>
/// <para>
/// Every one of these is a policy a business revisits — usually after a month of returns costs more
/// than anybody budgeted for — so none of them is configuration and none needs a deploy.
/// </para>
/// </remarks>
public sealed record ReturnsSettings : ISettingsSection<ReturnsSettings>
{
    /// <inheritdoc />
    public static string SectionKey => "returns";

    /// <inheritdoc />
    public static bool IsPublic => false;

    /// <summary>Whether shoppers may ask for a replacement rather than their money back.</summary>
    /// <remarks>
    /// Off by default. A replacement is a second dispatch against an order that has already been
    /// settled, and a store should decide it wants that before it offers it.
    /// </remarks>
    public bool ReplacementsEnabled { get; init; }

    /// <summary>
    /// A return at or below this value is approved without anybody looking at it.
    /// </summary>
    /// <remarks>
    /// Zero — the default — means every return is reviewed. It is the mirror of the refund approval
    /// threshold: below a figure, the cost of a person deciding exceeds the cost of being wrong.
    /// </remarks>
    public decimal AutoApproveBelow { get; init; }

    /// <summary>Whether a shopper must attach a photograph before a return can be asked for.</summary>
    /// <remarks>
    /// The blanket rule. A reason code can demand evidence on its own, which is the usual
    /// arrangement — a damaged parcel needs a photograph and a change of mind does not.
    /// </remarks>
    public bool RequireEvidence { get; init; }

    /// <summary>How many photographs a shopper may attach to one request.</summary>
    public int MaxEvidenceFiles { get; init; } = 5;

    /// <summary>
    /// Who pays to send the goods back, unless the reason code overrides it.
    /// </summary>
    /// <remarks>
    /// One of <see cref="ReturnShippingPayers"/>. The default is the platform, because in Indian
    /// e-commerce a paid reverse pickup is the exception and charging for one is a thing a store
    /// chooses to do.
    /// </remarks>
    public string DefaultShippingPayer { get; init; } = ReturnShippingPayers.Platform;

    /// <summary>What is charged for a reverse pickup when the shopper is the one paying.</summary>
    public decimal ReturnShippingFee { get; init; }

    /// <summary>
    /// Whether the original delivery charge goes back when the whole sub-order is returned.
    /// </summary>
    /// <remarks>
    /// True by default and only ever on a whole return: a shopper returning one of three items has
    /// still had the parcel delivered, and refunding the freight on it would refund a service that
    /// was performed.
    /// </remarks>
    public bool RefundShippingOnFullReturn { get; init; } = true;

    /// <summary>Where the money goes unless the shopper is offered a choice and takes it.</summary>
    /// <remarks>
    /// One of <see cref="RefundModes"/>. Back to the original instrument by default, which is what
    /// the RBI's own grievance guidance and every shopper expects.
    /// </remarks>
    public string DefaultRefundMode { get; init; } = RefundModes.Original;

    /// <summary>
    /// Whether a shopper may choose store credit instead, usually for a faster refund.
    /// </summary>
    /// <remarks>
    /// Reads as a courtesy and is also the only refund a cash-on-delivery order can have without a
    /// bank transfer, so a store that turns the wallet off should know it is turning that off too.
    /// </remarks>
    public bool AllowWalletRefunds { get; init; } = true;

    /// <summary>
    /// Whether passing quality control refunds the shopper without anybody clicking again.
    /// </summary>
    /// <remarks>
    /// On by default. The decision that matters was made at QC; making somebody confirm it a second
    /// time adds a day to every refund and catches nothing. The maker–checker threshold in
    /// <see cref="PaymentSettings.RefundApprovalThreshold"/> still applies underneath, so a large
    /// refund still waits for a second pair of eyes.
    /// </remarks>
    public bool AutoRefundOnQcPass { get; init; } = true;

    /// <summary>What quality control does with goods it passes, unless the inspector says otherwise.</summary>
    /// <remarks>One of <see cref="ReturnDispositions"/>.</remarks>
    public string DefaultPassedDisposition { get; init; } = ReturnDispositions.Restock;

    /// <summary>What quality control does with goods it fails, unless the inspector says otherwise.</summary>
    /// <remarks>
    /// Quarantine rather than scrap, on purpose. A failed inspection is a disagreement with the
    /// shopper about the state of the goods, and scrapping them destroys the evidence before anybody
    /// has heard the other side.
    /// </remarks>
    public string DefaultFailedDisposition { get; init; } = ReturnDispositions.Quarantine;

    /// <summary>
    /// How many days a courier has to bring an approved return back before it is chased.
    /// </summary>
    /// <remarks>
    /// The number the stale-return sweep counts against. It is not a deadline for the shopper — it
    /// is how long the platform waits before an operator is told a pickup has gone quiet.
    /// </remarks>
    public int PickupSlaDays { get; init; } = 7;
}

/// <summary>Who bears the cost of sending goods back.</summary>
public static class ReturnShippingPayers
{
    /// <summary>The store pays. The default, and what a shopper expects for a fault.</summary>
    public const string Platform = "platform";

    /// <summary>The seller pays. Used where the fault is theirs and the commission plan says so.</summary>
    public const string Vendor = "vendor";

    /// <summary>The shopper pays, and the fee is deducted from what goes back to them.</summary>
    public const string Customer = "customer";

    /// <summary>Every payer this platform recognises.</summary>
    public static readonly IReadOnlyList<string> All = [Platform, Vendor, Customer];
}

/// <summary>Where refunded money goes.</summary>
public static class RefundModes
{
    /// <summary>Back to the card, account or wallet it was paid from.</summary>
    public const string Original = "original";

    /// <summary>Into the shopper's store credit.</summary>
    public const string Wallet = "wallet";

    /// <summary>Every mode this platform recognises.</summary>
    public static readonly IReadOnlyList<string> All = [Original, Wallet];
}

/// <summary>What becomes of goods that have come back.</summary>
/// <remarks>
/// The same three words <c>KlaraHome.Contracts.Inventory.RestockDisposition</c> spells as an enum.
/// They are strings here because a settings section is JSON an operator edits, and a stored enum
/// name that stopped matching would be a setting nobody could correct through the screen.
/// </remarks>
public static class ReturnDispositions
{
    /// <summary>Saleable. Back on supply.</summary>
    public const string Restock = "Restock";

    /// <summary>Not saleable. Written off supply.</summary>
    public const string Scrap = "Scrap";

    /// <summary>Held, and neither.</summary>
    public const string Quarantine = "Quarantine";

    /// <summary>Every disposition this platform recognises.</summary>
    public static readonly IReadOnlyList<string> All = [Restock, Scrap, Quarantine];
}

/// <summary>
/// What the platform keeps out of a sale, and how often it pays the rest over
/// (docs/02-domain-model.md §7.3, docs/03-database-design.md §4.12).
/// </summary>
/// <remarks>
/// <para>
/// Not public, and none of it is configuration. Every value here is a commercial or a statutory
/// decision that changes on a schedule the product is not deployed on: a fee is renegotiated, a
/// cycle is shortened because sellers complained, and a TCS rate is changed by a notification in
/// the Gazette. A deployment that had to be rebuilt to follow a rate change would be a deployment
/// that files a wrong return.
/// </para>
/// <para>
/// Commission is deliberately absent. What a seller is charged for a sale is resolved from their
/// own plan when the order is placed and frozen onto the order line — a store-wide commission rate
/// here would be a second answer to a question that already has one, and the two would disagree the
/// first time a plan changed.
/// </para>
/// </remarks>
public sealed record SettlementSettings : ISettingsSection<SettlementSettings>
{
    /// <inheritdoc />
    public static string SectionKey => "settlements";

    /// <inheritdoc />
    public static bool IsPublic => false;

    /// <summary>How often a seller's period is drawn: one of <see cref="SettlementFrequencies"/>.</summary>
    /// <remarks>
    /// Weekly by default. It is the cadence most Indian marketplaces settle on, and it is short
    /// enough that a small seller's cash flow does not depend on the platform's convenience.
    /// </remarks>
    public string Frequency { get; init; } = SettlementFrequencies.Weekly;

    /// <summary>
    /// The day a weekly period starts on, as <c>1</c> for Monday through <c>7</c> for Sunday.
    /// </summary>
    /// <remarks>
    /// Ignored for the other frequencies, which anchor on a calendar boundary. Monday by default, so
    /// a week's statement covers a week a human recognises.
    /// </remarks>
    public int WeekStartDay { get; init; } = 1;

    /// <summary>
    /// How long after a period ends before it may be closed, in days.
    /// </summary>
    /// <remarks>
    /// The hold that makes a settlement safe to pay. A sale delivered on the last day of a period
    /// still has this many days in which a shopper can send it back, and a return raised inside the
    /// hold reverses in the same cycle rather than clawing money back from the next one. Set it to
    /// the store's own return window, which is the default it takes.
    /// </remarks>
    public int HoldDays { get; init; } = 7;

    /// <summary>
    /// The smallest balance worth sending, in the store currency.
    /// </summary>
    /// <remarks>
    /// A cycle below this is closed and carried forward rather than paid: a transfer costs the
    /// platform a fee and the seller a line on their bank statement, and neither is worth it for a
    /// few rupees. Zero pays every balance.
    /// </remarks>
    public decimal MinimumPayoutAmount { get; init; } = 100m;

    /// <summary>
    /// The value at or above which a payout batch needs a second signature.
    /// </summary>
    /// <remarks>
    /// The maker–checker threshold for money leaving the platform, and the sibling of
    /// <see cref="PaymentSettings.RefundApprovalThreshold"/>. Zero — the default — means every batch
    /// is approved by somebody other than the person who built it, which is the right starting point
    /// for a control on outbound payments.
    /// </remarks>
    public decimal PayoutApprovalThreshold { get; init; }

    /// <summary>
    /// The marketplace fee charged on a sale, as a percentage, on top of commission.
    /// </summary>
    /// <remarks>
    /// Zero by default. It exists because commission and platform fee are two different charges in
    /// most Indian marketplace agreements — commission is category-dependent and negotiated, and
    /// this is the flat cost of being on the platform at all.
    /// </remarks>
    public decimal PlatformFeePercent { get; init; }

    /// <summary>A flat marketplace fee per sub-order, in the store currency.</summary>
    public decimal PlatformFeeFixed { get; init; }

    /// <summary>The GST rate charged on the platform's own services, as a percentage.</summary>
    /// <remarks>
    /// Eighteen per cent, which is the rate on commission and marketplace services. The platform
    /// raises its own tax invoice for these, so the tax is a real charge to the seller and not a
    /// rounding of the fee.
    /// </remarks>
    public decimal PlatformServiceGstRate { get; init; } = 18m;

    /// <summary>
    /// The gateway's fee on a collection, as a percentage, passed on to the seller.
    /// </summary>
    /// <remarks>
    /// Zero by default, which means the platform absorbs it. Where it is charged on, the GST on the
    /// gateway's fee is charged with it — a gateway fee net of its own tax would under-recover.
    /// </remarks>
    public decimal PaymentGatewayFeePercent { get; init; }

    /// <summary>Whether the gateway fee is charged to the seller at all.</summary>
    public bool ChargeGatewayFeeToVendor { get; init; }

    /// <summary>
    /// Whether the freight the platform paid is charged back to the seller.
    /// </summary>
    /// <remarks>
    /// On by default, and it is one half of a pair. A seller is credited everything the shopper paid
    /// for their part of the order, delivery included; this then charges the delivery back, because
    /// in the default arrangement it is the platform that books the courier and pays for it. A store
    /// whose sellers ship on their own account turns it off, and the seller keeps what the shopper
    /// paid for delivery.
    /// </remarks>
    public bool ChargeShippingToVendor { get; init; } = true;

    /// <summary>Whether tax is collected at source under section 52 of the CGST Act.</summary>
    /// <remarks>
    /// On. An electronic commerce operator through whom a supplier makes a taxable supply is
    /// <em>required</em> to collect it; turning this off is a deployment that is not a marketplace,
    /// which is a real case and not the default one.
    /// </remarks>
    public bool TcsEnabled { get; init; } = true;

    /// <summary>
    /// The TCS rate, as a percentage of the net value of taxable supplies.
    /// </summary>
    /// <remarks>
    /// Half a per cent, which is the rate in force. It is charged on the taxable value and not on
    /// what the shopper paid: the GST inside the price is not part of the consideration for the
    /// supply.
    /// </remarks>
    public decimal TcsRatePercent { get; init; } = 0.5m;

    /// <summary>Whether tax is deducted at source under section 194-O of the Income-tax Act.</summary>
    public bool TdsEnabled { get; init; } = true;

    /// <summary>
    /// The TDS rate, as a percentage of the gross amount of sales.
    /// </summary>
    /// <remarks>
    /// A tenth of a per cent. Unlike TCS this is charged on the <em>gross</em> amount — the figure
    /// including GST — which is why the two deductions read from two different bases on the same
    /// statement.
    /// </remarks>
    public decimal TdsRatePercent { get; init; } = 0.1m;

    /// <summary>
    /// The TDS rate applied to a seller with no PAN on record, as a percentage.
    /// </summary>
    /// <remarks>
    /// Five per cent, which is what section 206AA requires where the deductee has not furnished a
    /// PAN. It is a separate value rather than a multiple of the ordinary rate because the two are
    /// set by two different provisions and have moved independently.
    /// </remarks>
    public decimal TdsRateWithoutPanPercent { get; init; } = 5m;

    /// <summary>
    /// Gross sales in a financial year below which no TDS is deducted, in the store currency.
    /// </summary>
    /// <remarks>
    /// Zero — deduct from the first rupee — because the statutory relief is narrow: it applies only
    /// to an individual or a Hindu undivided family who has furnished a PAN or Aadhaar, and applying
    /// it to a company would be a short deduction the platform is liable for. An operator who has
    /// established that their sellers qualify sets it to the threshold in force.
    /// </remarks>
    public decimal TdsAnnualThreshold { get; init; }

    /// <summary>Whether a closed cycle is put into a payout batch without anybody asking.</summary>
    /// <remarks>
    /// Off. Building the batch is cheap and reversible; sending money is neither, and a store should
    /// decide it wants an unattended payout run before it gets one. The approval threshold still
    /// applies to what the run produces.
    /// </remarks>
    public bool AutoBatchOnClose { get; init; }
}

/// <summary>How often a settlement period is drawn.</summary>
/// <remarks>
/// Strings rather than an enum because the value is stored in a JSON settings document read by an
/// admin screen, and a number in that document would mean nothing to the operator reading it.
/// </remarks>
public static class SettlementFrequencies
{
    /// <summary>A seven-day period, anchored on the configured week start.</summary>
    public const string Weekly = "weekly";

    /// <summary>Two periods a month: the 1st to the 15th, and the 16th to month end.</summary>
    public const string Fortnightly = "fortnightly";

    /// <summary>One period per calendar month.</summary>
    public const string Monthly = "monthly";

    /// <summary>Every frequency this platform draws.</summary>
    public static readonly IReadOnlyList<string> All = [Weekly, Fortnightly, Monthly];
}

/// <summary>
/// How the storefront finds things (Step 19).
/// </summary>
/// <remarks>
/// <para>
/// Public, because the storefront applies several of these itself: it has to know how many
/// characters to wait for before asking, which sort it is opening on, and what the price bands are
/// so it can render the filter before the first response comes back.
/// </para>
/// <para>
/// The four weights are the whole of the ranking policy, and they are settings rather than
/// configuration for the reason merchandising decisions always are: "our results feel stale" is a
/// complaint answered by raising the popularity weight on a Tuesday afternoon, and a deployment
/// that needed a rebuild to answer it would be answered with a spreadsheet instead. They are
/// deliberately not normalised — the score is a weighted sum, so doubling every weight changes
/// nothing, and only their ratios matter.
/// </para>
/// </remarks>
public sealed record SearchSettings : ISettingsSection<SearchSettings>
{
    /// <inheritdoc />
    public static string SectionKey => "search";

    /// <inheritdoc />
    public static bool IsPublic => true;

    /// <summary>The shortest query the engine will answer.</summary>
    /// <remarks>
    /// Two characters. One character matches most of the catalogue, which is a slow query and a
    /// useless answer, and the suggestion box would fire it on the first keystroke of every search.
    /// </remarks>
    public int MinimumQueryLength { get; init; } = 2;

    /// <summary>The longest query the engine will accept before truncating it.</summary>
    public int MaxQueryLength { get; init; } = 120;

    /// <summary>The order results come back in when the caller does not ask: one of <see cref="SearchSorts"/>.</summary>
    public string DefaultSort { get; init; } = SearchSorts.Relevance;

    /// <summary>
    /// Whether offers with no stock are left out of results entirely.
    /// </summary>
    /// <remarks>
    /// Off by default, and that is a merchandising judgement rather than a technical one. An
    /// out-of-stock product a shopper can still see is a product they can subscribe to; one that has
    /// vanished looks like a store that does not carry it. The availability boost below is the
    /// gentler instrument, and it is the one to reach for first.
    /// </remarks>
    public bool HideUnavailable { get; init; }

    /// <summary>
    /// Whether a query that matched nothing is retried against a fuzzy index.
    /// </summary>
    /// <remarks>
    /// On, and it is the difference between "cushin" finding cushions and "cushin" finding nothing.
    /// The fuzzy pass runs only when the exact pass returned no rows, so the common case pays
    /// nothing for it.
    /// </remarks>
    public bool EnableFuzzyFallback { get; init; } = true;

    /// <summary>How alike two words must be for the fuzzy pass to call them a match, from 0 to 1.</summary>
    /// <remarks>
    /// PostgreSQL's trigram similarity. Below about 0.2 a search for one product returns the whole
    /// catalogue; above about 0.5 it stops correcting the typos it exists to correct.
    /// </remarks>
    public decimal FuzzyThreshold { get; init; } = 0.3m;

    /// <summary>How much text relevance counts towards the score.</summary>
    /// <remarks>
    /// Contributes nothing at all on a browse page, where there is no query to be relevant to; on
    /// those pages the popularity weight is doing all of the work, which is what makes a category
    /// page open on the things people actually buy.
    /// </remarks>
    public decimal RelevanceWeight { get; init; } = 1.0m;

    /// <summary>How much past sales count towards the score.</summary>
    public decimal PopularityWeight { get; init; } = 0.35m;

    /// <summary>How much the review score counts.</summary>
    public decimal RatingWeight { get; init; } = 0.15m;

    /// <summary>How much a product in stock is lifted above an identical one that is not.</summary>
    /// <remarks>
    /// A boost rather than a filter, and the reason <see cref="HideUnavailable"/> can stay off: an
    /// out-of-stock product still appears, below its available competitors, where a shopper can ask
    /// to be told when it returns.
    /// </remarks>
    public decimal AvailabilityBoost { get; init; } = 0.25m;

    /// <summary>The most values one facet will offer before the rest are folded away.</summary>
    public int MaxFacetValues { get; init; } = 20;

    /// <summary>How many suggestions the autocomplete box is given.</summary>
    public int SuggestionLimit { get; init; } = 8;

    /// <summary>
    /// The upper bounds of the price filter's bands, ascending, in the store currency.
    /// </summary>
    /// <remarks>
    /// Bands rather than a slider, because a slider needs a distribution and a shopper needs a
    /// decision. The last band is open-ended: five bounds produce six bands, the last being
    /// "everything above the last bound".
    /// </remarks>
    public IReadOnlyList<decimal> PriceBands { get; init; } = [499m, 999m, 1999m, 4999m, 9999m];

    /// <summary>
    /// Whether queries are recorded for merchandising.
    /// </summary>
    /// <remarks>
    /// On, because what shoppers looked for and did not find is the most direct evidence a buying
    /// team has. It is a switch rather than a constant so that a deployment with a stricter reading
    /// of its own privacy notice can turn the log off without turning search off
    /// (docs/07-security-compliance.md).
    /// </remarks>
    public bool LogQueries { get; init; } = true;
}

/// <summary>
/// How the store presents itself to a crawler (docs/03-database-design.md §4.14).
/// </summary>
/// <remarks>
/// <para>
/// Added by Step 20. Every value here is a fact about the deployment rather than about the code: the
/// host the canonical URLs are built from, whether this environment wants to be indexed at all, and
/// the organisation identity a <c>schema.org</c> graph names. Putting them in configuration would
/// mean a staging site that leaked into an index needed a deploy to stop, and that is the one case
/// where minutes matter.
/// </para>
/// <para>
/// Public, because the storefront applies most of it itself — it renders the canonical tag, the
/// robots meta and the JSON-LD graph, and it has to be able to do so during server-side rendering
/// before any authenticated call could have happened.
/// </para>
/// <para>
/// <see cref="AllowIndexing"/> is the switch worth being careful with in both directions. Off, a
/// deployment publishes <c>Disallow: /</c> and a <c>noindex</c> on every page; on, it publishes the
/// real directives. It ships <b>off</b>, so a new environment is private until somebody decides
/// otherwise — the opposite default has cost more stores more traffic than any other setting on this
/// list.
/// </para>
/// </remarks>
public sealed record SeoSettings : ISettingsSection<SeoSettings>
{
    /// <inheritdoc />
    public static string SectionKey => "seo";

    /// <inheritdoc />
    public static bool IsPublic => true;

    /// <summary>
    /// The canonical origin, with scheme and no trailing slash — <c>https://www.example.in</c>.
    /// </summary>
    /// <remarks>
    /// Every absolute URL this platform emits is built from it: the sitemap's <c>loc</c>, the
    /// canonical tag, the Open Graph URL and the JSON-LD identifiers. Reading it from the inbound
    /// request's host instead would mean a crawler arriving on an internal hostname being told the
    /// canonical page lives there too.
    /// </remarks>
    public string CanonicalBaseUrl { get; init; } = string.Empty;

    /// <summary>Whether this deployment wants to be indexed at all. Off until somebody says so.</summary>
    public bool AllowIndexing { get; init; }

    /// <summary>Paths crawlers are asked to stay out of, one per entry, each beginning with a slash.</summary>
    /// <remarks>
    /// The defaults are the paths that are private, transactional or infinite — an account area, a
    /// checkout, and the faceted URLs that would otherwise give a crawler an unbounded space to walk.
    /// </remarks>
    public IReadOnlyList<string> DisallowedPaths { get; init; } =
        ["/account", "/cart", "/checkout", "/order", "/search?"];

    /// <summary>Extra lines appended to <c>robots.txt</c> verbatim, for a directive this list has no field for.</summary>
    public string? RobotsExtra { get; init; }

    /// <summary>
    /// The template a page title falls back to. <c>{title}</c> and <c>{store}</c> are substituted.
    /// </summary>
    public string TitleTemplate { get; init; } = "{title} | {store}";

    /// <summary>The meta description used when a page declares none.</summary>
    public string DefaultMetaDescription { get; init; } = string.Empty;

    /// <summary>The organisation's legal or trading name, for the <c>Organization</c> graph.</summary>
    /// <remarks>
    /// Blank means "use the branding section's store name", which is what it should almost always be.
    /// It is here for the store that trades under one name and is incorporated under another.
    /// </remarks>
    public string OrganizationName { get; init; } = string.Empty;

    /// <summary>The media file behind the organisation's logo in structured data.</summary>
    public string OrganizationLogoRef { get; init; } = string.Empty;

    /// <summary>The organisation's profiles elsewhere, which <c>sameAs</c> carries.</summary>
    public IReadOnlyList<string> SocialProfileUrls { get; init; } = [];

    /// <summary>The Twitter/X card type — <c>summary</c> or <c>summary_large_image</c>.</summary>
    public string TwitterCardType { get; init; } = "summary_large_image";

    /// <summary>How many URLs one sitemap page carries before the next begins.</summary>
    /// <remarks>
    /// The protocol's own ceiling is fifty thousand and ten megabytes. Ten thousand is well inside
    /// both and keeps a page small enough to generate inside one request.
    /// </remarks>
    public int SitemapPageSize { get; init; } = 10_000;

    /// <summary>Whether the sitemap lists product URLs. Off for a store still loading its catalogue.</summary>
    public bool SitemapIncludeProducts { get; init; } = true;

    /// <summary>Whether it lists category URLs.</summary>
    public bool SitemapIncludeCategories { get; init; } = true;
}

/// <summary>
/// The orders a result set may be returned in.
/// </summary>
/// <remarks>
/// Strings rather than an enum because they appear in a URL a shopper can bookmark and a
/// merchandiser can paste into a campaign, and because adding one is a new constant rather than a
/// contract change for every client.
/// </remarks>
public static class SearchSorts
{
    /// <summary>The blended score: text relevance, popularity, rating and availability.</summary>
    public const string Relevance = "relevance";

    /// <summary>Cheapest first.</summary>
    public const string PriceAscending = "price-asc";

    /// <summary>Dearest first.</summary>
    public const string PriceDescending = "price-desc";

    /// <summary>Most recently published first.</summary>
    public const string Newest = "newest";

    /// <summary>Deepest discount off MRP first.</summary>
    public const string Discount = "discount";

    /// <summary>Best-reviewed first.</summary>
    public const string Rating = "rating";

    /// <summary>Best-selling first.</summary>
    public const string Popularity = "popularity";

    /// <summary>Every order this platform serves.</summary>
    public static readonly IReadOnlyList<string> All =
        [Relevance, PriceAscending, PriceDescending, Newest, Discount, Rating, Popularity];

    /// <summary>Whether a value is one of them.</summary>
    /// <param name="sort">The candidate.</param>
    public static bool Contains(string? sort)
        => sort is not null && All.Contains(sort, StringComparer.OrdinalIgnoreCase);
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
