using FluentValidation;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Content.Application.Validation;
using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Infrastructure;
using KlaraHome.Modules.Content.Infrastructure.Collections;
using KlaraHome.Modules.Content.Infrastructure.Persistence;
using KlaraHome.Modules.Content.Infrastructure.Rendering;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Content.Application.Collections;

/// <summary>Lists the store's collections.</summary>
/// <param name="Search">A fragment of the name or the address.</param>
/// <param name="Kind">Only hand-picked, or only rule-based.</param>
/// <param name="ActiveOnly">Only the ones the storefront serves.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListCollectionsQuery(
    string? Search,
    string? Kind,
    bool? ActiveOnly,
    string? Cursor,
    int? Size) : IQuery<PagedResult<CollectionSummaryResponse>>;

/// <summary>Reads one collection.</summary>
/// <param name="Id">The collection.</param>
internal sealed record GetCollectionQuery(Guid Id) : IQuery<CollectionResponse>;

/// <summary>Lists what is in a collection, in its own order.</summary>
/// <param name="Id">The collection.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListCollectionItemsQuery(Guid Id, string? Cursor, int? Size)
    : IQuery<PagedResult<ProductCardResponse>>;

/// <summary>Opens a collection.</summary>
/// <param name="Slug">Its address, or null to derive one from the name.</param>
/// <param name="Name">What a shopper reads.</param>
/// <param name="Description">The copy beneath the heading.</param>
internal sealed record CreateCollectionCommand(string? Slug, string? Name, string? Description)
    : ICommand<CollectionResponse>;

/// <summary>Rewrites a collection's details.</summary>
/// <param name="Id">The collection.</param>
/// <param name="Slug">Its address.</param>
/// <param name="Name">What a shopper reads.</param>
/// <param name="Description">The copy beneath the heading.</param>
/// <param name="Seo">What a crawler is told.</param>
/// <param name="HeroImageFileId">The masthead image.</param>
/// <param name="IsActive">Whether the storefront serves it.</param>
/// <param name="IsListed">Whether the sitemap lists it.</param>
internal sealed record UpdateCollectionCommand(
    Guid Id,
    string? Slug,
    string? Name,
    string? Description,
    SeoBody? Seo,
    Guid? HeroImageFileId,
    bool IsActive,
    bool IsListed) : ICommand<CollectionResponse>;

/// <summary>Writes or clears a collection's rule.</summary>
/// <param name="Id">The collection.</param>
/// <param name="Rule">The rule, or null to make the collection hand-picked again.</param>
internal sealed record SetCollectionRuleCommand(Guid Id, CollectionRuleBody? Rule) : ICommand<CollectionResponse>;

/// <summary>Replaces the hand-picked membership of a collection.</summary>
/// <param name="Id">The collection.</param>
/// <param name="Items">The products, in the order they should render.</param>
internal sealed record SetCollectionItemsCommand(Guid Id, IReadOnlyList<CollectionItemBody>? Items)
    : ICommand<CollectionResponse>;

/// <summary>Evaluates a rule-based collection's rule now, rather than waiting for the sweep.</summary>
/// <param name="Id">The collection.</param>
internal sealed record RefreshCollectionCommand(Guid Id) : ICommand<CollectionResponse>;

/// <summary>Removes a collection.</summary>
/// <param name="Id">The collection.</param>
internal sealed record DeleteCollectionCommand(Guid Id) : ICommand;

/// <summary>Rules a new collection has to satisfy.</summary>
internal sealed class CreateCollectionCommandValidator : AbstractValidator<CreateCollectionCommand>
{
    public CreateCollectionCommandValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(ProductCollection.MaxNameLength);
        RuleFor(command => command.Slug).MaximumLength(ProductCollection.MaxSlugLength);
        RuleFor(command => command.Description).MaximumLength(ProductCollection.MaxDescriptionLength);
    }
}

/// <summary>Rules an edit has to satisfy.</summary>
internal sealed class UpdateCollectionCommandValidator : AbstractValidator<UpdateCollectionCommand>
{
    public UpdateCollectionCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(ProductCollection.MaxNameLength);
        RuleFor(command => command.Slug).NotEmpty().MaximumLength(ProductCollection.MaxSlugLength);
        RuleFor(command => command.Description).MaximumLength(ProductCollection.MaxDescriptionLength);

