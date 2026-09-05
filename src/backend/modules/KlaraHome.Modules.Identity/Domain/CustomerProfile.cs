using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Identity.Domain;

/// <summary>
/// The shopper's own details, kept apart from <see cref="User"/> because they answer a different
/// question: <see cref="User"/> is who may sign in, this is who they are
/// (docs/03-database-design.md §4.2).
/// </summary>
/// <remarks>
/// Marketing consent is a timestamp rather than a flag, because DPDP §6 requires consent to be
/// recorded when it was given and withdrawn as easily as granted. A boolean cannot answer "when
/// did they agree", which is the question a consent audit asks.
/// </remarks>
internal sealed class CustomerProfile : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private CustomerProfile(Guid id, Guid userId, string referralCode)
        : base(id)
    {
        UserId = userId;
        ReferralCode = Guard.NotNullOrWhiteSpace(referralCode);
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private CustomerProfile() => ReferralCode = string.Empty;

    /// <summary>The account these details belong to.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Given name. Personal data: masked in logs and admin lists.</summary>
    public string? FirstName { get; private set; }

    /// <summary>Family name.</summary>
    public string? LastName { get; private set; }

    /// <summary>Date of birth, where the customer chose to give one.</summary>
    public DateOnly? DateOfBirth { get; private set; }

    /// <summary>Self-declared gender, or null.</summary>
    public string? Gender { get; private set; }

    /// <summary>
    /// The customer's own GSTIN, for a B2B invoice. Set here it becomes the default; an address
    /// may still carry its own for a shipment billed to a different registration.
    /// </summary>
    public string? Gstin { get; private set; }

    /// <summary>When marketing consent was given. Null means it was never given, or was withdrawn.</summary>
    public DateTimeOffset? MarketingConsentAt { get; private set; }

    /// <summary>The code this customer shares to refer others. Unique within the tenant.</summary>
    public string ReferralCode { get; private set; }

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

    /// <summary>Creates the profile that accompanies a new shopper account.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="referralCode">The generated referral code.</param>
    public static CustomerProfile For(Guid userId, string referralCode)
        => new(UuidV7.New(), userId, referralCode);

    /// <summary>Updates the details the customer edits on their account page.</summary>
    /// <param name="firstName">Given name.</param>
    /// <param name="lastName">Family name.</param>
    /// <param name="dateOfBirth">Date of birth.</param>
    /// <param name="gender">Self-declared gender.</param>
    /// <param name="gstin">Default GSTIN for B2B invoices.</param>
    public void Update(
        string? firstName,
        string? lastName,
        DateOnly? dateOfBirth,
        string? gender,
        string? gstin)
    {
        FirstName = firstName;
        LastName = lastName;
        DateOfBirth = dateOfBirth;
        Gender = gender;
        Gstin = gstin;
    }

    /// <summary>Records marketing consent, or withdraws it.</summary>
    /// <param name="granted">Whether consent is being given.</param>
    /// <param name="at">When the decision was made.</param>
    public void SetMarketingConsent(bool granted, DateTimeOffset at)
        => MarketingConsentAt = granted ? at : null;
}

