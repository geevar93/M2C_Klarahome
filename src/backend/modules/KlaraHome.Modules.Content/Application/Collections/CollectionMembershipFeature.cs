using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Infrastructure.Collections;
using KlaraHome.Modules.Content.Infrastructure.Persistence;
using KlaraHome.Modules.Content.Infrastructure.Rendering;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Content.Application.Collections;

/// <summary>Adds one product to a collection's hand-picked membership.</summary>
/// <param name="Id">The collection.</param>
/// <param name="ProductId">The product to add.</param>
/// <param name="IsPinned">Whether it is fixed where it lands.</param>
internal sealed record AddCollectionItemCommand(Guid Id, Guid ProductId, bool IsPinned)
    : ICommand<CollectionResponse>;

/// <summary>Removes one product from a collection's hand-picked membership.</summary>
/// <param name="Id">The collection.</param>
/// <param name="ProductId">The product to remove.</param>
internal sealed record RemoveCollectionItemCommand(Guid Id, Guid ProductId) : ICommand<CollectionResponse>;

/// <summary>
/// Adds one product to a collection, without touching the rest.
/// </summary>
/// <remarks>
/// <para>
/// The whole-list <c>PUT</c> beside this one is the right shape for reordering and nothing else.
/// The back office has one screen of rows in hand — fifty of them — and sending those fifty back to
/// add a fifty-first deletes every row the screen had not loaded. That is not a hypothetical: it is
/// what pinning the fifty-first product did (Step 28B, deliverable 9).
/// </para>
/// <para>
/// A product already in the collection is not an error. Adding it again re-pins it where it is,
/// which is what somebody pressing "add" on a row they cannot see means, and refusing would make
/// the screen ask whether it is there first.
/// </para>
/// </remarks>
/// <param name="context">The Content data context.</param>
/// <param name="renderer">Resolves images for the response.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class AddCollectionItemCommandHandler(
    ContentDbContext context,
    ContentRenderer renderer,
    IClock clock) : ICommandHandler<AddCollectionItemCommand, CollectionResponse>
{
    public async Task<Result<CollectionResponse>> HandleAsync(
        AddCollectionItemCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ProductId == Guid.Empty)
        {
            return Result.Failure<CollectionResponse>(ContentErrors.NotFound("product"));
        }

        var collection = await context.Collections
            .FirstOrDefaultAsync(row => row.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (collection is null)
        {
            return Result.Failure<CollectionResponse>(ContentErrors.NotFound("collection"));
        }

        var items = await context.CollectionItems
            .Where(item => item.CollectionId == collection.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existing = items.Find(item => item.ProductId == command.ProductId);
        var now = clock.UtcNow;

        if (existing is not null)
        {
            existing.MoveTo(existing.Position, command.IsPinned);
        }
        else
        {
            if (items.Count >= CollectionRuleSet.MaxLimit)
            {
                return Result.Failure<CollectionResponse>(
                    ContentErrors.CollectionFull(CollectionRuleSet.MaxLimit));
            }

            // Appended, not inserted. A new pick belongs at the end until somebody orders it;
            // putting it first would silently demote whatever the merchandiser had put there.
            var position = items.Count == 0 ? 0 : items.Max(item => item.Position) + 1;

            context.CollectionItems.Add(CollectionItem.Create(
                collection.Id,
                command.ProductId,
                position,
                command.IsPinned,
                isFromRule: false,
                now));

            collection.RecordMembership(items.Count + 1, now);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var response = await CollectionReader
            .ToResponseAsync(collection, renderer, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(response);
    }
}

/// <summary>
/// Removes one product from a collection, without touching the rest.
/// </summary>
/// <remarks>
/// A row a rule put there is not removable by hand: it would come back on the next evaluation, and
/// a button whose effect is undone by a background sweep is worse than no button. Removing it means
/// changing the rule, which is a different screen and a different decision.
/// </remarks>
/// <param name="context">The Content data context.</param>
/// <param name="renderer">Resolves images for the response.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class RemoveCollectionItemCommandHandler(
    ContentDbContext context,
    ContentRenderer renderer,
    IClock clock) : ICommandHandler<RemoveCollectionItemCommand, CollectionResponse>
{
    public async Task<Result<CollectionResponse>> HandleAsync(
        RemoveCollectionItemCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var collection = await context.Collections
            .FirstOrDefaultAsync(row => row.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (collection is null)
        {
            return Result.Failure<CollectionResponse>(ContentErrors.NotFound("collection"));
        }

        var items = await context.CollectionItems
            .Where(item => item.CollectionId == collection.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var row = items.Find(item => item.ProductId == command.ProductId);

        if (row is null)
        {
            return Result.Failure<CollectionResponse>(ContentErrors.NotFound("collection item"));
        }

        if (row.IsFromRule)
        {
            return Result.Failure<CollectionResponse>(ContentErrors.CollectionItemFromRule);
        }

        context.CollectionItems.Remove(row);
        collection.RecordMembership(items.Count - 1, clock.UtcNow);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var response = await CollectionReader
            .ToResponseAsync(collection, renderer, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(response);
    }
}
