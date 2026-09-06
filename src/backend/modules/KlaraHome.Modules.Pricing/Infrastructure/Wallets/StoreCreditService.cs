using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Pricing;
using KlaraHome.Modules.Pricing.Domain;
using KlaraHome.Modules.Pricing.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Primitives;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Pricing.Infrastructure.Wallets;

/// <summary>
/// The store-credit wallet (docs/03-database-design.md §4.6).
/// </summary>
/// <remarks>
/// <para>
/// Every mover is keyed on the caller's reference and refuses to act twice on the same one. That is
/// not defensive coding: the events that credit a wallet — a refund, a cancellation, a loyalty
/// accrual — are delivered at least once, and a wallet that credited twice would be money given
/// away with no record of why.
/// </para>
/// <para>
/// The whole surface is behind the <c>pricing.store-credit</c> flag. With it off the balance reads
/// as zero and inactive and every mover refuses, so a deployment that has not decided its loyalty
/// rules cannot accrue a liability by accident.
/// </para>
/// </remarks>
/// <param name="context">The Pricing data context.</param>
/// <param name="settings">Supplies the store's currency.</param>
/// <param name="features">Gates the whole wallet.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class StoreCreditService(
    PricingDbContext context,
    IStoreSettings settings,
    IFeatureFlags features,
    IClock clock) : IStoreCredit
{
    /// <inheritdoc />
    public async ValueTask<StoreCreditBalance> GetBalanceAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var currency = await CurrencyAsync(cancellationToken).ConfigureAwait(false);

        if (!await EnabledAsync(customerId, cancellationToken).ConfigureAwait(false))
        {
            return new StoreCreditBalance(customerId, 0m, currency, IsActive: false);
        }

        var wallet = await context.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.CustomerId == customerId, cancellationToken)
            .ConfigureAwait(false);

        return wallet is null
            ? new StoreCreditBalance(customerId, 0m, currency, IsActive: true)
            : new StoreCreditBalance(customerId, wallet.Balance.Amount, wallet.Balance.Currency, wallet.IsActive);
    }

    /// <inheritdoc />
    public async ValueTask<decimal> RedeemAsync(
        Guid customerId,
        decimal amount,
        string reason,
        string referenceType,
        Guid referenceId,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0m || !await EnabledAsync(customerId, cancellationToken).ConfigureAwait(false))
        {
            return 0m;
        }

        var existing = await FindMovementAsync(
                customerId,
                WalletTransactionType.Debit,
                referenceType,
                referenceId,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing.Amount.Amount;
        }

        var wallet = await context.Wallets
            .FirstOrDefaultAsync(candidate => candidate.CustomerId == customerId, cancellationToken)
            .ConfigureAwait(false);

        if (wallet is null || !wallet.IsActive)
        {
            return 0m;
        }

        var movement = wallet.Apply(
            WalletTransactionType.Debit,
            amount,
            reason,
            referenceType,
            referenceId,
            clock.UtcNow);

        if (movement is null)
        {
            return 0m;
        }

        context.WalletTransactions.Add(movement);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return movement.Amount.Amount;
    }

    /// <inheritdoc />
    public async ValueTask<StoreCreditBalance> CreditAsync(
        Guid customerId,
        decimal amount,
        string reason,
        string referenceType,
        Guid referenceId,
        DateTimeOffset? expiresAt = null,
        CancellationToken cancellationToken = default)
    {
        var currency = await CurrencyAsync(cancellationToken).ConfigureAwait(false);

        if (!await EnabledAsync(customerId, cancellationToken).ConfigureAwait(false))
        {
            return new StoreCreditBalance(customerId, 0m, currency, IsActive: false);
        }

        var wallet = await context.Wallets
            .FirstOrDefaultAsync(candidate => candidate.CustomerId == customerId, cancellationToken)
            .ConfigureAwait(false);

        if (wallet is null)
        {
            wallet = Wallet.Open(customerId, currency);
            context.Wallets.Add(wallet);
        }
        else if (await FindMovementAsync(
                     customerId,
                     WalletTransactionType.Credit,
                     referenceType,
                     referenceId,
                     cancellationToken)
                 .ConfigureAwait(false) is not null)
        {
            // The same refund arriving twice. The balance already includes it, so the honest answer
            // is what the wallet holds now rather than a second credit.
            return Describe(wallet);
        }

        var movement = wallet.Apply(
            WalletTransactionType.Credit,
            amount,
            reason,
            referenceType,
            referenceId,
            clock.UtcNow,
            expiresAt);

        if (movement is not null)
        {
            context.WalletTransactions.Add(movement);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Describe(wallet);
    }

    /// <summary>
    /// Adjusts a wallet by hand, in either direction. Used by the admin surface only.
    /// </summary>
    /// <remarks>
    /// Not on <see cref="IStoreCredit"/>: a goodwill gesture is a staff action taken through an
    /// audited endpoint, and exporting it across the module boundary would let any module hand out
    /// money without one.
    /// </remarks>
    /// <param name="customerId">The shopper.</param>
    /// <param name="amount">Signed. Positive credits, negative debits.</param>
    /// <param name="reason">Why, from <see cref="StoreCreditReasons"/>.</param>
    /// <param name="note">Free text the operator typed, shown on the statement.</param>
    /// <param name="expiresAt">When the credit lapses, for a credit that does.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The movement, or null when there was nothing to move or too little to take.</returns>
    public async Task<WalletTransaction?> AdjustAsync(
        Guid customerId,
        decimal amount,
        string reason,
        string? note,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken)
    {
        if (amount == 0m)
        {
            return null;
        }

        var currency = await CurrencyAsync(cancellationToken).ConfigureAwait(false);

        var wallet = await context.Wallets
            .FirstOrDefaultAsync(candidate => candidate.CustomerId == customerId, cancellationToken)
            .ConfigureAwait(false);

        if (wallet is null)
        {
            wallet = Wallet.Open(customerId, currency);
            context.Wallets.Add(wallet);
        }

        var movement = wallet.Apply(
            amount > 0m ? WalletTransactionType.Credit : WalletTransactionType.Debit,
            Math.Abs(amount),
            reason,
            referenceType: null,
            referenceId: null,
            clock.UtcNow,
            expiresAt,
            note);

        if (movement is null)
        {
            return null;
        }

        context.WalletTransactions.Add(movement);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return movement;
    }

    /// <summary>Finds an earlier movement with the same reference, which is the idempotency check.</summary>
    /// <param name="customerId">The shopper.</param>
    /// <param name="type">Which way it moved.</param>
    /// <param name="referenceType">What caused it.</param>
    /// <param name="referenceId">The causing record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private Task<WalletTransaction?> FindMovementAsync(
        Guid customerId,
        WalletTransactionType type,
        string referenceType,
        Guid referenceId,
        CancellationToken cancellationToken)
        => (from transaction in context.WalletTransactions.AsNoTracking()
            join wallet in context.Wallets.AsNoTracking() on transaction.WalletId equals wallet.Id
            where wallet.CustomerId == customerId
                  && transaction.Type == type
                  && transaction.ReferenceType == referenceType
                  && transaction.ReferenceId == referenceId
            select transaction).FirstOrDefaultAsync(cancellationToken);

    /// <summary>Whether the wallet is switched on for this shopper.</summary>
    /// <param name="customerId">The shopper, for a percentage or segment rollout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private ValueTask<bool> EnabledAsync(Guid customerId, CancellationToken cancellationToken)
        => features.IsEnabledAsync(
            PricingFeatureFlags.StoreCredit,
            new FeatureAudience(customerId),
            cancellationToken);

    /// <summary>The currency a new wallet is opened in.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async ValueTask<string> CurrencyAsync(CancellationToken cancellationToken)
    {
        var localization = await settings.GetAsync<LocalizationSettings>(cancellationToken).ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(localization.CurrencyCode) ? Money.Inr : localization.CurrencyCode;
    }

    /// <summary>States a wallet for a caller of the published contract.</summary>
    /// <param name="wallet">The wallet.</param>
    private static StoreCreditBalance Describe(Wallet wallet)
        => new(wallet.CustomerId, wallet.Balance.Amount, wallet.Balance.Currency, wallet.IsActive);
}