        RuleFor(command => command.Seo!.SitemapPriority)
            .InclusiveBetween(0m, 1m)
            .When(command => command.Seo?.SitemapPriority is not null);
    }
}

/// <summary>Rules a membership list has to satisfy.</summary>
internal sealed class SetCollectionItemsCommandValidator : AbstractValidator<SetCollectionItemsCommand>
{
    public SetCollectionItemsCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();

        RuleFor(command => command.Items)
            .NotNull()
            .Must(items => items is null || items.Count <= CollectionRuleSet.MaxLimit)
            .WithMessage($"A collection may hold at most {CollectionRuleSet.MaxLimit} products.");
    }
}

/// <summary>Lists the store's collections, newest first.</summary>
/// <param name="context">The Content data context.</param>
internal sealed class ListCollectionsQueryHandler(ContentDbContext context)
    : IQueryHandler<ListCollectionsQuery, PagedResult<CollectionSummaryResponse>>
{
    public async Task<Result<PagedResult<CollectionSummaryResponse>>> HandleAsync(
        ListCollectionsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);
        var rows = context.Collections.AsNoTracking().AsQueryable();

        if (query.Search is { Length: > 0 } search)
        {
            var pattern = $"%{ContentQueries.EscapeLike(search)}%";

            rows = rows.Where(collection =>
                EF.Functions.ILike(collection.Name, pattern, "\\")
                || EF.Functions.ILike(collection.Slug, pattern, "\\"));
        }

        if (Enum.TryParse<CollectionKind>(query.Kind, ignoreCase: true, out var kind))
        {
            rows = rows.Where(collection => collection.Kind == kind);
        }

        if (query.ActiveOnly == true)
        {
            rows = rows.Where(collection => collection.IsActive);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(collection => collection.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(collection => collection.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var items = page.Take(size).Select(ContentProjection.ToCollectionSummary).ToArray();
        var next = hasMore && items.Length > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<CollectionSummaryResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>Reads one collection.</summary>
/// <param name="context">The Content data context.</param>
/// <param name="renderer">Resolves the masthead and Open Graph images.</param>
internal sealed class GetCollectionQueryHandler(ContentDbContext context, ContentRenderer renderer)
    : IQueryHandler<GetCollectionQuery, CollectionResponse>
{
    public async Task<Result<CollectionResponse>> HandleAsync(
        GetCollectionQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var collection = await context.Collections
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == query.Id, cancellationToken)
            .ConfigureAwait(false);

        if (collection is null)
        {
            return Result.Failure<CollectionResponse>(ContentErrors.NotFound("collection"));
        }

        var response = await CollectionReader
            .ToResponseAsync(collection, renderer, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(response);
    }
}

/// <summary>Lists what is in a collection, resolved into cards.</summary>
/// <param name="context">The Content data context.</param>
/// <param name="renderer">Resolves the products.</param>
internal sealed class ListCollectionItemsQueryHandler(ContentDbContext context, ContentRenderer renderer)
    : IQueryHandler<ListCollectionItemsQuery, PagedResult<ProductCardResponse>>
{
    public async Task<Result<PagedResult<ProductCardResponse>>> HandleAsync(
        ListCollectionItemsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);

        var exists = await context.Collections
            .AnyAsync(collection => collection.Id == query.Id, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
        {
            return Result.Failure<PagedResult<ProductCardResponse>>(ContentErrors.NotFound("collection"));
        }

        var rows = context.CollectionItems
            .AsNoTracking()
            .Where(item => item.CollectionId == query.Id);

        // The cursor is the position, which is what this list is ordered by. An id cursor would be
        // meaningless here: the order is a merchandiser's, not the insertion order.
        if (Cursor.TryDecode(query.Cursor, out var key) && int.TryParse(key, out var after))
        {
            rows = rows.Where(item => item.Position > after);
        }

        var page = await rows
            .OrderBy(item => item.Position)
            .Take(size + 1)
            .Select(item => new { item.ProductId, item.Position })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var slice = page.Take(size).ToList();

        var cards = await renderer
            .ResolveProductsAsync([.. slice.Select(row => row.ProductId)], cancellationToken)
            .ConfigureAwait(false);

        var next = hasMore && slice.Count > 0
            ? Cursor.Encode(slice[^1].Position.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : null;

        return Result.Success(new PagedResult<ProductCardResponse>(cards, new PageInfo(size, next)));
    }
}

/// <summary>Opens a collection, hand-picked to begin with.</summary>
/// <param name="context">The Content data context.</param>
/// <param name="renderer">Resolves images for the response.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class CreateCollectionCommandHandler(
    ContentDbContext context,
    ContentRenderer renderer,
    IClock clock)
    : ICommandHandler<CreateCollectionCommand, CollectionResponse>
{
    public async Task<Result<CollectionResponse>> HandleAsync(
        CreateCollectionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var slug = string.IsNullOrWhiteSpace(command.Slug)
            ? ContentFormats.ToSlug(command.Name!, ProductCollection.MaxSlugLength)
            : ContentFormats.ToSlug(command.Slug, ProductCollection.MaxSlugLength);

        if (!ContentFormats.Slug().IsMatch(slug))
        {
            return Result.Failure<CollectionResponse>(ContentErrors.InvalidSlug);
        }

        var taken = await context.Collections
            .AnyAsync(collection => collection.Slug == slug, cancellationToken)
            .ConfigureAwait(false);

        if (taken)
        {
            return Result.Failure<CollectionResponse>(ContentErrors.DuplicateSlug(slug));
        }

        var collection = ProductCollection.Create(slug, command.Name!.Trim(), CollectionKind.Manual);

        collection.Describe(
            slug,
            command.Name.Trim(),
            command.Description?.Trim(),
            new SeoMetadata(),
            heroImageFileId: null,
            isActive: true,
            isListed: true,
            clock.UtcNow);

        context.Collections.Add(collection);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var response = await CollectionReader
            .ToResponseAsync(collection, renderer, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(response);
    }
}

/// <summary>Rewrites a collection's details.</summary>
/// <param name="context">The Content data context.</param>
/// <param name="renderer">Resolves images for the response.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class UpdateCollectionCommandHandler(
    ContentDbContext context,
    ContentRenderer renderer,
    IClock clock)
    : ICommandHandler<UpdateCollectionCommand, CollectionResponse>
{
    public async Task<Result<CollectionResponse>> HandleAsync(
        UpdateCollectionCommand command,
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

        var slug = ContentFormats.ToSlug(command.Slug!, ProductCollection.MaxSlugLength);

        if (!ContentFormats.Slug().IsMatch(slug))
        {
            return Result.Failure<CollectionResponse>(ContentErrors.InvalidSlug);
        }

        var taken = await context.Collections
            .AnyAsync(row => row.Slug == slug && row.Id != collection.Id, cancellationToken)
            .ConfigureAwait(false);

        if (taken)
        {
            return Result.Failure<CollectionResponse>(ContentErrors.DuplicateSlug(slug));
        }

        collection.Describe(
            slug,
            command.Name!.Trim(),
            command.Description?.Trim(),
            ContentProjection.FromSeo(command.Seo),
            command.HeroImageFileId,
            command.IsActive,
            command.IsListed,
            clock.UtcNow);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var response = await CollectionReader
            .ToResponseAsync(collection, renderer, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(response);
    }
}

/// <summary>
/// Writes or clears a collection's rule, and evaluates it immediately.
/// </summary>
/// <remarks>
/// Evaluated on the spot rather than left to the sweep, because a merchandiser who has just written a
/// rule needs to see what it caught. A rule that matched nothing is almost always a rule with a
/// mistake in it, and finding that out half an hour later is finding it out after the campaign has
/// been announced.
/// </remarks>
/// <param name="context">The Content data context.</param>
/// <param name="materializer">Turns the rule into rows.</param>
/// <param name="renderer">Resolves images for the response.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class SetCollectionRuleCommandHandler(
    ContentDbContext context,
    CollectionMaterializer materializer,
    ContentRenderer renderer,
    IClock clock)
    : ICommandHandler<SetCollectionRuleCommand, CollectionResponse>
{
    public async Task<Result<CollectionResponse>> HandleAsync(
        SetCollectionRuleCommand command,
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

        if (command.Rule is null)
        {
            collection.ClearRule(clock.UtcNow);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var cleared = await CollectionReader
                .ToResponseAsync(collection, renderer, cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(cleared);
        }

        var parsed = RuleBinder.Bind(command.Rule);

        if (parsed.IsFailure)
        {
            return Result.Failure<CollectionResponse>(parsed.Error);
        }

        collection.SetRule(CollectionRules.Write(parsed.Value), clock.UtcNow);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await materializer.RefreshAsync(collection, cancellationToken).ConfigureAwait(false);

        var response = await CollectionReader
            .ToResponseAsync(collection, renderer, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(response);
    }
}

/// <summary>Replaces the hand-picked membership of a collection.</summary>
/// <remarks>
/// The rows a rule wrote are left alone. On a hand-picked collection there are none; on a rule-based
/// one this is how a merchandiser pins three products to the front, and overwriting the rule's rows
/// here would empty the collection until the next sweep.
/// </remarks>
/// <param name="context">The Content data context.</param>
/// <param name="renderer">Resolves images for the response.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class SetCollectionItemsCommandHandler(
    ContentDbContext context,
    ContentRenderer renderer,
    IClock clock)
    : ICommandHandler<SetCollectionItemsCommand, CollectionResponse>
{
    public async Task<Result<CollectionResponse>> HandleAsync(
        SetCollectionItemsCommand command,
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

        var existing = await context.CollectionItems
            .Where(item => item.CollectionId == collection.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var wanted = (command.Items ?? [])
            .Where(item => item.ProductId != Guid.Empty)
            .GroupBy(item => item.ProductId)
            .Select(group => group.First())
            .ToList();

        var now = clock.UtcNow;
        var position = 0;
        var kept = new List<Guid>();

        foreach (var item in wanted)
        {
            var row = existing.FirstOrDefault(candidate => candidate.ProductId == item.ProductId);

            if (row is null)
            {
                context.CollectionItems.Add(CollectionItem.Create(
                    collection.Id,
                    item.ProductId,
                    position,
                    item.IsPinned,
                    isFromRule: false,
                    now));
            }
            else
            {
                row.MoveTo(position, item.IsPinned);
            }

            kept.Add(item.ProductId);
            position++;
        }

        // Only the rows a person put here. A rule's rows are the rule's to remove.
        context.CollectionItems.RemoveRange(
            existing.Where(row => !row.IsFromRule && !kept.Contains(row.ProductId)));

        collection.RecordMembership(
            position + existing.Count(row => row.IsFromRule && !kept.Contains(row.ProductId)),
            now);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var response = await CollectionReader
            .ToResponseAsync(collection, renderer, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(response);
    }
}

/// <summary>Evaluates a rule now.</summary>
/// <param name="context">The Content data context.</param>
/// <param name="materializer">Turns the rule into rows.</param>
/// <param name="renderer">Resolves images for the response.</param>
internal sealed class RefreshCollectionCommandHandler(
    ContentDbContext context,
    CollectionMaterializer materializer,
    ContentRenderer renderer)
    : ICommandHandler<RefreshCollectionCommand, CollectionResponse>
{
    public async Task<Result<CollectionResponse>> HandleAsync(
        RefreshCollectionCommand command,
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

        if (collection.Kind != CollectionKind.Rule)
        {
            return Result.Failure<CollectionResponse>(ContentErrors.NotRuleBased);
        }

        await materializer.RefreshAsync(collection, cancellationToken).ConfigureAwait(false);

        var response = await CollectionReader
            .ToResponseAsync(collection, renderer, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(response);
    }
}

/// <summary>Removes a collection.</summary>
/// <remarks>
/// A soft delete, because a collection is a URL: a crawler has indexed it, a campaign has linked to
/// it, and a merchandiser deleting one should leave a row behind that an operator can point a redirect
/// at. Its membership rows go, since they are derived and nothing reads them once the collection is
/// gone.
/// </remarks>
/// <param name="context">The Content data context.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class DeleteCollectionCommandHandler(ContentDbContext context, ContentScope scope, IClock clock)
    : ICommandHandler<DeleteCollectionCommand>
{
    public async Task<Result> HandleAsync(DeleteCollectionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var collection = await context.Collections
            .FirstOrDefaultAsync(row => row.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (collection is null)
        {
            return Result.Failure(ContentErrors.NotFound("collection"));
        }

        var items = await context.CollectionItems
            .Where(item => item.CollectionId == collection.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        context.CollectionItems.RemoveRange(items);
        collection.Delete(clock.UtcNow, scope.ActorId);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Turns the rule a caller sent into the rule that is stored.</summary>
/// <remarks>
/// Every refusal here names what an editor did wrong in their own words, because a rule is the one
/// thing in this module a non-technical person writes that has the shape of a query. "That is not a
/// field this store can filter on" is actionable; a validation failure keyed on
/// <c>conditions[2].field</c> is not.
/// </remarks>
internal static class RuleBinder
{
    /// <summary>Validates and converts a rule.</summary>
    /// <param name="body">The rule as it was sent.</param>
    public static Result<CollectionRuleSet> Bind(CollectionRuleBody body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var conditions = body.Conditions ?? [];

        if (conditions.Count == 0)
        {
            return Result.Failure<CollectionRuleSet>(
                ContentErrors.InvalidRule("A rule needs at least one condition."));
        }

        if (conditions.Count > CollectionRuleSet.MaxConditions)
        {
            return Result.Failure<CollectionRuleSet>(ContentErrors.InvalidRule(
                $"A rule may have at most {CollectionRuleSet.MaxConditions} conditions."));
        }

        var bound = new List<RuleCondition>(conditions.Count);

        foreach (var condition in conditions)
        {
            if (!Enum.TryParse<RuleField>(condition.Field, ignoreCase: true, out var field))
            {
                return Result.Failure<CollectionRuleSet>(ContentErrors.InvalidRule(
                    $"'{condition.Field}' is not something this store can filter on. "
                    + $"Use one of: {string.Join(", ", Enum.GetNames<RuleField>())}."));
            }

            if (!Enum.TryParse<RuleOperator>(condition.Operator, ignoreCase: true, out var op))
            {
                return Result.Failure<CollectionRuleSet>(ContentErrors.InvalidRule(
                    $"'{condition.Operator}' is not a comparison this store understands."));
            }

            var values = (condition.Values ?? [])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .ToList();

            if (values.Count == 0)
            {
                return Result.Failure<CollectionRuleSet>(ContentErrors.InvalidRule(
                    $"The condition on {field} needs at least one value."));
            }

            // An attribute condition with no attribute named would match everything or nothing
            // depending on how it is read, which is the worst kind of ambiguity to leave in a rule a
            // merchandiser will trust.
            if (field == RuleField.Attribute && string.IsNullOrWhiteSpace(condition.Key))
            {
                return Result.Failure<CollectionRuleSet>(ContentErrors.InvalidRule(
                    "An attribute condition has to say which attribute it is about."));
            }

            bound.Add(new RuleCondition(field, condition.Key?.Trim(), op, values));
        }

        var sort = Enum.TryParse<CollectionSort>(body.Sort, ignoreCase: true, out var parsed)
            ? parsed
            : CollectionSort.Newest;

        var limit = body.Limit <= 0
            ? 100
            : Math.Min(body.Limit, CollectionRuleSet.MaxLimit);

        return Result.Success(new CollectionRuleSet(
            body.MatchAll,
            bound,
            sort,
            limit,
            body.IncludeOutOfStock));
    }
}

/// <summary>Assembles a collection's response, resolving the two images it carries.</summary>
internal static class CollectionReader
{
    /// <summary>Builds the document an editor opens.</summary>
    /// <param name="collection">The collection.</param>
    /// <param name="renderer">Resolves the images.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<CollectionResponse> ToResponseAsync(
        ProductCollection collection,
        ContentRenderer renderer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(renderer);

        var hero = await renderer
            .ResolveImageAsync(collection.HeroImageFileId, collection.Name, cancellationToken)
            .ConfigureAwait(false);

        var ogImage = await renderer
            .ResolveImageAsync(collection.Seo.OgImageFileId, collection.Name, cancellationToken)
            .ConfigureAwait(false);

        return ContentProjection.ToCollection(
            collection,
            ContentProjection.ToSeo(collection.Seo, ogImage),
            hero);
    }
}
