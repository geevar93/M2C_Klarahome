using FluentValidation;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.Persistence.Seeding;
using KlaraHome.Modules.Returns.Domain;
using KlaraHome.Modules.Returns.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Returns.Application.Reasons;

/// <summary>The reason codes, as an operator sees them.</summary>
/// <param name="IncludeInactive">Whether withdrawn reasons are listed too.</param>
internal sealed record ListReturnReasonsQuery(bool IncludeInactive)
    : IQuery<IReadOnlyList<ReturnReasonResponse>>;

/// <summary>Opens a reason code.</summary>
/// <param name="Code">Its stable code.</param>
/// <param name="Label">What the shopper reads.</param>
/// <param name="Description">The help beneath it.</param>
/// <param name="SortOrder">Where it sits on the dropdown.</param>
/// <param name="RequiresEvidence">Whether a photograph is needed.</param>
/// <param name="IsPickupRequired">Whether a courier collects.</param>
/// <param name="RequiresQc">Whether the goods are inspected.</param>
/// <param name="IsAutoApproved">Whether it is approved without a human.</param>
/// <param name="ShippingPayer">Who pays the reverse freight.</param>
/// <param name="IsVendorFault">Whether the seller bears the cost at settlement.</param>
/// <param name="AllowsReplacement">Whether a replacement may be asked for.</param>
internal sealed record CreateReturnReasonCommand(
    string Code,
    string Label,
    string? Description,
    int SortOrder,
    bool RequiresEvidence,
    bool IsPickupRequired,
    bool RequiresQc,
    bool IsAutoApproved,
    string? ShippingPayer,
    bool IsVendorFault,
    bool AllowsReplacement) : ICommand<ReturnReasonResponse>;

/// <summary>Edits a reason code. The code itself is never changed.</summary>
/// <param name="ReasonId">The reason.</param>
/// <param name="Label">What the shopper reads.</param>
/// <param name="Description">The help beneath it.</param>
/// <param name="SortOrder">Where it sits on the dropdown.</param>
/// <param name="IsActive">Whether it is offered.</param>
/// <param name="RequiresEvidence">Whether a photograph is needed.</param>
/// <param name="IsPickupRequired">Whether a courier collects.</param>
/// <param name="RequiresQc">Whether the goods are inspected.</param>
/// <param name="IsAutoApproved">Whether it is approved without a human.</param>
/// <param name="ShippingPayer">Who pays the reverse freight.</param>
/// <param name="IsVendorFault">Whether the seller bears the cost at settlement.</param>
/// <param name="AllowsReplacement">Whether a replacement may be asked for.</param>
internal sealed record UpdateReturnReasonCommand(
    Guid ReasonId,
    string Label,
    string? Description,
    int SortOrder,
    bool IsActive,
    bool RequiresEvidence,
    bool IsPickupRequired,
    bool RequiresQc,
    bool IsAutoApproved,
    string? ShippingPayer,
    bool IsVendorFault,
    bool AllowsReplacement) : ICommand<ReturnReasonResponse>;

/// <summary>Validates a new reason code.</summary>
internal sealed class CreateReturnReasonValidator : AbstractValidator<CreateReturnReasonCommand>
{
    public CreateReturnReasonValidator()
    {
        // Lower-case, hyphenated, and never anything else. A code appears in every return raised
        // under it for the life of the platform, and a code that differs from another only by case
        // would be two policies nobody could tell apart.
        RuleFor(command => command.Code)
            .NotEmpty()
            .MaximumLength(64)
            .Matches("^[a-z0-9]+(-[a-z0-9]+)*$")
            .WithMessage("A reason code is lower-case letters, digits and hyphens — for example damaged-in-transit.");

        RuleFor(command => command.Label).NotEmpty().MaximumLength(160);
        RuleFor(command => command.Description).MaximumLength(500);
        RuleFor(command => command.SortOrder).InclusiveBetween(0, 9999);
        RuleFor(command => command.ShippingPayer).Must(BeAPayer).WithMessage(PayerMessage);
    }

    internal static bool BeAPayer(string? value)
        => string.IsNullOrWhiteSpace(value)
           || Enum.TryParse<ReturnShippingPayer>(value, ignoreCase: true, out _);

    internal const string PayerMessage =
        "Who pays for a return must be one of: Platform, Vendor, Customer.";
}

