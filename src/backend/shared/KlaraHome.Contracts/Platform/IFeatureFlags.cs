namespace KlaraHome.Contracts.Platform;

/// <summary>
/// Asks whether a feature is on for the caller. A flag is a switch an operator can throw without a
/// deploy; it is not a permission check and it is not configuration a developer reads once at
/// startup.
/// </summary>
/// <remarks>
/// An unknown key is <b>off</b>. That makes the safe direction the default one: a typo in a flag
/// name hides a feature rather than exposing an unfinished one.
/// </remarks>
public interface IFeatureFlags
{
    /// <summary>Whether <paramref name="key"/> is on for <paramref name="audience"/>.</summary>
    /// <param name="key">The flag key, for example <c>platform.public-store-config</c>.</param>
    /// <param name="audience">Who is asking. Determines percentage and allow-list rollout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<bool> IsEnabledAsync(
        string key,
        FeatureAudience audience = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every known flag and whether it is on for <paramref name="audience"/>. Used to hand the
    /// storefront one map rather than making it ask flag by flag.
    /// </summary>
    /// <param name="audience">Who is asking.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IReadOnlyDictionary<string, bool>> GetAllAsync(
        FeatureAudience audience = default,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Who a flag is being evaluated for. Default is an anonymous caller, which sees only flags that
/// are on for everyone.
/// </summary>
/// <param name="UserId">
/// The authenticated subject, when there is one. A percentage rollout is stable per user: the same
/// user gets the same answer on every request, so a feature does not flicker between page loads.
/// </param>
/// <param name="Segment">
/// A named cohort the caller belongs to, for a rollout aimed at a group rather than a percentage.
/// Populated from Step 7, when there are roles to belong to.
/// </param>
public readonly record struct FeatureAudience(Guid? UserId = null, string? Segment = null);
