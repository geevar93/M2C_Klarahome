using KlaraHome.Modules.Vendors.Domain;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Vendors.Infrastructure.Payments;

/// <summary>What the gateway needs to know to open a payout account for a seller.</summary>
/// <param name="VendorId">The seller.</param>
/// <param name="LegalName">The registered name. Must match the PAN.</param>
/// <param name="DisplayName">The trading name.</param>
/// <param name="BusinessType">The legal form they trade as, which the gateway files them under.</param>
/// <param name="Email">Where the gateway sends account correspondence.</param>
/// <param name="Phone">The seller's contact number.</param>
/// <param name="Pan">Their PAN.</param>
/// <param name="Gstin">Their GSTIN, when they have one.</param>
/// <param name="AccountNumber">The bank account payouts are sent to, in the clear.</param>
/// <param name="Ifsc">That account's branch IFSC.</param>
/// <param name="AccountHolderName">The name that account is held in.</param>
internal sealed record PayoutAccountRequest(
    Guid VendorId,
    string LegalName,
    string DisplayName,
    VendorBusinessType BusinessType,
    string? Email,
    string? Phone,
    string? Pan,
    string? Gstin,
    string AccountNumber,
    string Ifsc,
    string AccountHolderName);

/// <summary>
/// Creates the gateway-side account a seller's payouts are sent through — Razorpay Route's linked
/// account (docs/08-integrations.md §2).
/// </summary>
/// <remarks>
/// <para>
/// Declared at Step 9 so activation could call it, and filled in at Step 28B by
/// <see cref="RazorpayLinkedAccounts"/> — inside this module rather than in Settlements, because the
/// data it needs is this module's. Replacing the no-op was exactly the one DI registration this
/// seam was shaped to be.
/// </para>
/// <para>
/// Provisioning must never fail an activation. A gateway outage is not a reason to refuse a seller
/// permission to trade — the account can be created on the next attempt, and the seller simply is
/// not payable until it is, which is a condition Settlements already has to handle.
/// </para>
/// </remarks>
internal interface IVendorPayoutAccounts
{
    /// <summary>Whether a real provider is configured. False means nothing will be created.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Creates the seller's payout account and returns the gateway's id for it, or null when
    /// nothing was created.
    /// </summary>
    /// <param name="request">What the gateway needs to know.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string?> CreateAsync(PayoutAccountRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The honest implementation for a deployment with no payment gateway: it creates nothing and says
/// so.
/// </summary>
/// <remarks>
/// The same shape as the Media module's no-op virus scanner (ADR-016), and for the same reason: a
/// seam whose default implementation pretends to have worked is worse than no seam at all, because
/// the gap stops being visible. It is still registered, and still what a deployment with no gateway
/// credentials or with Route switched off gets; it records each request at Information level so an
/// operator can see which sellers are trading but not yet payable.
/// </remarks>
/// <param name="logger">Reports each seller that would have been provisioned.</param>
internal sealed partial class UnprovisionedPayoutAccounts(ILogger<UnprovisionedPayoutAccounts> logger)
    : IVendorPayoutAccounts
{
    /// <inheritdoc />
    public bool IsConfigured => false;

    /// <inheritdoc />
    public Task<string?> CreateAsync(PayoutAccountRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        PayoutAccountNotProvisioned(logger, request.VendorId, request.LegalName);

        return Task.FromResult<string?>(null);
    }

    [LoggerMessage(EventId = 1900, Level = LogLevel.Information,
        Message = "No payout provider is configured, so no linked account was created for vendor {VendorId} "
                  + "({VendorLegalName}). They can trade but cannot yet be paid.")]
    private static partial void PayoutAccountNotProvisioned(ILogger logger, Guid vendorId, string vendorLegalName);
}
