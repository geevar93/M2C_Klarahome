using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Content.Application;
using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Infrastructure.Features;
using KlaraHome.Modules.Content.Infrastructure.Rendering;
using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Modules.Content.Infrastructure.Blocks;

/// <summary>
/// Turns the blocks a caller sent into the blocks a page will hold.
/// </summary>
/// <remarks>
/// <para>
/// One place, called by the page save and by the rollback, so the two cannot disagree about what a
/// valid block is. That matters more than it looks: a rollback restores content that was valid when
/// it was written, and if the schema has tightened since, the restore has to be refused with the same
/// message the editor would have got — not written back and rendered as a broken page.
/// </para>
/// <para>
/// Custom HTML is checked here rather than at the endpoint because it is a property of the
/// <em>payload</em> and not of the route. An editor saving a page that happens to contain one
/// custom-HTML block among twelve has to be refused; the endpoint cannot know that from its metadata.
/// </para>
/// </remarks>
/// <param name="flags">Whether this deployment permits custom HTML at all.</param>
internal sealed class PageBlockBinder(IFeatureFlags flags)
{
    /// <summary>
    /// Validates a page's blocks and builds them.
    /// </summary>
    /// <param name="bodies">The blocks, in the order they should render.</param>
    /// <param name="canWriteCustomHtml">Whether this caller holds the custom-HTML permission.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The blocks, or the failure that names every field that is wrong.</returns>
    public async Task<Result<List<BlockDraft>>> BindAsync(
        IReadOnlyList<BlockBody> bodies,
        bool canWriteCustomHtml,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bodies);

        if (bodies.Count > ContentPage.MaxBlocks)
        {
            return Result.Failure<List<BlockDraft>>(ContentErrors.TooManyBlocks(ContentPage.MaxBlocks));
        }

        var customHtmlAllowed = canWriteCustomHtml
                                && await flags
                                    .IsEnabledAsync(ContentFeatures.CustomHtml, cancellationToken: cancellationToken)
                                    .ConfigureAwait(false);

        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var blocks = new List<BlockDraft>(bodies.Count);

        for (var index = 0; index < bodies.Count; index++)
        {
            var body = bodies[index];
            var path = $"blocks[{index}]";

            var descriptor = BlockCatalog.Find(body.Type);

            if (descriptor is null)
            {
                return Result.Failure<List<BlockDraft>>(
                    ContentErrors.UnknownBlockType(body.Type ?? string.Empty));
            }

            if (descriptor.IsPrivileged && !customHtmlAllowed)
            {
                return Result.Failure<List<BlockDraft>>(ContentErrors.CustomHtmlNotAllowed);
            }

            if (body.EndsAt is not null && body.StartsAt is not null && body.EndsAt <= body.StartsAt)
            {
                return Result.Failure<List<BlockDraft>>(ContentErrors.InvalidWindow);
            }

            var validated = BlockValidator.Validate(descriptor.Type, body.Config, path, errors);

            if (validated is null)
            {
                continue;
            }

            blocks.Add(new BlockDraft(
                body.Id is { } id && id != Guid.Empty ? id : null,
                descriptor.Type,
                validated.Config,
                body.IsVisible,
                body.StartsAt,
                body.EndsAt));
        }

        if (errors.Count > 0)
        {
            return Result.Failure<List<BlockDraft>>(ContentErrors.InvalidBlock(
                errors.ToDictionary(
                    entry => entry.Key,
                    entry => (IReadOnlyList<string>)entry.Value,
                    StringComparer.Ordinal)));
        }

        return Result.Success(blocks);
    }

    /// <summary>
    /// Rebuilds a page's blocks from a version snapshot.
    /// </summary>
    /// <remarks>
    /// Validated again, on purpose. A snapshot is content that was valid when it was written, and a
    /// build in which a block type has since gained a required field would otherwise restore a block
    /// the current storefront cannot render. Refusing the rollback with the field named is a problem
    /// an editor can solve; a page that renders as a gap is one they report as a bug.
    /// </remarks>
    /// <param name="snapshots">The blocks as they stood.</param>
    /// <param name="canWriteCustomHtml">Whether this caller holds the custom-HTML permission.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<Result<List<BlockDraft>>> BindSnapshotAsync(
        IReadOnlyList<BlockSnapshot> snapshots,
        bool canWriteCustomHtml,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        var bodies = snapshots
            .OrderBy(snapshot => snapshot.Position)
            .Select(snapshot => new BlockBody(
                snapshot.Id,
                snapshot.Type,
                snapshot.Config,
                snapshot.IsVisible,
                snapshot.StartsAt,
                snapshot.EndsAt))
            .ToList();

        return BindAsync(bodies, canWriteCustomHtml, cancellationToken);
    }
}
