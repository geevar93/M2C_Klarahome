using FluentValidation;
using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Identity.Application.Authentication;
using KlaraHome.Modules.Identity.Application.Validation;
using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Identity.Application.Account;

/// <summary>An address as the caller supplies it.</summary>
/// <param name="Label">What the customer calls it.</param>
/// <param name="RecipientName">Who the courier asks for.</param>
/// <param name="Mobile">The delivery contact number.</param>
/// <param name="Line1">House or flat and building.</param>
/// <param name="Line2">Street or locality.</param>
/// <param name="Landmark">A nearby landmark.</param>
/// <param name="City">City or town.</param>
/// <param name="StateId">The state or union territory, from <c>GET /store/states</c>.</param>
/// <param name="Pincode">Six-digit PIN code.</param>
/// <param name="Gstin">A GSTIN for invoices billed to this address.</param>
/// <param name="Type">Home or office.</param>
/// <param name="IsDefaultShipping">Whether checkout should ship here by default.</param>
/// <param name="IsDefaultBilling">Whether invoices should bill here by default.</param>
internal sealed record AddressInput(
    string? Label,
    string RecipientName,
    string Mobile,
    string Line1,
    string? Line2,
    string? Landmark,
    string City,
    Guid StateId,
    string Pincode,
    string? Gstin,
    AddressType Type,
    bool IsDefaultShipping,
    bool IsDefaultBilling);

/// <summary>Lists the caller's saved addresses.</summary>
internal sealed record GetAddressesQuery : IQuery<IReadOnlyList<AddressResponse>>;

/// <summary>Saves a new address for the caller.</summary>
/// <param name="Address">The address.</param>
internal sealed record CreateAddressCommand(AddressInput Address) : ICommand<AddressResponse>;

/// <summary>Replaces one of the caller's addresses.</summary>
/// <param name="Id">The address to replace.</param>
/// <param name="Address">The new contents.</param>
internal sealed record UpdateAddressCommand(Guid Id, AddressInput Address) : ICommand<AddressResponse>;

/// <summary>Removes one of the caller's addresses.</summary>
/// <param name="Id">The address to remove.</param>
internal sealed record DeleteAddressCommand(Guid Id) : ICommand;

/// <summary>A saved address.</summary>
/// <param name="Id">The address id.</param>
/// <param name="Label">What the customer calls it.</param>
/// <param name="RecipientName">Who the courier asks for.</param>
/// <param name="Mobile">The delivery contact number.</param>
/// <param name="Line1">House or flat and building.</param>
/// <param name="Line2">Street or locality.</param>
/// <param name="Landmark">A nearby landmark.</param>
/// <param name="City">City or town.</param>
/// <param name="StateId">The state or union territory.</param>
/// <param name="Pincode">Six-digit PIN code.</param>
/// <param name="Gstin">A GSTIN for invoices billed here.</param>
/// <param name="Type">Home or office.</param>
/// <param name="IsDefaultShipping">Whether checkout ships here by default.</param>
/// <param name="IsDefaultBilling">Whether invoices bill here by default.</param>
internal sealed record AddressResponse(
    Guid Id,
    string? Label,
    string RecipientName,
    string Mobile,
    string Line1,
    string? Line2,
    string? Landmark,
    string City,
    Guid StateId,
    string Pincode,
    string? Gstin,
    AddressType Type,
    bool IsDefaultShipping,
    bool IsDefaultBilling);

/// <summary>
/// The rules an Indian address must satisfy.
/// </summary>
/// <remarks>
/// The state is required and is checked against <c>platform.states</c> by the handler, not here:
/// place of supply decides whether a sale is taxed as CGST + SGST or as IGST, so an address whose
/// state is wrong is an invoice that is wrong.
/// </remarks>
internal sealed class AddressInputValidator : AbstractValidator<AddressInput>
{
    public AddressInputValidator()
    {
        RuleFor(address => address.RecipientName).NotEmpty().MaximumLength(120);

        RuleFor(address => address.Mobile)
            .Must(IndianMobile.IsValid)
            .WithMessage("Enter a valid Indian mobile number for the delivery contact.");

        RuleFor(address => address.Line1).NotEmpty().MaximumLength(200);
        RuleFor(address => address.Line2).MaximumLength(200);
        RuleFor(address => address.Landmark).MaximumLength(200);
        RuleFor(address => address.City).NotEmpty().MaximumLength(100);
        RuleFor(address => address.Label).MaximumLength(40);
        RuleFor(address => address.StateId).NotEmpty().WithMessage("Choose a state or union territory.");

        RuleFor(address => address.Pincode)
            .NotEmpty()
            .Matches(IdentityFormats.Pincode())
            .WithMessage("A PIN code is six digits and does not start with zero.");

        RuleFor(address => address.Gstin!)
            .Matches(IdentityFormats.Gstin())
            .When(address => !string.IsNullOrWhiteSpace(address.Gstin))
            .WithMessage("A GSTIN is 15 characters, for example 27AAPFU0939F1ZV.");
    }
}

