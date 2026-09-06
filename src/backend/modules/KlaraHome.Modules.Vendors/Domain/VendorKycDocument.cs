using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Vendors.Domain;

/// <summary>
/// The kinds of document this platform collects before a seller may trade
/// (docs/03-database-design.md §4.3).
/// </summary>
internal enum KycDocumentType
{
    /// <summary>Permanent Account Number card. Required of every seller, whatever their legal form.</summary>
    Pan = 0,

    /// <summary>GST registration certificate. Required of a seller above the registration threshold.</summary>
    Gstin = 1,

    /// <summary>A cancelled cheque, which proves the bank account belongs to the seller.</summary>
    CancelledCheque = 2,

    /// <summary>Proof that the business operates from the address it registered.</summary>
    AddressProof = 3,

    /// <summary>Photo identity for the signatory — Aadhaar, passport, driving licence.</summary>
    IdentityProof = 4,

    /// <summary>Certificate of incorporation, partnership deed, or the equivalent for the legal form.</summary>
    IncorporationCertificate = 5,
}

/// <summary>Whether a document has been checked, and what the checker concluded.</summary>
internal enum KycVerificationStatus
{
    /// <summary>Uploaded, nobody has looked at it.</summary>
    Pending = 0,

    /// <summary>Checked and accepted.</summary>
    Verified = 1,

    /// <summary>Checked and refused. The reason is recorded and shown to the seller.</summary>
    Rejected = 2,
}

/// <summary>
/// One document a seller submitted, and what became of it (docs/03-database-design.md §4.3).
/// </summary>
/// <remarks>
/// <para>
/// The scan itself is a private <c>media.files</c> object and is never public: a PAN card served
/// from a public bucket is a data breach with a URL. Only the file id is stored here, and reading
/// it back mints a short-lived signed link.
/// </para>
/// <para>
/// The number is stored <em>masked</em> and only masked. Verification compares what the checker
/// read off the scan against what the seller typed, and after that nothing this platform does
/// needs the full number — so keeping it would be collecting a government identifier for no
/// purpose, which is exactly what DPDP §6 purpose limitation forbids. The seller's PAN and GSTIN,
/// which invoicing genuinely needs, live on <see cref="Vendor"/> instead.
/// </para>
/// </remarks>
internal sealed class VendorKycDocument : AggregateRoot<Guid>, ITenantScoped, IVendorScoped, IAuditable
{
    private VendorKycDocument(Guid id, Guid vendorId, KycDocumentType documentType, Guid fileId)
        : base(id)
    {
        VendorId = vendorId;
        DocumentType = documentType;
        FileId = fileId;
        Status = KycVerificationStatus.Pending;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private VendorKycDocument()
    {
    }

    /// <inheritdoc />
    public Guid? VendorId { get; private set; }

    /// <summary>What the document is.</summary>
    public KycDocumentType DocumentType { get; private set; }

    /// <summary>The scan, as a private <c>media.files</c> id.</summary>
    public Guid FileId { get; private set; }

    /// <summary>The identifier on the document, masked to its last four characters.</summary>
    public string? NumberMasked { get; private set; }

    /// <summary>Whether it has been checked, and what was concluded.</summary>
    public KycVerificationStatus Status { get; private set; }

    /// <summary>The staff user who checked it.</summary>
    public Guid? VerifiedBy { get; private set; }

    /// <summary>When it was checked.</summary>
    public DateTimeOffset? VerifiedAt { get; private set; }

    /// <summary>Why it was refused. Shown to the seller so they know what to send again.</summary>
    public string? RejectionReason { get; private set; }

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

    /// <summary>Records a submitted document, awaiting a check.</summary>
    /// <param name="vendorId">The seller.</param>
    /// <param name="documentType">What the document is.</param>
    /// <param name="fileId">The private media file holding the scan.</param>
    /// <param name="number">The identifier on the document; only its mask is kept.</param>
    public static VendorKycDocument Submit(
        Guid vendorId,
        KycDocumentType documentType,
        Guid fileId,
        string? number)
        => new(UuidV7.New(), vendorId, documentType, Guard.NotEmpty(fileId))
        {
            NumberMasked = Mask(number),
        };

    /// <summary>Replaces the scan, and puts the document back in the queue to be checked again.</summary>
    /// <param name="fileId">The new scan.</param>
    /// <param name="number">The identifier on it.</param>
    public void Resubmit(Guid fileId, string? number)
    {
        FileId = Guard.NotEmpty(fileId);
        NumberMasked = Mask(number);
        Status = KycVerificationStatus.Pending;
        VerifiedBy = null;
        VerifiedAt = null;
        RejectionReason = null;
    }

    /// <summary>Accepts the document.</summary>
    /// <param name="verifiedBy">The staff user who checked it.</param>
    /// <param name="at">When they checked it.</param>
    public void Verify(Guid? verifiedBy, DateTimeOffset at)
    {
        Status = KycVerificationStatus.Verified;
        VerifiedBy = verifiedBy;
        VerifiedAt = at;
        RejectionReason = null;
    }

    /// <summary>Refuses the document.</summary>
    /// <param name="reason">Why, in the checker's words. Shown to the seller.</param>
    /// <param name="verifiedBy">The staff user who checked it.</param>
    /// <param name="at">When they checked it.</param>
    public void Reject(string reason, Guid? verifiedBy, DateTimeOffset at)
    {
        Status = KycVerificationStatus.Rejected;
        RejectionReason = Guard.NotNullOrWhiteSpace(reason);
        VerifiedBy = verifiedBy;
        VerifiedAt = at;
    }

    /// <summary>
    /// Masks an identifier to its last four characters — <c>ABCDE1234F</c> becomes
    /// <c>••••••234F</c>.
    /// </summary>
    /// <remarks>
    /// Short values are masked entirely rather than partially. Revealing four of six characters is
    /// not a mask, it is a hint.
    /// </remarks>
    /// <param name="number">The identifier, or null.</param>
    internal static string? Mask(string? number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return null;
        }

        var trimmed = number.Trim().ToUpperInvariant();

        return trimmed.Length <= 8
            ? new string('•', trimmed.Length)
            : string.Concat(new string('•', trimmed.Length - 4), trimmed[^4..]);
    }
}
