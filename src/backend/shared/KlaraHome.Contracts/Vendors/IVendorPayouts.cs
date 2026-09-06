namespace KlaraHome.Contracts.Vendors;

/// <summary>
/// Where a seller's money is sent, and whether it can be sent at all.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately carries no account number. A payout is executed by naming an account the
/// <em>gateway</em> already holds — a Route linked account or an X fund account — and the plaintext
/// of a bank account has no business crossing a module boundary to make that call. The last four
/// digits and the IFSC are here because a payout statement and a failed-transfer investigation both
/// need to say which account was meant, and neither is a credential.
/// </para>
/// <para>
/// <see cref="IsPayable"/> is the single question the payout run asks. A seller who is suspended, or
/// who has never had an account provisioned at the gateway, still accrues earnings in the ledger —
/// they simply are not included in a batch, which is what keeps "owed" and "paid" two different
/// facts.
/// </para>
/// </remarks>
/// <param name="VendorId">The seller.</param>
/// <param name="Code">Their short code, which appears on the payout statement.</param>
/// <param name="LegalName">The registered name the payment is made to.</param>
/// <param name="DisplayName">The trading name.</param>
/// <param name="Gstin">Their GST registration, which the TCS extract is filed under.</param>
/// <param name="Pan">Their PAN, which decides the TDS rate under section 206AA.</param>
/// <param name="GatewayAccountId">Their account at the payout gateway, once one exists.</param>
/// <param name="HasVerifiedBankAccount">Whether a primary bank account has passed verification.</param>
/// <param name="BankAccountLast4">The last four digits of that account, for the statement.</param>
/// <param name="Ifsc">That account's branch IFSC.</param>
/// <param name="AccountHolderName">The name the account is held in.</param>
/// <param name="IsActive">Whether the seller may currently trade.</param>
public sealed record VendorPayoutProfile(
    Guid VendorId,
    string Code,
    string LegalName,
    string DisplayName,
    string? Gstin,
    string? Pan,
    string? GatewayAccountId,
    bool HasVerifiedBankAccount,
    string? BankAccountLast4,
    string? Ifsc,
    string? AccountHolderName,
    bool IsActive)
{
    /// <summary>Whether money can actually be sent to this seller today.</summary>
    /// <remarks>
    /// Both halves matter. A suspended seller is owed what they have earned and is not paid while
    /// the suspension stands; a seller with no destination cannot be paid however active they are.
    /// </remarks>
    public bool IsPayable
        => IsActive
           && (!string.IsNullOrWhiteSpace(GatewayAccountId) || HasVerifiedBankAccount);

    /// <summary>
    /// Why they cannot be paid, in words an operator can act on, or null when they can.
    /// </summary>
    /// <remarks>
    /// The reason is resolved here rather than by the caller, because there is exactly one place
    /// that knows both conditions and a second opinion would eventually disagree with this one.
    /// </remarks>
    public string? NotPayableReason
        => !IsActive
            ? "The seller is not active."
            : IsPayable
                ? null
                : "The seller has no verified bank account and no payout account at the gateway.";
}

/// <summary>
/// Reads a seller's payout destination from outside the Vendors module
/// (docs/03-database-design.md §4.3, docs/08-integrations.md §1).
/// </summary>
/// <remarks>
/// <para>
/// A second, narrower seam beside <see cref="IVendorDirectory"/> rather than four more fields on
/// <see cref="VendorSummary"/>. The summary is read on the buy-box path for every listing on a page;
/// this is read once per seller per settlement run, and putting a seller's PAN on the record the
/// storefront reads would be putting it somewhere it has no reason to be.
/// </para>
/// <para>
/// Read-only, exactly as <see cref="IVendorDirectory"/> is. Creating the gateway-side account is the
/// Vendors module's own job, done at activation; this contract reports whether it happened.
/// </para>
/// </remarks>
public interface IVendorPayouts
{
    /// <summary>One seller's payout destination, or null when there is no such seller.</summary>
    /// <param name="vendorId">The seller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<VendorPayoutProfile?> FindAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Several sellers' payout destinations at once, keyed by id. Absent ids are simply not in the
    /// result — a settlement cycle for a seller who has since been purged is a case the caller
    /// handles rather than a failure of the run.
    /// </summary>
    /// <param name="vendorIds">The sellers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IReadOnlyDictionary<Guid, VendorPayoutProfile>> FindManyAsync(
        IReadOnlyCollection<Guid> vendorIds,
        CancellationToken cancellationToken = default);
}