/// <summary>Rules for creating an address.</summary>
internal sealed class CreateAddressValidator : AbstractValidator<CreateAddressCommand>
{
    public CreateAddressValidator(AddressInputValidator address)
        => RuleFor(command => command.Address).NotNull().SetValidator(address);
}

/// <summary>Rules for replacing an address.</summary>
internal sealed class UpdateAddressValidator : AbstractValidator<UpdateAddressCommand>
{
    public UpdateAddressValidator(AddressInputValidator address)
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.Address).NotNull().SetValidator(address);
    }
}

/// <summary>Lists the caller's saved addresses, the default shipping one first.</summary>
/// <param name="context">The Identity data context.</param>
/// <param name="caller">The signed-in caller.</param>
internal sealed class GetAddressesQueryHandler(IdentityDbContext context, ICallerContext caller)
    : IQueryHandler<GetAddressesQuery, IReadOnlyList<AddressResponse>>
{
    public async Task<Result<IReadOnlyList<AddressResponse>>> HandleAsync(
        GetAddressesQuery query,
        CancellationToken cancellationToken)
    {
        if (caller.UserId is null)
        {
            return Error.Unauthorized();
        }

        var addresses = await context.Addresses
            .Where(address => address.UserId == caller.UserId)
            .OrderByDescending(address => address.IsDefaultShipping)
            .ThenByDescending(address => address.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<AddressResponse> response = addresses.ConvertAll(AddressWriter.Project);
        return Result.Success(response);
    }
}

/// <summary>Saves a new address.</summary>
/// <param name="context">The Identity data context.</param>
/// <param name="caller">The signed-in caller.</param>
/// <param name="writer">Applies the input and keeps the defaults consistent.</param>
internal sealed class CreateAddressCommandHandler(
    IdentityDbContext context,
    ICallerContext caller,
    AddressWriter writer) : ICommandHandler<CreateAddressCommand, AddressResponse>
{
    public async Task<Result<AddressResponse>> HandleAsync(
        CreateAddressCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (caller.UserId is null)
        {
            return Error.Unauthorized();
        }

        var stateError = await writer
            .ValidateStateAsync(command.Address.StateId, cancellationToken)
            .ConfigureAwait(false);

        if (stateError is not null)
        {
            return stateError;
        }

        var userId = caller.UserId.Value;

        var isFirst = !await context.Addresses
            .AnyAsync(address => address.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        var address = Address.For(userId, command.Address.Type);
        context.Addresses.Add(address);

        // The first address a customer saves becomes both defaults whatever the request said.
        // Otherwise a customer with exactly one address can reach checkout with none selected.
        await writer
            .ApplyAsync(
                address,
                command.Address,
                command.Address.IsDefaultShipping || isFirst,
                command.Address.IsDefaultBilling || isFirst,
                cancellationToken)
            .ConfigureAwait(false);

        return AddressWriter.Project(address);
    }
}

/// <summary>Replaces one of the caller's addresses.</summary>
/// <param name="context">The Identity data context.</param>
/// <param name="caller">The signed-in caller.</param>
/// <param name="writer">Applies the input and keeps the defaults consistent.</param>
internal sealed class UpdateAddressCommandHandler(
    IdentityDbContext context,
    ICallerContext caller,
    AddressWriter writer) : ICommandHandler<UpdateAddressCommand, AddressResponse>
{
    public async Task<Result<AddressResponse>> HandleAsync(
        UpdateAddressCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (caller.UserId is null)
        {
            return Error.Unauthorized();
        }

        var address = await context.Addresses
            .FirstOrDefaultAsync(
                candidate => candidate.Id == command.Id && candidate.UserId == caller.UserId,
                cancellationToken)
            .ConfigureAwait(false);

        if (address is null)
        {
            return AddressWriter.NotFound();
        }

        var stateError = await writer
            .ValidateStateAsync(command.Address.StateId, cancellationToken)
            .ConfigureAwait(false);

        if (stateError is not null)
        {
            return stateError;
        }

        await writer
            .ApplyAsync(
                address,
                command.Address,
                command.Address.IsDefaultShipping,
                command.Address.IsDefaultBilling,
                cancellationToken)
            .ConfigureAwait(false);

        return AddressWriter.Project(address);
    }
}

/// <summary>Removes one of the caller's addresses.</summary>
/// <param name="context">The Identity data context.</param>
/// <param name="caller">The signed-in caller.</param>
/// <param name="clock">The clock.</param>
internal sealed class DeleteAddressCommandHandler(
    IdentityDbContext context,
    ICallerContext caller,
    IClock clock) : ICommandHandler<DeleteAddressCommand>
{
    public async Task<Result> HandleAsync(DeleteAddressCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (caller.UserId is null)
        {
            return Result.Failure(Error.Unauthorized());
        }

        var address = await context.Addresses
            .FirstOrDefaultAsync(
                candidate => candidate.Id == command.Id && candidate.UserId == caller.UserId,
                cancellationToken)
            .ConfigureAwait(false);

        if (address is null)
        {
            return Result.Failure(AddressWriter.NotFound());
        }

        // Soft-deleted, because an order placed to this address still points at it. The customer
        // sees it gone; an invoice from last year still resolves.
        address.Delete(clock.UtcNow);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

/// <summary>
/// The parts of writing an address that both the create and the update path need: validating the
/// state against the Platform module's list, and keeping at most one default of each kind.
/// </summary>
/// <remarks>
/// The defaults are cleared here <em>and</em> constrained by a partial unique index. The handler
/// keeps the common case correct; the index is what makes two browser tabs unable to leave a
/// customer with two default shipping addresses.
/// </remarks>
/// <param name="context">The Identity data context.</param>
/// <param name="states">The Platform module's jurisdiction list.</param>
internal sealed class AddressWriter(IdentityDbContext context, Contracts.Platform.IReferenceData states)
{
    /// <summary>The response for an address that is not the caller's, or does not exist.</summary>
    /// <remarks>
    /// 404 rather than 403 for someone else's address: an out-of-scope resource must be
    /// indistinguishable from an absent one (docs/07-security-compliance.md §2).
    /// </remarks>
    public static Error NotFound()
        => Error.NotFound("IDENTITY_ADDRESS_NOT_FOUND", "That address does not exist.");

    /// <summary>Checks a state id against the Platform module's list.</summary>
    /// <param name="stateId">The state the caller chose.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Error?> ValidateStateAsync(Guid stateId, CancellationToken cancellationToken)
        => await states.StateExistsAsync(stateId, cancellationToken).ConfigureAwait(false)
            ? null
            : Error.Validation(
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["stateId"] = ["Choose a state or union territory from the list."],
                });

    /// <summary>Applies the input, clears any superseded default, and saves.</summary>
    /// <param name="address">The address being written.</param>
    /// <param name="input">What the caller supplied.</param>
    /// <param name="defaultShipping">Whether this becomes the default shipping address.</param>
    /// <param name="defaultBilling">Whether this becomes the default billing address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ApplyAsync(
        Address address,
        AddressInput input,
        bool defaultShipping,
        bool defaultBilling,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(input);

        address.SetDetails(
            input.Label,
            input.RecipientName,
            IndianMobile.Normalize(input.Mobile),
            input.Line1,
            input.Line2,
            input.Landmark,
            input.City,
            input.StateId,
            input.Pincode,
            string.IsNullOrWhiteSpace(input.Gstin) ? null : input.Gstin.Trim().ToUpperInvariant(),
            input.Type);

        if (defaultShipping || defaultBilling)
        {
            var others = await context.Addresses
                .Where(candidate => candidate.UserId == address.UserId && candidate.Id != address.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var other in others)
            {
                other.SetDefaults(
                    other.IsDefaultShipping && !defaultShipping,
                    other.IsDefaultBilling && !defaultBilling);
            }
        }

        address.SetDefaults(defaultShipping, defaultBilling);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Projects an address onto its response.</summary>
    /// <param name="address">The address.</param>
    public static AddressResponse Project(Address address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return new AddressResponse(
            address.Id,
            address.Label,
            address.RecipientName,
            address.Mobile,
            address.Line1,
            address.Line2,
            address.Landmark,
            address.City,
            address.StateId,
            address.Pincode,
            address.Gstin,
            address.Type,
            address.IsDefaultShipping,
            address.IsDefaultBilling);
    }
}
