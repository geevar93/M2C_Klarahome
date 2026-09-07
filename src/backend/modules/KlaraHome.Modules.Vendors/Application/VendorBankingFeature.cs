using FluentValidation;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.Security;
using KlaraHome.Modules.Vendors.Application.Validation;
using KlaraHome.Modules.Vendors.Domain;
using KlaraHome.Modules.Vendors.Infrastructure;
using KlaraHome.Modules.Vendors.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Vendors.Application;

/// <summary>
/// A seller's bank account, as the API states it.
/// </summary>
/// <remarks>
/// There is no field carrying the account number, and there is no endpoint that returns one. The
/// last four digits identify the account for anybody who needs to recognise it, and the full number
/// leaves this process exactly once — into the payment gateway, at provisioning.
/// </remarks>
/// <param name="Id">The account.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="AccountName">The name it is held in.</param>
/// <param name="AccountNumberLast4">Its last four digits.</param>
/// <param name="Ifsc">The branch IFSC.</param>
/// <param name="BankName">The bank, as the seller gave it.</param>
/// <param name="BranchName">The branch, as the seller gave it.</param>
/// <param name="IsPrimary">Whether payouts are sent here.</param>
/// <param name="VerificationStatus">Whether it has been proved to belong to the seller.</param>
/// <param name="VerifiedAt">When it was last checked.</param>
/// <param name="VerificationNote">Why a check failed.</param>
internal sealed record BankAccountResponse(
    Guid Id,
    Guid? VendorId,
    string AccountName,
    string AccountNumberLast4,
    string Ifsc,
    string? BankName,
    string? BranchName,
    bool IsPrimary,
    BankVerificationStatus VerificationStatus,
    DateTimeOffset? VerifiedAt,
    string? VerificationNote);

/// <summary>Lists a seller's bank accounts.</summary>
/// <param name="VendorId">The seller, or null for the caller's own.</param>
internal sealed record ListBankAccountsQuery(Guid? VendorId) : IQuery<IReadOnlyList<BankAccountResponse>>;

/// <summary>Records a bank account for a seller.</summary>
/// <param name="VendorId">The seller, or null for the caller's own.</param>
/// <param name="AccountName">The name the account is held in.</param>
/// <param name="AccountNumber">The account number. Encrypted before it is stored.</param>
/// <param name="Ifsc">The branch IFSC.</param>
/// <param name="BankName">The bank.</param>
/// <param name="BranchName">The branch.</param>
/// <param name="MakePrimary">Whether payouts should be sent here.</param>
internal sealed record AddBankAccountCommand(
    Guid? VendorId,
    string AccountName,
    string AccountNumber,
    string Ifsc,
    string? BankName,
    string? BranchName,
    bool MakePrimary) : ICommand<BankAccountResponse>;

/// <summary>Makes one of a seller's accounts the one payouts go to.</summary>
/// <param name="VendorId">The seller, or null for the caller's own.</param>
/// <param name="AccountId">The account.</param>
internal sealed record SetPrimaryBankAccountCommand(Guid? VendorId, Guid AccountId)
    : ICommand<BankAccountResponse>;

/// <summary>Records the outcome of a check on an account.</summary>
/// <param name="VendorId">The seller.</param>
/// <param name="AccountId">The account.</param>
/// <param name="Verified">Whether the check passed.</param>
/// <param name="Note">Why, for a failure.</param>
internal sealed record VerifyBankAccountCommand(Guid VendorId, Guid AccountId, bool Verified, string? Note)
    : ICommand<BankAccountResponse>;

/// <summary>Removes an account a seller no longer uses.</summary>
/// <param name="VendorId">The seller, or null for the caller's own.</param>
/// <param name="AccountId">The account.</param>
internal sealed record RemoveBankAccountCommand(Guid? VendorId, Guid AccountId) : ICommand;

/// <summary>Rules for a new account.</summary>
internal sealed class AddBankAccountValidator : AbstractValidator<AddBankAccountCommand>
{
    public AddBankAccountValidator()
    {
        RuleFor(command => command.AccountName).NotEmpty().MaximumLength(200);

        RuleFor(command => command.AccountNumber)
            .NotEmpty()
            .Matches(VendorFormats.BankAccountNumber())
            .WithMessage("An account number is 9 to 18 digits, with no spaces.");

        RuleFor(command => command.Ifsc)
            .NotEmpty()
            .Matches(VendorFormats.Ifsc())
            .WithMessage("An IFSC is four letters, a zero and six characters — for example HDFC0001234.");

        RuleFor(command => command.BankName!).MaximumLength(120).When(command => command.BankName is not null);
        RuleFor(command => command.BranchName!).MaximumLength(120).When(command => command.BranchName is not null);
    }
}