/// <summary>
/// An Indian postal address (docs/03-database-design.md §4.2), belonging to one customer.
/// </summary>
/// <remarks>
/// <para>
/// The state is a foreign key in spirit only — it is a plain <c>state_id</c> holding a
/// <c>platform.states</c> row's id, because no foreign key may cross a schema boundary
/// (docs/01-architecture.md §2.1). The Platform module owns the list; this module validates
/// against it at the application layer.
/// </para>
/// <para>
/// The state matters far beyond delivery: place of supply decides whether a sale is taxed as
/// CGST + SGST or as IGST, so a wrong state here is a wrong tax on the invoice.
/// </para>
/// </remarks>
internal sealed class Address : AggregateRoot<Guid>, ITenantScoped, IAuditable, ISoftDeletable
{
    private Address(Guid id, Guid userId, AddressType type)
        : base(id)
    {
        UserId = userId;
        Type = type;
        RecipientName = string.Empty;
        Mobile = string.Empty;
        Line1 = string.Empty;
        City = string.Empty;
        Pincode = string.Empty;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Address()
    {
        RecipientName = string.Empty;
        Mobile = string.Empty;
        Line1 = string.Empty;
        City = string.Empty;
        Pincode = string.Empty;
    }

    /// <summary>The customer this address belongs to.</summary>
    public Guid UserId { get; private set; }

    /// <summary>What the customer calls it: Home, Office, Mum's.</summary>
    public string? Label { get; private set; }

    /// <summary>Who the courier asks for at the door.</summary>
    public string RecipientName { get; private set; }

    /// <summary>The delivery contact number in E.164, which need not be the account's.</summary>
    public string Mobile { get; private set; }

    /// <summary>House or flat number and building.</summary>
    public string Line1 { get; private set; }

    /// <summary>Street, area or locality.</summary>
    public string? Line2 { get; private set; }

    /// <summary>A nearby landmark. Not decoration in India — couriers navigate by it.</summary>
    public string? Landmark { get; private set; }

    /// <summary>City or town.</summary>
    public string City { get; private set; }

    /// <summary>The <c>platform.states</c> row for the state or union territory.</summary>
    public Guid StateId { get; private set; }

    /// <summary>Six-digit PIN code.</summary>
    public string Pincode { get; private set; }

    /// <summary>A GSTIN specific to this address, for a B2B invoice billed to it.</summary>
    public string? Gstin { get; private set; }

    /// <summary>Whether it is a home or a business address; couriers price and time them differently.</summary>
    public AddressType Type { get; private set; }

    /// <summary>Whether this is the address checkout ships to by default.</summary>
    public bool IsDefaultShipping { get; private set; }

    /// <summary>Whether this is the address invoices are billed to by default.</summary>
    public bool IsDefaultBilling { get; private set; }

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

    /// <inheritdoc />
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <inheritdoc />
    public Guid? DeletedBy { get; private set; }

    /// <summary>Creates an address for a customer.</summary>
    /// <param name="userId">The owning customer.</param>
    /// <param name="type">Home or business.</param>
    public static Address For(Guid userId, AddressType type) => new(UuidV7.New(), userId, type);

    /// <summary>Sets every field the customer supplies. Addresses are edited whole, never patched.</summary>
    /// <param name="label">The customer's own name for it.</param>
    /// <param name="recipientName">Who to ask for.</param>
    /// <param name="mobile">The delivery contact number.</param>
    /// <param name="line1">House or flat and building.</param>
    /// <param name="line2">Street or locality.</param>
    /// <param name="landmark">A nearby landmark.</param>
    /// <param name="city">City or town.</param>
    /// <param name="stateId">The state or union territory.</param>
    /// <param name="pincode">Six-digit PIN code.</param>
    /// <param name="gstin">A GSTIN for this address, or null.</param>
    /// <param name="type">Home or business.</param>
    public void SetDetails(
        string? label,
        string recipientName,
        string mobile,
        string line1,
        string? line2,
        string? landmark,
        string city,
        Guid stateId,
        string pincode,
        string? gstin,
        AddressType type)
    {
        Label = label;
        RecipientName = Guard.NotNullOrWhiteSpace(recipientName);
        Mobile = Guard.NotNullOrWhiteSpace(mobile);
        Line1 = Guard.NotNullOrWhiteSpace(line1);
        Line2 = line2;
        Landmark = landmark;
        City = Guard.NotNullOrWhiteSpace(city);
        StateId = stateId;
        Pincode = Guard.NotNullOrWhiteSpace(pincode);
        Gstin = gstin;
        Type = type;
    }

    /// <summary>Makes this the default shipping or billing address, or clears the flag.</summary>
    /// <param name="shipping">Whether it is the default shipping address.</param>
    /// <param name="billing">Whether it is the default billing address.</param>
    public void SetDefaults(bool shipping, bool billing)
    {
        IsDefaultShipping = shipping;
        IsDefaultBilling = billing;
    }

    /// <summary>Retires the address. Orders already placed still point at it.</summary>
    /// <param name="at">When it was removed.</param>
    public void Delete(DateTimeOffset at)
    {
        DeletedAt = at;
        IsDefaultShipping = false;
        IsDefaultBilling = false;
    }
}

/// <summary>Whether an address is residential or commercial.</summary>
internal enum AddressType
{
    /// <summary>A residence.</summary>
    Home = 0,

    /// <summary>A place of business.</summary>
    Office = 1,
}
