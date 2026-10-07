using KlaraHome.Contracts.Media;
using KlaraHome.Modules.Media.Domain;
using KlaraHome.Modules.Media.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Media.Infrastructure.Seeding;

/// <summary>
/// Reads back the images <see cref="DemoMediaSeeder"/> stored, keyed by the name they were uploaded as.
/// </summary>
/// <param name="context">The Media data context.</param>
internal sealed class DemoMediaCatalogue(MediaDbContext context) : IDemoMediaCatalogue
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, Guid>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context.Files
            .AsNoTracking()
            .Where(file => file.OwnerType == DemoMediaSeeder.OwnerType && file.Status == StoredFileStatus.Ready)
            .OrderBy(file => file.CreatedAt)
            .Select(file => new { file.Id, file.FileName })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // The name is the key plus the extension the inspector chose. Latest wins on a duplicate,
        // which can only happen if somebody re-uploaded one by hand under the same name.
        var result = new Dictionary<string, Guid>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            result[Path.GetFileNameWithoutExtension(row.FileName)] = row.Id;
        }

        return result;
    }
}