/// <summary>Validates an edit.</summary>
internal sealed class UpdateReturnReasonValidator : AbstractValidator<UpdateReturnReasonCommand>
{
    public UpdateReturnReasonValidator()
    {
        RuleFor(command => command.Label).NotEmpty().MaximumLength(160);
        RuleFor(command => command.Description).MaximumLength(500);
        RuleFor(command => command.SortOrder).InclusiveBetween(0, 9999);

        RuleFor(command => command.ShippingPayer)
            .Must(CreateReturnReasonValidator.BeAPayer)
            .WithMessage(CreateReturnReasonValidator.PayerMessage);
    }
}

/// <summary>Lists the reason codes in the order a shopper sees them.</summary>
/// <param name="context">The Returns data context.</param>
internal sealed class ListReturnReasonsQueryHandler(ReturnsDbContext context)
    : IQueryHandler<ListReturnReasonsQuery, IReadOnlyList<ReturnReasonResponse>>
{
    public async Task<Result<IReadOnlyList<ReturnReasonResponse>>> HandleAsync(
        ListReturnReasonsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var rows = context.Reasons.AsNoTracking().AsQueryable();

        if (!query.IncludeInactive)
        {
            rows = rows.Where(reason => reason.IsActive);
        }

        var reasons = await rows
            .OrderBy(reason => reason.SortOrder)
            .ThenBy(reason => reason.Label)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<ReturnReasonResponse>>(
            [.. reasons.Select(ReturnProjection.ToReason)]);
    }
}

/// <summary>Opens a reason code.</summary>
/// <param name="context">The Returns data context.</param>
internal sealed class CreateReturnReasonCommandHandler(ReturnsDbContext context)
    : ICommandHandler<CreateReturnReasonCommand, ReturnReasonResponse>
{
    public async Task<Result<ReturnReasonResponse>> HandleAsync(
        CreateReturnReasonCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var code = command.Code.Trim().ToLowerInvariant();

        var exists = await context.Reasons
            .AnyAsync(reason => reason.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            return Result.Failure<ReturnReasonResponse>(ReturnsErrors.DuplicateReason);
        }

        var reason = ReturnReason.Create(code, command.Label);

        reason.Describe(command.Label, command.Description, command.SortOrder);

        reason.Govern(
            command.RequiresEvidence,
            command.IsPickupRequired,
            command.RequiresQc,
            command.IsAutoApproved,
            ParsePayer(command.ShippingPayer),
            command.IsVendorFault,
            command.AllowsReplacement);

        context.Reasons.Add(reason);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ReturnProjection.ToReason(reason));
    }

    internal static ReturnShippingPayer ParsePayer(string? value)
        => Enum.TryParse<ReturnShippingPayer>(value, ignoreCase: true, out var parsed)
            ? parsed
            : ReturnShippingPayer.Platform;
}

/// <summary>
/// Edits a reason code.
/// </summary>
/// <remarks>
/// The code is deliberately not editable. It is written onto every return raised under it and is
/// what a report groups by; changing it would silently re-file history. A reason that turns out to
/// be wrong is withdrawn and a new one opened.
/// </remarks>
/// <param name="context">The Returns data context.</param>
internal sealed class UpdateReturnReasonCommandHandler(ReturnsDbContext context)
    : ICommandHandler<UpdateReturnReasonCommand, ReturnReasonResponse>
{
    public async Task<Result<ReturnReasonResponse>> HandleAsync(
        UpdateReturnReasonCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var reason = await context.Reasons
            .FirstOrDefaultAsync(candidate => candidate.Id == command.ReasonId, cancellationToken)
            .ConfigureAwait(false);

        if (reason is null)
        {
            return Result.Failure<ReturnReasonResponse>(ReturnsErrors.NotFound("reason"));
        }

        reason.Describe(command.Label, command.Description, command.SortOrder);
        reason.SetActive(command.IsActive);

        reason.Govern(
            command.RequiresEvidence,
            command.IsPickupRequired,
            command.RequiresQc,
            command.IsAutoApproved,
            CreateReturnReasonCommandHandler.ParsePayer(command.ShippingPayer),
            command.IsVendorFault,
            command.AllowsReplacement);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ReturnProjection.ToReason(reason));
    }
}

/// <summary>
/// Puts the reason codes an Indian marketplace needs into a fresh deployment.
/// </summary>
/// <remarks>
/// <para>
/// The list every marketplace converges on, with the policy a consumer forum would expect attached
/// to each: a fault the seller caused is collected free and inspected, and a change of mind is
/// collected at the shopper's cost. All of it is editable on the day it is wrong.
/// </para>
/// <para>
/// Idempotent, like every seeder: it inserts only the codes that are missing, so a deployment that
/// has withdrawn "changed mind" does not find it back after the next migration.
/// </para>
/// </remarks>
/// <param name="context">The Returns data context.</param>
internal sealed class ReturnReasonSeeder(ReturnsDbContext context) : IDataSeeder
{
    /// <inheritdoc />
    public string Name => "Return reasons";

