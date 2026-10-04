using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Identity.Infrastructure.Auditing;

/// <summary>
/// Names users on the audit trail, as the target of an entry and — through the same lookup — as
/// the actor of every entry.
/// </summary>
/// <remarks>
/// Staff are labelled by email, which is what they sign in with and what a colleague recognises.
/// A shopper is labelled by their name when they gave one and by email or mobile when they did not,
/// the same fallback <c>CustomerDirectory</c> uses. Two reads for the whole page — users, then the
/// profiles of those who are customers — never one per entry.
/// </remarks>
/// <param name="context">The Identity data context.</param>
internal sealed class UserAuditLabels(IdentityDbContext context) : IAuditLabelSource
{
    /// <summary>The entity type the sign-in and user-management audit entries carry.</summary>
    public const string UserEntityType = "User";

    /// <inheritdoc />
    public string EntityType => UserEntityType;

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> LabelsAsync(
        IReadOnlyCollection<string> entityIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityIds);

        var ids = entityIds
            .Select(id => Guid.TryParse(id, out var parsed) ? parsed : (Guid?)null)
            .OfType<Guid>()
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return new Dictionary<string, string>();
        }

        var users = await context.Users
            .AsNoTracking()
            .Where(user => ids.Contains(user.Id))
            .Select(user => new { user.Id, user.Email, user.Mobile })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var names = await context.CustomerProfiles
            .AsNoTracking()
            .Where(profile => ids.Contains(profile.UserId))
            .Select(profile => new { profile.UserId, profile.FirstName, profile.LastName })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var named = names.ToDictionary(
            profile => profile.UserId,
            profile => string.Join(' ', new[] { profile.FirstName, profile.LastName }
                .Where(part => !string.IsNullOrWhiteSpace(part))).Trim());

        var labels = new Dictionary<string, string>(users.Count);

        foreach (var user in users)
        {
            var label = named.GetValueOrDefault(user.Id);

            if (string.IsNullOrWhiteSpace(label))
            {
                label = user.Email ?? user.Mobile;
            }

            if (!string.IsNullOrWhiteSpace(label))
            {
                labels[user.Id.ToString()] = label;
            }
        }

        return labels;
    }
}
