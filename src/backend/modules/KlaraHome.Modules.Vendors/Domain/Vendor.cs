using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Vendors.Domain;

/// <summary>
/// Where a seller is in their life on this marketplace (docs/02-domain-model.md §5).
/// </summary>
/// <remarks>
/// The order of the values is the order of the happy path, and nothing depends on it: the legal
/// transitions are declared in <see cref="Vendor.IsTransitionAllowed"/>, not inferred from the
/// numbers, so inserting a state later cannot silently open a path.
/// </remarks>
internal enum VendorStatus
{
    /// <summary>They have applied. Nothing has been checked.</summary>
    Applied = 0,

    /// <summary>Somebody is checking their documents.</summary>
    UnderReview = 1,

    /// <summary>The checks passed. They still cannot trade — activation is a separate decision.</summary>
    Approved = 2,

    /// <summary>Trading. The only state in which their listings may be sold.</summary>
    Active = 3,

    /// <summary>Stopped from making new sales. Their open orders are unaffected.</summary>
    Suspended = 4,

    /// <summary>Gone, for good. Terminal.</summary>
    Offboarded = 5,
}

/// <summary>
/// The legal form the seller trades as. Decides which documents KYC has to see
/// (docs/03-database-design.md §4.3).
/// </summary>
internal enum VendorBusinessType
{
    /// <summary>A person trading under their own name. PAN is theirs, not a company's.</summary>
    Individual = 0,

    /// <summary>One person, trading under a business name.</summary>
    SoleProprietorship = 1,

    /// <summary>A registered partnership firm.</summary>
    Partnership = 2,

    /// <summary>A limited liability partnership.</summary>
    LimitedLiabilityPartnership = 3,

    /// <summary>A private limited company.</summary>
    PrivateLimited = 4,

    /// <summary>A public limited company.</summary>
    PublicLimited = 5,

    /// <summary>A Hindu Undivided Family.</summary>
    HinduUndividedFamily = 6,

    /// <summary>A trust or a society.</summary>
    Trust = 7,
}

/// <summary>
/// What a seller promises about returns. Stored as JSON because it is the seller's own policy,
/// read whole and never queried into (docs/03-database-design.md §1).
/// </summary>
/// <remarks>
/// The platform's return window is a store setting and is the ceiling; a seller may be stricter
/// within it but not laxer, and Returns enforces that at Step 17. This type only records what they
/// said.
/// </remarks>
internal sealed class ReturnPolicy
{
    /// <summary>Whether the seller accepts returns at all.</summary>
    public bool AcceptsReturns { get; set; } = true;

    /// <summary>How many days after delivery a return may be raised.</summary>
    public int WindowDays { get; set; }

    /// <summary>Whether an exchange is offered as well as a refund.</summary>
    public bool AcceptsExchanges { get; set; }

    /// <summary>Who pays the return shipping when the reason is not a defect.</summary>
    public bool CustomerPaysReturnShipping { get; set; }

    /// <summary>The seller's own wording, shown on the product page.</summary>
    public string? Notes { get; set; }
}

/// <summary>
/// The seller's registered address, as it appears on their GST registration.
/// </summary>
/// <remarks>
/// JSON rather than columns, and that is a deliberate exception to "never jsonb for relational
/// data": nothing joins to it, nothing filters on it, and the one field that <em>is</em> queried —
/// the state, which decides the place of supply — is derived from the GSTIN instead. The pickup
/// locations, which shipping really does query, are a table.
/// </remarks>
internal sealed class RegisteredAddress
{
    /// <summary>House, flat or building.</summary>
    public string Line1 { get; set; } = string.Empty;

    /// <summary>Street, area or locality.</summary>
    public string? Line2 { get; set; }

    /// <summary>City or town.</summary>
    public string City { get; set; } = string.Empty;

    /// <summary>The <c>platform.states</c> row for the state or union territory.</summary>
    public Guid StateId { get; set; }

    /// <summary>Six-digit PIN code.</summary>
    public string Pincode { get; set; } = string.Empty;
}