    /// <inheritdoc />
    public int Order => 320;

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var existing = await context.Reasons
            .IgnoreQueryFilters()
            .Select(reason => reason.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var known = existing.ToHashSet(StringComparer.Ordinal);
        var added = false;

        foreach (var seed in Defaults)
        {
            if (!known.Add(seed.Code))
            {
                continue;
            }

            var reason = ReturnReason.Create(seed.Code, seed.Label);

            reason.Describe(seed.Label, seed.Description, seed.SortOrder);

            reason.Govern(
                seed.RequiresEvidence,
                seed.IsPickupRequired,
                seed.RequiresQc,
                seed.IsAutoApproved,
                seed.ShippingPayer,
                seed.IsVendorFault,
                seed.AllowsReplacement);

            context.Reasons.Add(reason);
            added = true;
        }

        if (added)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>One seeded reason.</summary>
    private sealed record Seed(
        string Code,
        string Label,
        string? Description,
        int SortOrder,
        bool RequiresEvidence,
        bool IsPickupRequired,
        bool RequiresQc,
        bool IsAutoApproved,
        ReturnShippingPayer ShippingPayer,
        bool IsVendorFault,
        bool AllowsReplacement);

    /// <summary>The reasons a fresh deployment starts with.</summary>
    private static readonly IReadOnlyList<Seed> Defaults =
    [
        new(
            ReturnReasonCodes.DamagedInTransit,
            "Damaged in transit",
            "The parcel or what was inside it arrived broken.",
            10,
            RequiresEvidence: true,
            IsPickupRequired: true,
            RequiresQc: true,
            IsAutoApproved: false,
            ReturnShippingPayer.Platform,
            IsVendorFault: true,
            AllowsReplacement: true),
        new(
            ReturnReasonCodes.WrongItem,
            "Wrong item sent",
            "What arrived is not what was ordered.",
            20,
            RequiresEvidence: true,
            IsPickupRequired: true,
            RequiresQc: true,
            IsAutoApproved: false,
            ReturnShippingPayer.Vendor,
            IsVendorFault: true,
            AllowsReplacement: true),
        new(
            ReturnReasonCodes.MissingItem,
            "Something was missing",
            "Part of the order was not in the parcel.",
            30,
            RequiresEvidence: true,
            IsPickupRequired: false,
            RequiresQc: false,
            IsAutoApproved: false,
            ReturnShippingPayer.Vendor,
            IsVendorFault: true,
            AllowsReplacement: true),
        new(
            ReturnReasonCodes.Defective,
            "It does not work",
            "The item is faulty.",
            40,
            RequiresEvidence: true,
            IsPickupRequired: true,
            RequiresQc: true,
            IsAutoApproved: false,
            ReturnShippingPayer.Vendor,
            IsVendorFault: true,
            AllowsReplacement: true),
        new(
            ReturnReasonCodes.NotAsDescribed,
            "Not as described",
            "The item is not what the listing said it was.",
            50,
            RequiresEvidence: true,
            IsPickupRequired: true,
            RequiresQc: true,
            IsAutoApproved: false,
            ReturnShippingPayer.Vendor,
            IsVendorFault: true,
            AllowsReplacement: true),
        new(
            ReturnReasonCodes.SizeIssue,
            "Wrong size or fit",
            "The size or the dimensions are not right.",
            60,
            RequiresEvidence: false,
            IsPickupRequired: true,
            RequiresQc: true,
            IsAutoApproved: false,
            ReturnShippingPayer.Platform,
            IsVendorFault: false,
            AllowsReplacement: true),
        new(
            ReturnReasonCodes.ChangedMind,
            "I changed my mind",
            "No longer wanted.",
            70,
            RequiresEvidence: false,
            IsPickupRequired: true,
            RequiresQc: true,
            IsAutoApproved: false,
            ReturnShippingPayer.Customer,
            IsVendorFault: false,
            AllowsReplacement: false),
        new(
            ReturnReasonCodes.LateDelivery,
            "It arrived too late",
            "The delivery was late enough that it was no longer wanted.",
            80,
            RequiresEvidence: false,
            IsPickupRequired: true,
            RequiresQc: true,
            IsAutoApproved: false,
            ReturnShippingPayer.Platform,
            IsVendorFault: false,
            AllowsReplacement: false),
    ];
}
