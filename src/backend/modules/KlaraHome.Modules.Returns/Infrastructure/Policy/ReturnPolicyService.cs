using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Modules.Returns.Domain;
using KlaraHome.SharedKernel.Time;

namespace KlaraHome.Modules.Returns.Infrastructure.Policy;

/// <summary>
/// Whether a particular line may be sent back, and until when.
/// </summary>
/// <param name="IsReturnable">Whether it may be sent back at all.</param>
/// <param name="WindowDays">The window that applied, in days.</param>
/// <param name="ClosesAt">When the window closes, or null when the line was never returnable.</param>
/// <param name="Source">
/// Which of the three policies decided it: <c>product</c>, <c>vendor</c> or <c>store</c>. It is
/// carried so a support screen can say <em>why</em> a return was refused, which is the difference
/// between an answer and an argument.
/// </param>
internal readonly record struct ReturnEligibility(
    bool IsReturnable,
    int WindowDays,
    DateTimeOffset? ClosesAt,
    string Source);

/// <summary>
/// Resolves the return policy that applies to a line.
/// </summary>
/// <remarks>
/// <para>
/// Three policies can have an opinion and they are consulted in a fixed order: the <b>product</b>
/// first, because its window was frozen onto the order line at placement and is what the shopper was
/// shown; the <b>seller</b> next, because their promise appears on every product page they list on;
/// and the <b>store</b> last, as the backstop every deployment has. The first one that has an
/// opinion wins — this is deliberately not "the shortest applies", because the shortest is not what
/// the shopper read.
/// </para>
/// <para>
/// A product marked non-returnable is final. It is the one veto in the chain, because that fact was
/// a mandatory listing disclosure under the Consumer Protection (E-Commerce) Rules 2020 and the
/// shopper bought having seen it.
/// </para>
/// <para>
/// The window is measured from delivery, not from placement. A parcel that took three weeks to
/// arrive has not used up the shopper's week to look at it.
/// </para>
/// </remarks>
/// <param name="settings">Supplies the store's own window and the rest of the returns policy.</param>
/// <param name="vendors">Supplies the seller's promise.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class ReturnPolicyService(
    IStoreSettings settings,
    IVendorDirectory vendors,
    IClock clock)
{
    /// <summary>The policies, resolved once for a whole sub-order.</summary>
    /// <param name="Store">The store's own returns policy.</param>
    /// <param name="Commerce">Where the store's own window lives.</param>
    /// <param name="Vendor">The seller's promise, or null when they have none.</param>
    internal readonly record struct ResolvedPolicy(
        ReturnsSettings Store,
        CommerceSettings Commerce,
        VendorReturnPolicy? Vendor);

    /// <summary>Reads every policy that could have an opinion about one seller's part.</summary>
    /// <remarks>
    /// Read once per request rather than once per line. A five-line return would otherwise make
    /// fifteen policy reads to answer one question.
    /// </remarks>
    /// <param name="vendorId">The seller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ResolvedPolicy> ResolveAsync(Guid vendorId, CancellationToken cancellationToken)
    {
        var store = await settings.GetAsync<ReturnsSettings>(cancellationToken).ConfigureAwait(false);
        var commerce = await settings.GetAsync<CommerceSettings>(cancellationToken).ConfigureAwait(false);
        var vendor = await vendors.ReturnPolicyAsync(vendorId, cancellationToken).ConfigureAwait(false);

        return new ResolvedPolicy(store, commerce, vendor);
    }

    /// <summary>
    /// Whether one line may be sent back, and until when.
    /// </summary>
    /// <param name="line">The line, with the window frozen onto it at placement.</param>
    /// <param name="deliveredAt">When the parcel arrived. The window runs from here.</param>
    /// <param name="policy">The policies that apply.</param>
    public ReturnEligibility Eligibility(
        ReturnableLine line,
        DateTimeOffset? deliveredAt,
        ResolvedPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(line);

        // The one veto. It was a mandatory listing disclosure and the shopper bought having seen it.
        if (!line.IsReturnable)
        {
            return new ReturnEligibility(false, 0, null, "product");
        }

        // A seller who takes nothing back takes nothing back, whatever the store's default is.
        if (policy.Vendor is { AcceptsReturns: false })
        {
            return new ReturnEligibility(false, 0, null, "vendor");
        }

        var (days, source) = line.ReturnWindowDays is { } productDays
            ? (productDays, "product")
            : policy.Vendor is { WindowDays: > 0 } vendorPolicy
                ? (vendorPolicy.WindowDays, "vendor")
                : (policy.Commerce.ReturnWindowDays, "store");

        if (days <= 0)
        {
            return new ReturnEligibility(false, 0, null, source);
        }

        // No delivery date means the parcel has not arrived, which the caller has already refused on
        // status. Treating it as an open window here rather than a closed one keeps this method's
        // answer about the policy and leaves "has it arrived" where it belongs.
        if (deliveredAt is not { } delivered)
        {
            return new ReturnEligibility(true, days, null, source);
        }

        var closes = delivered.AddDays(days);

        return new ReturnEligibility(closes >= clock.UtcNow, days, closes, source);
    }

    /// <summary>
    /// Who pays for the reverse pickup, and how much.
    /// </summary>
    /// <remarks>
    /// The reason code decides first, because it is the most specific statement anybody has made
    /// about this particular return — a damaged parcel is collected free whatever the seller's
    /// general terms say. The store's default is the fallback, and the seller's
    /// <c>CustomerPaysReturnShipping</c> is consulted only when neither has an opinion, because it
    /// is a term about ordinary returns rather than about faults.
    /// </remarks>
    /// <param name="reason">The reason given, when it is one this store knows.</param>
    /// <param name="policy">The policies that apply.</param>
    /// <returns>The fee the shopper bears, which is zero unless they are the payer.</returns>
    public static decimal ReturnShippingFee(ReturnReason? reason, ResolvedPolicy policy)
    {
        var payer = reason?.ShippingPayer
                    ?? ParsePayer(policy.Store.DefaultShippingPayer)
                    ?? (policy.Vendor is { CustomerPaysReturnShipping: true }
                        ? ReturnShippingPayer.Customer
                        : ReturnShippingPayer.Platform);

        return payer == ReturnShippingPayer.Customer ? Math.Max(0m, policy.Store.ReturnShippingFee) : 0m;
    }

    /// <summary>
    /// Whether the return is approved without anybody looking at it.
    /// </summary>
    /// <remarks>
    /// Two independent grounds, and either is enough. A reason marked automatic is one the business
    /// has decided to trust; a value at or below the threshold is one where a person deciding costs
    /// more than being wrong. A store wanting neither leaves the threshold at zero and marks no
    /// reason automatic, which is the default.
    /// </remarks>
    /// <param name="reason">The reason given.</param>
    /// <param name="amount">What the return is worth.</param>
    /// <param name="policy">The policies that apply.</param>
    public static bool IsAutoApproved(ReturnReason? reason, decimal amount, ResolvedPolicy policy)
        => reason is { IsAutoApproved: true }
           || (policy.Store.AutoApproveBelow > 0m && amount <= policy.Store.AutoApproveBelow);

    /// <summary>
    /// Where the money goes when the shopper expressed no preference.
    /// </summary>
    /// <param name="requested">What the shopper asked for, when they asked for anything.</param>
    /// <param name="policy">The policies that apply.</param>
    public static ReturnRefundMode RefundMode(ReturnRefundMode? requested, ResolvedPolicy policy)
    {
        if (requested is { } chosen)
        {
            // A store with the wallet off cannot honour a request for store credit, whatever was
            // asked. The endpoint refuses this earlier with a code the shopper can act on; this is
            // the belt to that braces.
            return chosen == ReturnRefundMode.Wallet && !policy.Store.AllowWalletRefunds
                ? ReturnRefundMode.Original
                : chosen;
        }

        return string.Equals(
                   policy.Store.DefaultRefundMode,
                   RefundModes.Wallet,
                   StringComparison.OrdinalIgnoreCase)
               && policy.Store.AllowWalletRefunds
            ? ReturnRefundMode.Wallet
            : ReturnRefundMode.Original;
    }

    /// <summary>What quality control does with goods by default, given the verdict.</summary>
    /// <param name="passed">Whether the goods were as they should have been.</param>
    /// <param name="policy">The policies that apply.</param>
    public static ReturnDisposition DefaultDisposition(bool passed, ResolvedPolicy policy)
        => ParseDisposition(
               passed ? policy.Store.DefaultPassedDisposition : policy.Store.DefaultFailedDisposition)
           ?? (passed ? ReturnDisposition.Restock : ReturnDisposition.Quarantine);

    /// <summary>Reads a payer from the settings section's spelling of it.</summary>
    private static ReturnShippingPayer? ParsePayer(string? value)
        => Enum.TryParse<ReturnShippingPayer>(value, ignoreCase: true, out var parsed) ? parsed : null;

    /// <summary>Reads a disposition from the settings section's spelling of it.</summary>
    private static ReturnDisposition? ParseDisposition(string? value)
        => Enum.TryParse<ReturnDisposition>(value, ignoreCase: true, out var parsed)
           && parsed != ReturnDisposition.Pending
            ? parsed
            : null;
}
