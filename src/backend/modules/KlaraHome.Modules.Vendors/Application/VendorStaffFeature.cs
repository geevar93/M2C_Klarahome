using FluentValidation;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Vendors.Domain;
using KlaraHome.Modules.Vendors.Infrastructure;
using KlaraHome.Modules.Vendors.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Vendors.Application;

/// <summary>
/// One person on a seller's account, as the API states it.
/// </summary>
/// <remarks>
/// Deliberately thin. The account's email address, its status and the roles it holds belong to the
/// Identity module and are read from its own endpoints — duplicating them here would produce two
/// answers to "is this person locked out", which is the failure mode a modular monolith is supposed
/// to avoid.
/// </remarks>
/// <param name="Id">The membership.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="UserId">The <c>identity.users</c> account.</param>
/// <param name="IsOwner">Whether they are the legal owner.</param>
/// <param name="JobTitle">What they do for the seller.</param>
/// <param name="AddedAt">When they joined.</param>
internal sealed record VendorStaffResponse(
    Guid Id,
    Guid? VendorId,
    Guid UserId,
    bool IsOwner,
    string? JobTitle,
    DateTimeOffset AddedAt);

/// <summary>Lists the people on a seller's account.</summary>
/// <param name="VendorId">The seller, or null for the caller's own.</param>
internal sealed record ListVendorStaffQuery(Guid? VendorId) : IQuery<IReadOnlyList<VendorStaffResponse>>;

/// <summary>Adds a person to a seller's account.</summary>
/// <param name="VendorId">The seller, or null for the caller's own.</param>
/// <param name="UserId">The Identity account to add.</param>
/// <param name="IsOwner">Whether they become the legal owner.</param>
/// <param name="JobTitle">What they do for the seller.</param>
internal sealed record AddVendorStaffCommand(Guid? VendorId, Guid UserId, bool IsOwner, string? JobTitle)
    : ICommand<VendorStaffResponse>;

/// <summary>Changes what a member is on the account.</summary>
/// <param name="VendorId">The seller, or null for the caller's own.</param>
/// <param name="MembershipId">The membership.</param>
/// <param name="IsOwner">Whether they are the legal owner.</param>
/// <param name="JobTitle">What they do for the seller.</param>
internal sealed record UpdateVendorStaffCommand(
    Guid? VendorId,
    Guid MembershipId,
    bool IsOwner,
    string? JobTitle) : ICommand<VendorStaffResponse>;

/// <summary>Takes a person off a seller's account.</summary>
/// <param name="VendorId">The seller, or null for the caller's own.</param>
/// <param name="MembershipId">The membership.</param>
internal sealed record RemoveVendorStaffCommand(Guid? VendorId, Guid MembershipId) : ICommand;

/// <summary>Rules for adding a member.</summary>
internal sealed class AddVendorStaffValidator : AbstractValidator<AddVendorStaffCommand>
{
    public AddVendorStaffValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.JobTitle!).MaximumLength(120).When(command => command.JobTitle is not null);
    }
}

