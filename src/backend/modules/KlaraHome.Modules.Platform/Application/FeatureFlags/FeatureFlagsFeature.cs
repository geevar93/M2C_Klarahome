using FluentValidation;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Platform.Domain;
using KlaraHome.Modules.Platform.Infrastructure.FeatureFlags;
using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Modules.Platform.Application.FeatureFlags;

/// <summary>A flag and its configuration, as the admin surface shows it.</summary>
/// <param name="Key">The dotted key.</param>
/// <param name="Enabled">The master switch.</param>
/// <param name="Description">What the flag controls.</param>
/// <param name="Rollout">Who it reaches while it is enabled.</param>
internal sealed record FeatureFlagResponse(string Key, bool Enabled, string Description, RolloutModel Rollout);

/// <summary>The rollout, in the shape the API accepts and returns.</summary>
/// <param name="Percentage">Share of signed-in users the flag reaches, 0-100.</param>
/// <param name="UserIds">Users it reaches regardless of the percentage.</param>
/// <param name="Segments">Named cohorts it reaches regardless of the percentage.</param>
internal sealed record RolloutModel(int Percentage, IReadOnlyList<Guid> UserIds, IReadOnlyList<string> Segments)
{
    /// <summary>The rollout that reaches everybody.</summary>
    public static RolloutModel Everyone { get; } = new(100, [], []);

    /// <summary>Maps a stored rollout onto the API shape.</summary>
    /// <param name="rollout">The stored rollout.</param>
    public static RolloutModel From(FeatureRollout rollout)
    {
        ArgumentNullException.ThrowIfNull(rollout);
        return new RolloutModel(rollout.Percentage, rollout.UserIds, rollout.Segments);
    }

    /// <summary>Maps the API shape onto a stored rollout.</summary>
    public FeatureRollout ToDomain()
        => new() { Percentage = Percentage, UserIds = UserIds ?? [], Segments = Segments ?? [] };
}

/// <summary>Lists every declared flag with its current configuration.</summary>
internal sealed record GetFeatureFlagsQuery : IQuery<IReadOnlyList<FeatureFlagResponse>>;

/// <summary>Turns one flag on or off, and sets who it reaches.</summary>
/// <param name="Key">The flag key. Must already be declared by a module.</param>
/// <param name="Enabled">The new master switch value.</param>
/// <param name="Rollout">The new rollout. Null means "everyone".</param>
/// <param name="Description">A new description, or null to leave it as the module declared it.</param>
internal sealed record UpdateFeatureFlagCommand(
    string Key,
    bool Enabled,
    RolloutModel? Rollout,
    string? Description) : ICommand<FeatureFlagResponse>;

/// <summary>Rules for a flag change.</summary>
internal sealed class UpdateFeatureFlagCommandValidator : AbstractValidator<UpdateFeatureFlagCommand>
{
    public UpdateFeatureFlagCommandValidator()
    {
        RuleFor(command => command.Key)
            .NotEmpty()
            .MaximumLength(128);

        RuleFor(command => command.Rollout!.Percentage)
            .InclusiveBetween(0, 100)
            .When(command => command.Rollout is not null)
            .WithMessage("Rollout percentage must be between 0 and 100.");

        RuleFor(command => command.Description)
            .MaximumLength(500)
            .When(command => command.Description is not null);
    }
}

/// <param name="flags">The flag service.</param>
internal sealed class GetFeatureFlagsQueryHandler(FeatureFlagService flags)
    : IQueryHandler<GetFeatureFlagsQuery, IReadOnlyList<FeatureFlagResponse>>
{
    public async Task<Result<IReadOnlyList<FeatureFlagResponse>>> HandleAsync(
        GetFeatureFlagsQuery query,
        CancellationToken cancellationToken)
    {
        var snapshots = await flags.ListAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<FeatureFlagResponse> response =
        [
            .. snapshots.Select(flag => new FeatureFlagResponse(
                flag.Key,
                flag.Enabled,
                flag.Description,
                RolloutModel.From(flag.Rollout))),
        ];

        return Result.Success(response);
    }
}

/// <summary>
/// Applies a flag change and audits it.
/// </summary>
/// <remarks>
/// A flag can only be reconfigured, never created here. Flags are declared in code by the module
/// that reads them, so an admin UI that could invent one would be creating a switch nothing is
/// wired to — which looks exactly like a switch that does not work.
/// </remarks>
/// <param name="flags">The flag service.</param>
/// <param name="audit">The audit trail.</param>
internal sealed class UpdateFeatureFlagCommandHandler(FeatureFlagService flags, IAuditLogger audit)
    : ICommandHandler<UpdateFeatureFlagCommand, FeatureFlagResponse>
{
    /// <summary>The action recorded in the audit trail for a flag change.</summary>
    public const string AuditAction = "platform.feature-flag.updated";

    /// <summary>The entity type recorded against that action.</summary>
    public const string AuditEntityType = "FeatureFlag";

    public async Task<Result<FeatureFlagResponse>> HandleAsync(
        UpdateFeatureFlagCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var rollout = (command.Rollout ?? RolloutModel.Everyone).ToDomain();

        var before = await flags
            .ConfigureAsync(command.Key, command.Enabled, rollout, command.Description, cancellationToken)
            .ConfigureAwait(false);

        if (before is null)
        {
            return Error.NotFound(
                "FEATURE_FLAG_UNKNOWN",
                $"There is no feature flag named '{command.Key}'. Flags are declared in code by the module "
                + "that reads them.");
        }

        var after = new FeatureFlagResponse(
            command.Key,
            command.Enabled,
            command.Description ?? before.Description,
            RolloutModel.From(rollout));

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AuditEntityType,
                EntityId = command.Key,
                Before = new FeatureFlagResponse(
                    before.Key,
                    before.Enabled,
                    before.Description,
                    RolloutModel.From(before.Rollout)),
                After = after,
                ActorType = AuditActorType.StaffUser,
            },
            cancellationToken).ConfigureAwait(false);

        return after;
    }
}
