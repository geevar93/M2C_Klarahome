using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Vendors.Domain;

/// <summary>Whether the account has been proved to belong to the seller.</summary>
internal enum BankVerificationStatus
{
    /// <summary>Recorded, nothing has checked it.</summary>
    Unverified = 0,

    /// <summary>A penny drop or a cancelled cheque proved it.</summary>
    Verified = 1,

    /// <summary>A check was made and it failed. No payout may be sent here.</summary>
    Failed = 2,
}

/// <summary>
/// Where a seller's payouts go (docs/03-database-design.md §4.3).
/// </summary>
/// <remarks>
/// <para>
/// The account number is encrypted at the column, because it is "Sensitive" under
/// docs/07-security-compliance.md §5 and cannot be hashed — money has to be sent to it. The last
/// four digits are stored separately, in the clear: every screen that shows an account shows those
/// four digits, and decrypting a row to render a list would put the plaintext of every seller's
/// account through the application for no reason.
/// </para>
/// <para>
/// Exactly one account per seller is primary, and only a primary, verified account may be paid.
/// Settlements enforces the second half of that at Step 18; this type owns the first.
/// </para>
/// </remarks>
internal sealed class VendorBankAccount : AggregateRoot<Guid>, ITenantScoped, IVendorScoped, IAuditable
{
    private VendorBankAccount(
        Guid id,
        Guid vendorId,
        string accountName,
        string accountNumberEncrypted,
        string accountNumberLast4,
        string ifsc)
        : base(id)
    {
        VendorId = vendorId;
        AccountName = Guard.NotNullOrWhiteSpace(accountName);
        AccountNumberEncrypted = Guard.NotNullOrWhiteSpace(accountNumberEncrypted);
        AccountNumberLast4 = Guard.NotNullOrWhiteSpace(accountNumberLast4);
        Ifsc = Guard.NotNullOrWhiteSpace(ifsc);
        VerificationStatus = BankVerificationStatus.Unverified;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private VendorBankAccount()
    {
        AccountName = string.Empty;
        AccountNumberEncrypted = string.Empty;
        AccountNumberLast4 = string.Empty;
        Ifsc = string.Empty;
    }

    /// <inheritdoc />
    public Guid? VendorId { get; private set; }

    /// <summary>The name the account is held in. Must match the seller's legal name.</summary>
    public string AccountName { get; private set; }

    /// <summary>The account number, AES-256-GCM encrypted under the deployment's key ring.</summary>
    public string AccountNumberEncrypted { get; private set; }

    /// <summary>The last four digits, in the clear, for every screen that identifies the account.</summary>
    public string AccountNumberLast4 { get; private set; }

    /// <summary>The IFSC of the branch. Eleven characters, fifth is always zero.</summary>
    public string Ifsc { get; private set; }

    /// <summary>The bank's name, as the seller gave it. Display only; the IFSC is the authority.</summary>
    public string? BankName { get; private set; }

    /// <summary>The branch name, as the seller gave it.</summary>
    public string? BranchName { get; private set; }

    /// <summary>Whether this is the account payouts are sent to.</summary>
    public bool IsPrimary { get; private set; }

    /// <summary>Whether the account has been proved to belong to the seller.</summary>
    public BankVerificationStatus VerificationStatus { get; private set; }

    /// <summary>When it was last checked.</summary>
    public DateTimeOffset? VerifiedAt { get; private set; }

    /// <summary>Why a check failed.</summary>
    public string? VerificationNote { get; private set; }

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

    /// <summary>Records an account for a seller.</summary>
    /// <param name="vendorId">The seller.</param>
    /// <param name="accountName">The name the account is held in.</param>
    /// <param name="accountNumberEncrypted">The already-encrypted account number.</param>
    /// <param name="accountNumberLast4">Its last four digits.</param>
    /// <param name="ifsc">The branch IFSC, normalised to upper case.</param>
    public static VendorBankAccount Add(
        Guid vendorId,
        string accountName,
        string accountNumberEncrypted,
        string accountNumberLast4,
        string ifsc)
        => new(UuidV7.New(), vendorId, accountName, accountNumberEncrypted, accountNumberLast4, ifsc);

    /// <summary>Records the bank and branch the seller named.</summary>
    /// <param name="bankName">The bank.</param>
    /// <param name="branchName">The branch.</param>
    public void Describe(string? bankName, string? branchName)
    {
        BankName = bankName;
        BranchName = branchName;
    }

    /// <summary>Makes this the account payouts are sent to, or takes that away.</summary>
    /// <param name="isPrimary">Whether it is now primary.</param>
    public void SetPrimary(bool isPrimary) => IsPrimary = isPrimary;

    /// <summary>Records the outcome of a check on this account.</summary>
    /// <param name="status">What the check concluded.</param>
    /// <param name="at">When it concluded it.</param>
    /// <param name="note">Why, for a failure.</param>
    public void RecordVerification(BankVerificationStatus status, DateTimeOffset at, string? note = null)
    {
        VerificationStatus = status;
        VerifiedAt = at;
        VerificationNote = note;
    }

    /// <summary>The last four digits of an account number, for the column kept in the clear.</summary>
    /// <param name="accountNumber">The full account number.</param>
    internal static string Last4(string accountNumber)
    {
        var digits = Guard.NotNullOrWhiteSpace(accountNumber).Trim();
        return digits.Length <= 4 ? digits : digits[^4..];
    }
}