/// <summary>Rules for a verification decision.</summary>
internal sealed class VerifyBankAccountValidator : AbstractValidator<VerifyBankAccountCommand>
{
    public VerifyBankAccountValidator()
    {
        RuleFor(command => command.VendorId).NotEmpty();
        RuleFor(command => command.AccountId).NotEmpty();
        RuleFor(command => command.Note!).MaximumLength(500).When(command => command.Note is not null);
        RuleFor(command => command.Note)
            .NotEmpty()
            .When(command => !command.Verified)
            .WithMessage("Say why the check failed.");
    }
}

/// <summary>Lists a seller's accounts.</summary>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Finds the seller the caller may act on.</param>
internal sealed class ListBankAccountsQueryHandler(VendorsDbContext context, VendorScope scope)
    : IQueryHandler<ListBankAccountsQuery, IReadOnlyList<BankAccountResponse>>
{
    public async Task<Result<IReadOnlyList<BankAccountResponse>>> HandleAsync(
        ListBankAccountsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var resolved = scope.Resolve(query.VendorId);

        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var accounts = await context.BankAccounts
            .AsNoTracking()
            .Where(account => account.VendorId == resolved.Value)
            .OrderByDescending(account => account.IsPrimary)
            .ThenBy(account => account.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<BankAccountResponse>>(
            accounts.ConvertAll(BankProjection.ToResponse));
    }
}

/// <summary>Records a bank account, encrypting the number on the way in.</summary>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Finds the seller the caller may act on.</param>
/// <param name="protector">Encrypts the account number.</param>
/// <param name="audit">Records that an account was added — never what it is.</param>
internal sealed class AddBankAccountCommandHandler(
    VendorsDbContext context,
    VendorScope scope,
    IFieldProtector protector,
    IAuditLogger audit) : ICommandHandler<AddBankAccountCommand, BankAccountResponse>
{
    /// <summary>The audited action for a new account.</summary>
    public const string AuditAction = "vendors.bank-account.added";

    /// <summary>The entity type recorded against a bank-account action.</summary>
    public const string AuditEntityType = "VendorBankAccount";

    public async Task<Result<BankAccountResponse>> HandleAsync(
        AddBankAccountCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var resolved = scope.Resolve(command.VendorId);

        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var vendor = await scope.FindAsync(resolved.Value, cancellationToken).ConfigureAwait(false);

        if (vendor is null)
        {
            return VendorErrors.NotFound;
        }

        // A deployment with no encryption key cannot store an account number, and storing it in the
        // clear "for now" is how it stays in the clear. Refused at the request rather than at boot,
        // because every other part of this module works without a key.
        if (!protector.IsConfigured)
        {
            return Error.Unavailable(
                "VENDOR_ENCRYPTION_UNCONFIGURED",
                "This deployment has no column-encryption key, so a bank account cannot be stored.");
        }

        var number = command.AccountNumber.Trim();
        var ifsc = command.Ifsc.Trim().ToUpperInvariant();

        var existing = await context.BankAccounts
            .Where(account => account.VendorId == vendor.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Ciphertext is not comparable — the same number encrypts differently every time, which is
        // the point of the nonce — so a duplicate is caught on the two fields that are readable.
        // It is a warning against double entry, not a security control.
        if (existing.Exists(account =>
                account.AccountNumberLast4 == VendorBankAccount.Last4(number) && account.Ifsc == ifsc))
        {
            return Error.Conflict(
                "VENDOR_BANK_ACCOUNT_EXISTS",
                "That account is already on file for this seller.");
        }

        var account = VendorBankAccount.Add(
            vendor.Id,
            command.AccountName.Trim(),
            protector.Protect(number),
            VendorBankAccount.Last4(number),
            ifsc);

        account.Describe(command.BankName, command.BranchName);

        // The first account is always primary: a seller with one account and no primary is a seller
        // who cannot be paid, for no reason anybody chose.
        if (command.MakePrimary || existing.Count == 0)
        {
            foreach (var other in existing)
            {
                other.SetPrimary(false);
            }

            account.SetPrimary(true);
        }

        context.BankAccounts.Add(account);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AuditEntityType,
                EntityId = account.Id.ToString(),
                After = new
                {
                    vendorId = vendor.Id,
                    account.AccountName,
                    account.AccountNumberLast4,
                    account.Ifsc,
                    account.IsPrimary,
                },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(BankProjection.ToResponse(account));
    }
}

/// <summary>Moves the primary flag to another account.</summary>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Finds the seller the caller may act on.</param>
/// <param name="audit">Records where the money will now go.</param>
internal sealed class SetPrimaryBankAccountCommandHandler(
    VendorsDbContext context,
    VendorScope scope,
    IAuditLogger audit) : ICommandHandler<SetPrimaryBankAccountCommand, BankAccountResponse>
{
    /// <summary>The audited action for a change of payout account.</summary>
    public const string AuditAction = "vendors.bank-account.made-primary";

    public async Task<Result<BankAccountResponse>> HandleAsync(
        SetPrimaryBankAccountCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var resolved = scope.Resolve(command.VendorId);

        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var accounts = await context.BankAccounts
            .Where(account => account.VendorId == resolved.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var chosen = accounts.Find(account => account.Id == command.AccountId);

        if (chosen is null)
        {
            return VendorErrors.ChildNotFound("bank account");
        }

        var previous = accounts.Find(account => account.IsPrimary && account.Id != chosen.Id);

        foreach (var account in accounts)
        {
            account.SetPrimary(account.Id == chosen.Id);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AddBankAccountCommandHandler.AuditEntityType,
                EntityId = chosen.Id.ToString(),
                Before = new { accountId = previous?.Id, last4 = previous?.AccountNumberLast4 },
                After = new { accountId = chosen.Id, last4 = chosen.AccountNumberLast4 },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(BankProjection.ToResponse(chosen));
    }
}

/// <summary>Records the outcome of a check on an account.</summary>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Refuses a seller checking their own account.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="audit">Records the decision.</param>
internal sealed class VerifyBankAccountCommandHandler(
    VendorsDbContext context,
    VendorScope scope,
    IClock clock,
    IAuditLogger audit) : ICommandHandler<VerifyBankAccountCommand, BankAccountResponse>
{
    /// <summary>The audited action for a verification decision.</summary>
    public const string AuditAction = "vendors.bank-account.verified";

    public async Task<Result<BankAccountResponse>> HandleAsync(
        VerifyBankAccountCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (scope.IsVendorCaller)
        {
            return VendorErrors.PlatformOnly;
        }

        var account = await context.BankAccounts
            .FirstOrDefaultAsync(
                candidate => candidate.Id == command.AccountId && candidate.VendorId == command.VendorId,
                cancellationToken)
            .ConfigureAwait(false);

        if (account is null)
        {
            return VendorErrors.ChildNotFound("bank account");
        }

        var before = account.VerificationStatus;

        account.RecordVerification(
            command.Verified ? BankVerificationStatus.Verified : BankVerificationStatus.Failed,
            clock.UtcNow,
            command.Note);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AddBankAccountCommandHandler.AuditEntityType,
                EntityId = account.Id.ToString(),
                Before = new { status = before.ToString() },
                After = new
                {
                    status = account.VerificationStatus.ToString(),
                    account.VerificationNote,
                    vendorId = command.VendorId,
                },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(BankProjection.ToResponse(account));
    }
}

/// <summary>Removes an account.</summary>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Finds the seller the caller may act on.</param>
/// <param name="audit">Records the removal.</param>
internal sealed class RemoveBankAccountCommandHandler(
    VendorsDbContext context,
    VendorScope scope,
    IAuditLogger audit) : ICommandHandler<RemoveBankAccountCommand>
{
    /// <summary>The audited action for a removed account.</summary>
    public const string AuditAction = "vendors.bank-account.removed";

    public async Task<Result> HandleAsync(RemoveBankAccountCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var resolved = scope.Resolve(command.VendorId);

        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        var accounts = await context.BankAccounts
            .Where(account => account.VendorId == resolved.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var account = accounts.Find(candidate => candidate.Id == command.AccountId);

        if (account is null)
        {
            return Result.Failure(VendorErrors.ChildNotFound("bank account"));
        }

        // Removing the account the money goes to, while another exists, would leave the seller with
        // no payout destination and no error to tell them so. Move the primary flag first.
        if (account.IsPrimary && accounts.Count > 1)
        {
            return Result.Failure(VendorErrors.NotReady(
                "Make another account primary before removing this one."));
        }

        context.BankAccounts.Remove(account);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AddBankAccountCommandHandler.AuditEntityType,
                EntityId = account.Id.ToString(),
                Before = new
                {
                    vendorId = resolved.Value,
                    account.AccountNumberLast4,
                    account.Ifsc,
                },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Maps an account onto its API shape.</summary>
internal static class BankProjection
{
    /// <summary>Builds the response. There is deliberately no path from here to the account number.</summary>
    /// <param name="account">The account.</param>
    public static BankAccountResponse ToResponse(VendorBankAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return new BankAccountResponse(
            account.Id,
            account.VendorId,
            account.AccountName,
            account.AccountNumberLast4,
            account.Ifsc,
            account.BankName,
            account.BranchName,
            account.IsPrimary,
            account.VerificationStatus,
            account.VerifiedAt,
            account.VerificationNote);
    }
}