/// <summary>Lists the people on a seller's account.</summary>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Finds the seller the caller may act on.</param>
internal sealed class ListVendorStaffQueryHandler(VendorsDbContext context, VendorScope scope)
    : IQueryHandler<ListVendorStaffQuery, IReadOnlyList<VendorStaffResponse>>
{
    public async Task<Result<IReadOnlyList<VendorStaffResponse>>> HandleAsync(
        ListVendorStaffQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var resolved = scope.Resolve(query.VendorId);

        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var members = await context.VendorUsers
            .AsNoTracking()
            .Where(member => member.VendorId == resolved.Value)
            .OrderByDescending(member => member.IsOwner)
            .ThenBy(member => member.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<VendorStaffResponse>>(
            members.ConvertAll(StaffProjection.ToResponse));
    }
}

/// <summary>
/// Adds a person to a seller's account.
/// </summary>
/// <remarks>
/// It records the membership; it does not create the account or grant the roles. Those are
/// Identity's, and the caller creates the user there first — which is why this takes a user id
/// rather than an email address. The <c>vendor_id</c> in that user's token comes from the role
/// grant Identity holds, and this row is what an operator reads to answer "who works for this
/// seller".
/// </remarks>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Finds the seller the caller may act on.</param>
/// <param name="audit">Records the addition. Access to a seller's account is worth auditing.</param>
internal sealed class AddVendorStaffCommandHandler(
    VendorsDbContext context,
    VendorScope scope,
    IAuditLogger audit) : ICommandHandler<AddVendorStaffCommand, VendorStaffResponse>
{
    /// <summary>The audited action for a new member.</summary>
    public const string AuditAction = "vendors.staff.added";

    /// <summary>The entity type recorded against a membership action.</summary>
    public const string AuditEntityType = "VendorUser";

    public async Task<Result<VendorStaffResponse>> HandleAsync(
        AddVendorStaffCommand command,
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

        var members = await context.VendorUsers
            .Where(member => member.VendorId == vendor.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (members.Exists(member => member.UserId == command.UserId))
        {
            return Error.Conflict(
                "VENDOR_STAFF_EXISTS",
                "That person is already on this seller's account.");
        }

        var member = VendorUser.Join(
            vendor.Id,
            command.UserId,
            // The first person on the account is always the owner. A seller with staff and no owner
            // is a seller nobody can be asked about a payout.
            command.IsOwner || members.Count == 0,
            command.JobTitle);

        if (member.IsOwner)
        {
            foreach (var other in members)
            {
                other.SetOwner(false);
            }
        }

        context.VendorUsers.Add(member);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AuditEntityType,
                EntityId = member.Id.ToString(),
                After = new { vendorId = vendor.Id, member.UserId, member.IsOwner, member.JobTitle },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(StaffProjection.ToResponse(member));
    }
}

/// <summary>Changes what a member is on the account.</summary>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Finds the seller the caller may act on.</param>
/// <param name="audit">Records a change of owner.</param>
internal sealed class UpdateVendorStaffCommandHandler(
    VendorsDbContext context,
    VendorScope scope,
    IAuditLogger audit) : ICommandHandler<UpdateVendorStaffCommand, VendorStaffResponse>
{
    /// <summary>The audited action for a change of owner.</summary>
    public const string AuditAction = "vendors.staff.owner-changed";

    public async Task<Result<VendorStaffResponse>> HandleAsync(
        UpdateVendorStaffCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var resolved = scope.Resolve(command.VendorId);

        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var members = await context.VendorUsers
            .Where(member => member.VendorId == resolved.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var member = members.Find(candidate => candidate.Id == command.MembershipId);

        if (member is null)
        {
            return VendorErrors.ChildNotFound("staff member");
        }

        var wasOwner = member.IsOwner;

        if (wasOwner && !command.IsOwner)
        {
            return VendorErrors.LastOne(
                "owner. Make somebody else the owner instead, which moves it in one step");
        }

        member.Describe(command.JobTitle);

        if (command.IsOwner && !wasOwner)
        {
            foreach (var other in members)
            {
                other.SetOwner(other.Id == member.Id);
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await audit.RecordAsync(
                new AuditEntry
                {
                    Action = AuditAction,
                    EntityType = AddVendorStaffCommandHandler.AuditEntityType,
                    EntityId = member.Id.ToString(),
                    Before = new { ownerUserId = members.Find(other => other.Id != member.Id && wasOwner)?.UserId },
                    After = new { vendorId = resolved.Value, ownerUserId = member.UserId },
                },
                cancellationToken).ConfigureAwait(false);

            return Result.Success(StaffProjection.ToResponse(member));
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(StaffProjection.ToResponse(member));
    }
}

/// <summary>Takes a person off a seller's account.</summary>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Finds the seller the caller may act on.</param>
/// <param name="audit">Records the removal.</param>
internal sealed class RemoveVendorStaffCommandHandler(
    VendorsDbContext context,
    VendorScope scope,
    IAuditLogger audit) : ICommandHandler<RemoveVendorStaffCommand>
{
    /// <summary>The audited action for a removed member.</summary>
    public const string AuditAction = "vendors.staff.removed";

    public async Task<Result> HandleAsync(RemoveVendorStaffCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var resolved = scope.Resolve(command.VendorId);

        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        var members = await context.VendorUsers
            .Where(member => member.VendorId == resolved.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var member = members.Find(candidate => candidate.Id == command.MembershipId);

        if (member is null)
        {
            return Result.Failure(VendorErrors.ChildNotFound("staff member"));
        }

        if (member.IsOwner)
        {
            return Result.Failure(VendorErrors.LastOne("owner. Make somebody else the owner first"));
        }

        context.VendorUsers.Remove(member);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Their Identity account and its roles are untouched, deliberately: this removes them from
        // the seller, and whether the account itself should be closed is a decision for whoever
        // manages users.
        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AddVendorStaffCommandHandler.AuditEntityType,
                EntityId = member.Id.ToString(),
                Before = new { vendorId = resolved.Value, member.UserId },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Maps a membership onto its API shape.</summary>
internal static class StaffProjection
{
    /// <summary>Builds the response.</summary>
    /// <param name="member">The membership.</param>
    public static VendorStaffResponse ToResponse(VendorUser member)
    {
        ArgumentNullException.ThrowIfNull(member);

        return new VendorStaffResponse(
            member.Id,
            member.VendorId,
            member.UserId,
            member.IsOwner,
            member.JobTitle,
            member.CreatedAt);
    }
}
