using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Vendors.Infrastructure.Payments;

/// <summary>What the gateway needs to know to open a payout account for a seller.</summary>
/// <param name="VendorId">The seller.</param>
/// <param name="LegalName">The registered name. Must match the PAN.</param>
/// <param name="DisplayName">The trading name.</param>
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
/// The seam exists now and the implementation arrives at Step 18, which is where payouts are built
/// and where the Razorpay credentials will exist. Declaring it here rather than there is what lets
/// activation call it: a seller becomes payable at the moment they are activated, and wiring that
/// call in nine steps' time would mean revisiting the activation path rather than replacing one DI
/// registration.
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
/// the gap stops being visible. This one records the request at Information level, so an operator
/// reading the log after Step 18 lands can see which sellers still need an account.
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
                  + "({VendorLegalName}). They can trade but cannot yet be paid; Step 18 provisions them.")]
    private static partial void PayoutAccountNotProvisioned(ILogger logger, Guid vendorId, string vendorLegalName);
}