/// <summary>
/// A seller (docs/03-database-design.md §4.3). The aggregate root of everything about them:
/// their staff, their documents, their bank accounts, their pickup points and where they deliver.
/// </summary>
/// <remarks>
/// <para>
/// The identity of this row is the <c>vendor_id</c> that eight other schemas carry, so it is
/// never deleted — a seller who leaves becomes <see cref="VendorStatus.Offboarded"/>, because
/// Settlements may still owe them money and Orders certainly still holds their invoices.
/// </para>
/// <para>
/// Nothing here reads its children. Whether a seller <em>may</em> be activated depends on their
/// KYC and their bank account, which are separate aggregates; that rule lives in the application
/// layer, where both can be loaded, and this type only knows which transitions exist.
/// </para>
/// </remarks>
internal sealed class Vendor : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private Vendor(Guid id, string code, string legalName, string displayName, string slug)
        : base(id)
    {
        Code = Guard.NotNullOrWhiteSpace(code);
        LegalName = Guard.NotNullOrWhiteSpace(legalName);
        DisplayName = Guard.NotNullOrWhiteSpace(displayName);
        Slug = Guard.NotNullOrWhiteSpace(slug);
        Status = VendorStatus.Applied;
        ReturnPolicy = new ReturnPolicy();
        RegisteredAddress = new RegisteredAddress();
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Vendor()
    {
        Code = string.Empty;
        LegalName = string.Empty;
        DisplayName = string.Empty;
        Slug = string.Empty;
        ReturnPolicy = new ReturnPolicy();
        RegisteredAddress = new RegisteredAddress();
    }

    /// <summary>The short, stable code quoted in support calls and printed on the invoice.</summary>
    public string Code { get; private set; }

    /// <summary>The name they are registered under. This is the name on the tax invoice.</summary>
    public string LegalName { get; private set; }

    /// <summary>The name a shopper sees. Often a brand rather than a company.</summary>
    public string DisplayName { get; private set; }

    /// <summary>The storefront path segment. Unique per tenant.</summary>
    public string Slug { get; private set; }

    /// <summary>Where they are in the onboarding life cycle.</summary>
    public VendorStatus Status { get; private set; }

    /// <summary>Why they were last suspended or offboarded. Shown to them.</summary>
    public string? StatusReason { get; private set; }

    /// <summary>Their GST registration number, or null for a seller below the threshold.</summary>
    public string? Gstin { get; private set; }

    /// <summary>Their PAN. Required before approval, whatever the legal form.</summary>
    public string? Pan { get; private set; }

    /// <summary>The legal form they trade as.</summary>
    public VendorBusinessType BusinessType { get; private set; }

    /// <summary>Their registered address, as JSON.</summary>
    public RegisteredAddress RegisteredAddress { get; private set; }

    /// <summary>Where a customer's escalation reaches them.</summary>
    public string? SupportEmail { get; private set; }

    /// <summary>Their support number, in E.164.</summary>
    public string? SupportPhone { get; private set; }

    /// <summary>The commission plan applied to their sales, or null until one is assigned.</summary>
    public Guid? CommissionPlanId { get; private set; }

    /// <summary>How many hours they have to hand a parcel to a courier after an order is confirmed.</summary>
    public int DispatchSlaHours { get; private set; } = DefaultDispatchSlaHours;

    /// <summary>What they promise about returns, as JSON.</summary>
    public ReturnPolicy ReturnPolicy { get; private set; }

    /// <summary>Whether they deliver everywhere, in which case no region rows are needed.</summary>
    public bool ServesAllIndia { get; private set; } = true;

    /// <summary>Their storefront blurb.</summary>
    public string? About { get; private set; }

    /// <summary>Their logo, as a <c>media.files</c> id. A soft reference; never a foreign key.</summary>
    public Guid? LogoFileId { get; private set; }

    /// <summary>Their storefront banner, as a <c>media.files</c> id.</summary>
    public Guid? BannerFileId { get; private set; }

    /// <summary>Their average review score, written by Reporting. Null until they have reviews.</summary>
    public decimal? Rating { get; private set; }

    /// <summary>When they first became <see cref="VendorStatus.Active"/>. Never reset by a suspension.</summary>
    public DateTimeOffset? OnboardedAt { get; private set; }

    /// <summary>
    /// Their Razorpay Route linked-account id, once one has been created for them. Null until the
    /// payout provisioning at Step 18 has run.
    /// </summary>
    public string? GatewayAccountId { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; private set; }

    /// <summary>Whether this seller may currently sell anything.</summary>
    public bool IsTrading => Status == VendorStatus.Active;

    /// <summary>The dispatch SLA a seller starts with: one working day.</summary>
    public const int DefaultDispatchSlaHours = 24;

    /// <summary>The longest dispatch SLA the platform will accept.</summary>
    public const int MaxDispatchSlaHours = 168;

    /// <summary>Registers an application to sell. The seller starts in <see cref="VendorStatus.Applied"/>.</summary>
    /// <param name="code">The short code, already normalised and known to be free.</param>
    /// <param name="legalName">The registered name.</param>
    /// <param name="displayName">The name shoppers see.</param>
    /// <param name="slug">The storefront path segment, already normalised and known to be free.</param>
    /// <param name="businessType">The legal form.</param>
    public static Vendor Apply(
        string code,
        string legalName,
        string displayName,
        string slug,
        VendorBusinessType businessType)
    {
        var vendor = new Vendor(UuidV7.New(), code, legalName, displayName, slug)
        {
            BusinessType = businessType,
        };

        return vendor;
    }

    /// <summary>
    /// Whether the life cycle allows one status to follow another
    /// (docs/02-domain-model.md §5).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Declared as a table rather than as a chain of <c>if</c>s so that the whole machine can be
    /// read — and tested — in one place. Two rules are worth stating out loud:
    /// </para>
    /// <para>
    /// <see cref="VendorStatus.UnderReview"/> may go back to <see cref="VendorStatus.Applied"/>,
    /// because "we need a clearer scan of your cheque" is the single most common outcome of a real
    /// review and must not require the seller to reapply. And every non-terminal state may go
    /// straight to <see cref="VendorStatus.Offboarded"/>, because a rejected application and a
    /// seller who walks away are the same terminal fact.
    /// </para>
    /// </remarks>
    /// <param name="from">The current status.</param>
    /// <param name="to">The status being moved to.</param>
    public static bool IsTransitionAllowed(VendorStatus from, VendorStatus to)
        => (from, to) switch
        {
            (VendorStatus.Applied, VendorStatus.UnderReview) => true,
            (VendorStatus.UnderReview, VendorStatus.Applied) => true,
            (VendorStatus.UnderReview, VendorStatus.Approved) => true,
            (VendorStatus.Approved, VendorStatus.Active) => true,
            (VendorStatus.Active, VendorStatus.Suspended) => true,
            (VendorStatus.Suspended, VendorStatus.Active) => true,
            (_, VendorStatus.Offboarded) => from != VendorStatus.Offboarded,
            _ => false,
        };

    /// <summary>
    /// Moves the seller to a new status, or returns false if the life cycle does not allow it.
    /// </summary>
    /// <remarks>
    /// False rather than an exception: an operator pressing "activate" on a seller somebody else
    /// suspended a second ago is a conflict to report, not a programming error.
    /// </remarks>
    /// <param name="next">The status to move to.</param>
    /// <param name="at">When the transition happened.</param>
    /// <param name="reason">Why, for a suspension or an offboarding.</param>
    public bool TransitionTo(VendorStatus next, DateTimeOffset at, string? reason = null)
    {
        if (!IsTransitionAllowed(Status, next))
        {
            return false;
        }

        Status = next;
        StatusReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        // Set once, on the first activation. A seller who is suspended and reinstated has not
        // joined the marketplace twice, and their commission tier may depend on how long they have
        // been on it.
        if (next == VendorStatus.Active)
        {
            OnboardedAt ??= at;
        }

        return true;
    }

    /// <summary>Records the registration details an approval depends on.</summary>
    /// <param name="legalName">The registered name.</param>
    /// <param name="businessType">The legal form.</param>
    /// <param name="pan">Their PAN, normalised to upper case.</param>
    /// <param name="gstin">Their GSTIN, or null.</param>
    /// <param name="address">Their registered address.</param>
    public void DescribeBusiness(
        string legalName,
        VendorBusinessType businessType,
        string? pan,
        string? gstin,
        RegisteredAddress address)
    {
        LegalName = Guard.NotNullOrWhiteSpace(legalName);
        BusinessType = businessType;
        Pan = Normalize(pan);
        Gstin = Normalize(gstin);
        RegisteredAddress = Guard.NotNull(address);
    }

    /// <summary>Updates the storefront profile — what a shopper sees on the seller's page.</summary>
    /// <param name="displayName">The name shoppers see.</param>
    /// <param name="about">The storefront blurb.</param>
    /// <param name="logoFileId">Their logo, as a media file id.</param>
    /// <param name="bannerFileId">Their banner, as a media file id.</param>
    /// <param name="supportEmail">Where an escalation reaches them.</param>
    /// <param name="supportPhone">Their support number.</param>
    public void UpdateProfile(
        string displayName,
        string? about,
        Guid? logoFileId,
        Guid? bannerFileId,
        string? supportEmail,
        string? supportPhone)
    {
        DisplayName = Guard.NotNullOrWhiteSpace(displayName);
        About = about;
        LogoFileId = logoFileId;
        BannerFileId = bannerFileId;
        SupportEmail = supportEmail?.Trim().ToLowerInvariant();
        SupportPhone = supportPhone;
    }

    /// <summary>Updates the operational settings fulfilment reads.</summary>
    /// <param name="dispatchSlaHours">Hours to hand a parcel to a courier.</param>
    /// <param name="returnPolicy">What they promise about returns.</param>
    /// <param name="servesAllIndia">Whether they deliver everywhere.</param>
    public void UpdateOperations(int dispatchSlaHours, ReturnPolicy returnPolicy, bool servesAllIndia)
    {
        DispatchSlaHours = Guard.Positive(dispatchSlaHours);
        ReturnPolicy = Guard.NotNull(returnPolicy);
        ServesAllIndia = servesAllIndia;
    }

    /// <summary>Assigns the commission plan that applies to this seller's sales.</summary>
    /// <param name="planId">The plan, or null to remove the assignment.</param>
    public void AssignCommissionPlan(Guid? planId) => CommissionPlanId = planId;

    /// <summary>Records the payout account the gateway created for this seller (Step 18).</summary>
    /// <param name="gatewayAccountId">The linked-account id.</param>
    public void LinkGatewayAccount(string gatewayAccountId)
        => GatewayAccountId = Guard.NotNullOrWhiteSpace(gatewayAccountId);

    /// <summary>Records the average review score computed elsewhere.</summary>
    /// <param name="rating">The score, or null when they have no reviews.</param>
    public void RecordRating(decimal? rating) => Rating = rating;

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}
