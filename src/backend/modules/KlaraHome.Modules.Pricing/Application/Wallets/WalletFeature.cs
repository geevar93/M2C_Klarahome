using FluentValidation;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Pricing;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Pricing.Infrastructure.Persistence;
using KlaraHome.Modules.Pricing.Infrastructure.Wallets;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Pricing.Application.Wallets;

/// <summary>A wallet, as the API states it.</summary>
/// <param name="CustomerId">The shopper.</param>
/// <param name="Balance">What they have.</param>
/// <param name="CurrencyCode">ISO 4217 code the balance is in.</param>
/// <param name="IsActive">Whether it may still be spent from.</param>
/// <param name="UpdatedAt">When it last moved.</param>
internal sealed record WalletResponse(
    Guid CustomerId,
    decimal Balance,
    string CurrencyCode,
    bool IsActive,
    DateTimeOffset? UpdatedAt);

/// <summary>One movement of store credit, as the API states it.</summary>
/// <param name="Id">The movement.</param>
/// <param name="Type">Which way it moved.</param>
/// <param name="Amount">How much. Always positive; the type carries the sign.</param>
/// <param name="BalanceAfter">What the wallet held afterwards.</param>
/// <param name="Reason">Why.</param>
/// <param name="ReferenceType">What caused it.</param>
/// <param name="ReferenceId">The causing record.</param>
/// <param name="OccurredAt">When.</param>
/// <param name="ExpiresAt">When this credit lapses.</param>
/// <param name="Note">Free text an operator typed.</param>
internal sealed record WalletTransactionResponse(
    Guid Id,
    string Type,
    decimal Amount,
    decimal BalanceAfter,
    string Reason,
    string? ReferenceType,
    Guid? ReferenceId,
    DateTimeOffset OccurredAt,
    DateTimeOffset? ExpiresAt,
    string? Note);

/// <summary>Lists wallets.</summary>
/// <param name="CustomerId">Restrict to one shopper.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListWalletsQuery(Guid? CustomerId, string? Cursor, int? Size)
    : IQuery<PagedResult<WalletResponse>>;

/// <summary>Reads one shopper's balance.</summary>
/// <param name="CustomerId">The shopper.</param>
internal sealed record GetWalletQuery(Guid CustomerId) : IQuery<WalletResponse>;

/// <summary>Lists one shopper's movements, newest first.</summary>
/// <param name="CustomerId">The shopper.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListWalletTransactionsQuery(Guid CustomerId, string? Cursor, int? Size)
    : IQuery<PagedResult<WalletTransactionResponse>>;

/// <summary>Adjusts a shopper's credit by hand.</summary>
/// <param name="CustomerId">The shopper.</param>
/// <param name="Amount">Signed. Positive credits, negative debits.</param>
/// <param name="Reason">Why, from <see cref="StoreCreditReasons"/>.</param>
/// <param name="Note">Free text shown on the statement.</param>
/// <param name="ExpiresAt">When the credit lapses, for a credit that does.</param>
internal sealed record AdjustWalletCommand(
    Guid CustomerId,
    decimal Amount,
    string Reason,
    string? Note,
    DateTimeOffset? ExpiresAt) : ICommand<WalletResponse>;

/// <summary>Rejects an adjustment that could never be stored.</summary>
internal sealed class AdjustWalletValidator : AbstractValidator<AdjustWalletCommand>
{
    public AdjustWalletValidator()
    {
        RuleFor(command => command.CustomerId).NotEmpty();
        RuleFor(command => command.Amount).NotEqual(0m).WithMessage("An adjustment has to change something.");
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(48);
        RuleFor(command => command.Note).MaximumLength(500);
    }
}

