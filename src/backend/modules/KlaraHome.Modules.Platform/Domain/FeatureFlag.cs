using System.Security.Cryptography;
using System.Text;
using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Platform.Domain;

/// <summary>
/// A switch an operator can throw without a deploy (docs/03-database-design.md §4.1). The master
/// switch is <see cref="Enabled"/>; <see cref="Rollout"/> narrows it to part of the audience.
/// </summary>
internal sealed class FeatureFlag : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private FeatureFlag(Guid id, string key, bool enabled, FeatureRollout rollout, string description)
        : base(id)
    {
        Key = Guard.NotNullOrWhiteSpace(key);
        Enabled = enabled;
        Rollout = rollout;
        Description = description;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private FeatureFlag()
    {
        Key = string.Empty;
        Description = string.Empty;
        Rollout = FeatureRollout.Everyone;
    }

    /// <summary>Dotted lowercase key, unique within the tenant.</summary>
    public string Key { get; private set; }

    /// <summary>The master switch. Off means off for everybody, whatever the rollout says.</summary>
    public bool Enabled { get; private set; }

    /// <summary>Who, among the audience, the feature is on for while it is enabled.</summary>
    public FeatureRollout Rollout { get; private set; }

    /// <summary>What the flag controls, so the admin UI is not a list of bare keys.</summary>
    public string Description { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; private set; }

    /// <summary>Declares a flag.</summary>
    /// <param name="key">The dotted key.</param>
    /// <param name="enabled">Whether it starts on.</param>
    /// <param name="description">What it controls.</param>
    /// <param name="rollout">Who it is on for; everyone by default.</param>
    public static FeatureFlag Declare(string key, bool enabled, string description, FeatureRollout? rollout = null)
        => new(UuidV7.New(), key, enabled, rollout ?? FeatureRollout.Everyone, description ?? string.Empty);

    /// <summary>Sets the master switch and the rollout in one operation, as the admin UI edits them.</summary>
    /// <param name="enabled">The new master switch value.</param>
    /// <param name="rollout">The new rollout.</param>
    public void Configure(bool enabled, FeatureRollout rollout)
    {
        ArgumentNullException.ThrowIfNull(rollout);

        Enabled = enabled;
        Rollout = rollout;
    }

    /// <summary>Updates the operator-facing description.</summary>
    /// <param name="description">What the flag controls.</param>
    public void Describe(string description) => Description = description ?? string.Empty;

    /// <summary>Whether this flag is on for the given caller.</summary>
    /// <param name="userId">The authenticated subject, or null for an anonymous caller.</param>
    /// <param name="segment">A named cohort the caller belongs to, or null.</param>
    public bool IsEnabledFor(Guid? userId, string? segment)
        => Enabled && Rollout.Includes(Key, userId, segment);
}

/// <summary>
/// Who a flag reaches while it is enabled: everyone, a percentage of signed-in users, a named
/// cohort, or an explicit allow list.
/// </summary>
/// <remarks>
/// Stored as the <c>rollout</c> <c>jsonb</c> column. Adding a rollout dimension is a change to this
/// record and to the evaluation below, never a migration.
/// </remarks>
internal sealed record FeatureRollout
{
    /// <summary>The rollout that includes every caller. The default for a newly declared flag.</summary>
    public static readonly FeatureRollout Everyone = new();

    /// <summary>
    /// Share of signed-in users the feature reaches, 0-100. Anonymous callers are excluded by any
    /// value below 100: there is no stable identity to bucket them by, and a feature that flickers
    /// between page loads is worse than one that is simply off.
    /// </summary>
    public int Percentage { get; init; } = 100;

    /// <summary>Users the feature is on for regardless of the percentage. The internal-testers list.</summary>
    public IReadOnlyList<Guid> UserIds { get; init; } = [];

    /// <summary>Named cohorts the feature is on for regardless of the percentage.</summary>
    public IReadOnlyList<string> Segments { get; init; } = [];

    /// <summary>Whether this rollout includes the given caller.</summary>
    /// <param name="key">The flag key, so two flags at 25% do not select the same quarter of users.</param>
    /// <param name="userId">The authenticated subject, or null.</param>
    /// <param name="segment">A named cohort the caller belongs to, or null.</param>
    public bool Includes(string key, Guid? userId, string? segment)
    {
        if (userId is not null && UserIds.Contains(userId.Value))
        {
            return true;
        }

        if (segment is not null && Segments.Contains(segment, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (Percentage >= 100)
        {
            return true;
        }

        if (Percentage <= 0 || userId is null)
        {
            return false;
        }

        return BucketOf(key, userId.Value) < Percentage;
    }

    /// <summary>
    /// A stable bucket in [0, 100) for a (flag, user) pair. SHA-256 rather than
    /// <see cref="object.GetHashCode"/>, which is randomised per process and would put a user in a
    /// different bucket on every replica.
    /// </summary>
    /// <param name="key">The flag key.</param>
    /// <param name="userId">The user being bucketed.</param>
    internal static int BucketOf(string key, Guid userId)
    {
        var keyLength = Encoding.UTF8.GetByteCount(key);
        Span<byte> input = keyLength <= 128 ? stackalloc byte[16 + keyLength] : new byte[16 + keyLength];

        userId.TryWriteBytes(input[..16], bigEndian: true, out _);
        Encoding.UTF8.GetBytes(key, input[16..]);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);

        var value = BitConverter.ToUInt32(hash[..4]);
        return (int)(value % 100);
    }
}
