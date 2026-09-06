using KlaraHome.Infrastructure.Security;
using KlaraHome.Modules.Vendors.Domain;
using KlaraHome.Modules.Vendors.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Vendors.Infrastructure.Payments;

/// <summary>
/// Creates the gateway payout account for a seller who has just been activated, and records the id
/// it comes back with.
/// </summary>
/// <remarks>
/// <para>
/// Called after the activation has been committed, and it cannot undo one. A gateway that is down
/// is not a reason to refuse a seller permission to trade: the account can be created on the next
/// attempt, and until then the seller is exactly what Settlements already knows how to handle — one
/// with no <c>gateway_account_id</c>, whose payouts wait.
/// </para>
/// <para>
/// The account number is decrypted here and nowhere else, held for the length of one call, and
/// never logged. That is the whole reason the plaintext exists in this process at all.
/// </para>
/// </remarks>
/// <param name="context">The Vendors data context.</param>
/// <param name="accounts">The gateway seam. A no-op until Step 18.</param>
/// <param name="protector">Decrypts the account number for the one call that needs it.</param>
/// <param name="logger">Reports a failure that must not become an exception.</param>
internal sealed partial class VendorPayoutProvisioner(
    VendorsDbContext context,
    IVendorPayoutAccounts accounts,
    IFieldProtector protector,
    ILogger<VendorPayoutProvisioner> logger)
{
    /// <summary>
    /// Creates the seller's payout account, if a provider is configured and they have an account to
    /// be paid into. Returns whether one was created.
    /// </summary>
    /// <param name="vendor">The seller, already activated and committed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> TryProvisionAsync(Vendor vendor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vendor);

        if (!accounts.IsConfigured)
        {
            return false;
        }

        var bank = await context.BankAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                account => account.VendorId == vendor.Id
                           && account.IsPrimary
                           && account.VerificationStatus == BankVerificationStatus.Verified,
                cancellationToken)
            .ConfigureAwait(false);

        if (bank is null)
        {
            return false;
        }

        var accountNumber = protector.Unprotect(bank.AccountNumberEncrypted);

        if (string.IsNullOrEmpty(accountNumber))
        {
            // The row cannot be read under any key this deployment holds. That is an operational
            // problem — a key that was rotated away — and the seller must re-enter their account,
            // so it is reported rather than retried.
            PayoutAccountUnreadable(logger, vendor.Id);
            return false;
        }

        try
        {
            var gatewayAccountId = await accounts
                .CreateAsync(
                    new PayoutAccountRequest(
                        vendor.Id,
                        vendor.LegalName,
                        vendor.DisplayName,
                        vendor.SupportEmail,
                        vendor.SupportPhone,
                        vendor.Pan,
                        vendor.Gstin,
                        accountNumber,
                        bank.Ifsc,
                        bank.AccountName),
                    cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(gatewayAccountId))
            {
                return false;
            }

            vendor.LinkGatewayAccount(gatewayAccountId);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Deliberately broad, and deliberately swallowed. Whatever the gateway adapter throws,
            // the seller is already active and the alternative — letting it escape — would turn a
            // committed activation into a 500 the operator would retry, hitting the "already
            // active" conflict on the way back in.
            PayoutAccountFailed(logger, exception, vendor.Id);
            return false;
        }
    }

    [LoggerMessage(EventId = 1901, Level = LogLevel.Error,
        Message = "Vendor {VendorId} was activated but their bank account could not be decrypted, so no payout "
                  + "account was created. The account must be re-entered.")]
    private static partial void PayoutAccountUnreadable(ILogger logger, Guid vendorId);

    [LoggerMessage(EventId = 1902, Level = LogLevel.Error,
        Message = "Vendor {VendorId} was activated but the payout provider did not create their account. They "
                  + "can trade and cannot yet be paid; provisioning is retried when they are next activated.")]
    private static partial void PayoutAccountFailed(ILogger logger, Exception exception, Guid vendorId);
}