/// <summary>Lists wallets.</summary>
/// <param name="context">The Pricing data context.</param>
internal sealed class ListWalletsQueryHandler(PricingDbContext context)
    : IQueryHandler<ListWalletsQuery, PagedResult<WalletResponse>>
{
    public async Task<Result<PagedResult<WalletResponse>>> HandleAsync(
        ListWalletsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);
        var rows = context.Wallets.AsNoTracking();

        if (query.CustomerId is { } customerId)
        {
            rows = rows.Where(wallet => wallet.CustomerId == customerId);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(wallet => wallet.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(wallet => wallet.Id)
            .Take(size + 1)
            .Select(wallet => new WalletResponse(
                wallet.CustomerId,
                wallet.Balance.Amount,
                wallet.Balance.Currency,
                wallet.IsActive,
                wallet.UpdatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        return Result.Success(new PagedResult<WalletResponse>(
            page,
            new PageInfo(size, hasMore ? Cursor.Encode(page[^1].CustomerId.ToString()) : null)));
    }
}

/// <summary>
/// Reads one shopper's balance.
/// </summary>
/// <remarks>
/// Through <see cref="IStoreCredit"/> rather than the table, so the feature flag is honoured in one
/// place. A shopper whose store has the wallet switched off is told they have nothing, which is
/// true, rather than being shown a balance they cannot spend.
/// </remarks>
/// <param name="credit">The wallet service.</param>
internal sealed class GetWalletQueryHandler(IStoreCredit credit) : IQueryHandler<GetWalletQuery, WalletResponse>
{
    public async Task<Result<WalletResponse>> HandleAsync(GetWalletQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var balance = await credit.GetBalanceAsync(query.CustomerId, cancellationToken).ConfigureAwait(false);

        return Result.Success(new WalletResponse(
            balance.CustomerId,
            balance.Balance,
            balance.CurrencyCode,
            balance.IsActive,
            UpdatedAt: null));
    }
}

/// <summary>Lists one shopper's movements.</summary>
/// <param name="context">The Pricing data context.</param>
internal sealed class ListWalletTransactionsQueryHandler(PricingDbContext context)
    : IQueryHandler<ListWalletTransactionsQuery, PagedResult<WalletTransactionResponse>>
{
    public async Task<Result<PagedResult<WalletTransactionResponse>>> HandleAsync(
        ListWalletTransactionsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);

        var rows = from transaction in context.WalletTransactions.AsNoTracking()
                   join wallet in context.Wallets.AsNoTracking() on transaction.WalletId equals wallet.Id
                   where wallet.CustomerId == query.CustomerId
                   select transaction;

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(transaction => transaction.Id.CompareTo(after) < 0);
        }

        // Ordered by id rather than by the timestamp: the id is UUIDv7, so it is already in time
        // order, and it is the only column a keyset cursor can page on without ties.
        var page = await rows
            .OrderByDescending(transaction => transaction.Id)
            .Take(size + 1)
            .Select(transaction => new WalletTransactionResponse(
                transaction.Id,
                transaction.Type.ToString(),
                transaction.Amount.Amount,
                transaction.BalanceAfter.Amount,
                transaction.Reason,
                transaction.ReferenceType,
                transaction.ReferenceId,
                transaction.OccurredAt,
                transaction.ExpiresAt,
                transaction.Note))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        return Result.Success(new PagedResult<WalletTransactionResponse>(
            page,
            new PageInfo(size, hasMore ? Cursor.Encode(page[^1].Id.ToString()) : null)));
    }
}

/// <summary>
/// Adjusts a shopper's credit by hand.
/// </summary>
/// <remarks>
/// The one endpoint in this platform that creates money, so it is audited with the actor, the
/// amount and the reason. It is deliberately not on <see cref="IStoreCredit"/>: a goodwill gesture
/// is a staff action taken through an audited route, and exporting it across the module boundary
/// would let any module hand out credit without one.
/// </remarks>
/// <param name="credit">The wallet service.</param>
/// <param name="audit">Records the adjustment.</param>
internal sealed class AdjustWalletCommandHandler(StoreCreditService credit, IAuditLogger audit)
    : ICommandHandler<AdjustWalletCommand, WalletResponse>
{
    /// <summary>The audited action for a manual wallet adjustment.</summary>
    public const string AuditAction = "pricing.wallet.adjusted";

    /// <summary>The entity type recorded against every wallet action.</summary>
    public const string AuditEntityType = "Wallet";

    public async Task<Result<WalletResponse>> HandleAsync(
        AdjustWalletCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Amount == 0m)
        {
            return PricingErrors.EmptyAdjustment;
        }

        var movement = await credit
            .AdjustAsync(
                command.CustomerId,
                command.Amount,
                command.Reason,
                command.Note,
                command.ExpiresAt,
                cancellationToken)
            .ConfigureAwait(false);

        if (movement is null)
        {
            return PricingErrors.InsufficientCredit;
        }

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AuditEntityType,
                EntityId = command.CustomerId.ToString(),
                After = new
                {
                    command.Amount,
                    command.Reason,
                    BalanceAfter = movement.BalanceAfter.Amount,
                },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(new WalletResponse(
            command.CustomerId,
            movement.BalanceAfter.Amount,
            movement.BalanceAfter.Currency,
            IsActive: true,
            movement.OccurredAt));
    }
}
