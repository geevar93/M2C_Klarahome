using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Search.Domain;
using KlaraHome.Modules.Search.Infrastructure.Persistence;
using KlaraHome.Modules.Search.Infrastructure.Query;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Search.Application.Vocabulary;

/// <summary>Lists the words this store ignores in a query.</summary>
/// <param name="ActiveOnly">Only the ones currently applied.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListStopWordsQuery(bool? ActiveOnly, string? Cursor, int? Size)
    : IQuery<PagedResult<StopWordResponse>>;

/// <summary>Adds a word to the stop list.</summary>
/// <param name="Word">The word.</param>
internal sealed record CreateStopWordCommand(string? Word) : ICommand<StopWordResponse>;

/// <summary>Switches a stop word on or off.</summary>
/// <param name="Id">The rule.</param>
/// <param name="IsActive">Whether it is applied.</param>
internal sealed record SetStopWordActiveCommand(Guid Id, bool IsActive) : ICommand<StopWordResponse>;

/// <summary>Removes a word from the stop list.</summary>
/// <param name="Id">The rule.</param>
internal sealed record DeleteStopWordCommand(Guid Id) : ICommand;

/// <summary>Lists the store's stop words, alphabetically.</summary>
/// <param name="context">The Search data context.</param>
internal sealed class ListStopWordsQueryHandler(SearchDbContext context)
    : IQueryHandler<ListStopWordsQuery, PagedResult<StopWordResponse>>
{
    public async Task<Result<PagedResult<StopWordResponse>>> HandleAsync(
        ListStopWordsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);
        var rows = context.StopWords.AsNoTracking().AsQueryable();

        if (query.ActiveOnly == true)
        {
            rows = rows.Where(word => word.IsActive);
        }

        if (Cursor.TryDecode(query.Cursor, out var key))
        {
            rows = rows.Where(word => string.Compare(word.Word, key, StringComparison.Ordinal) > 0);
        }

        var page = await rows
            .OrderBy(word => word.Word)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var items = page.Take(size).Select(SearchVocabularyProjection.ToStopWord).ToArray();
        var next = hasMore && items.Length > 0 ? Cursor.Encode(items[^1].Word) : null;

        return Result.Success(new PagedResult<StopWordResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>
/// Adds a word to the stop list.
/// </summary>
/// <remarks>
/// Single words only, for the same reason a synonym is: the list is consulted per token, and a
/// two-word entry would silently never match anything.
/// </remarks>
/// <param name="context">The Search data context.</param>
/// <param name="vocabulary">Drops this process's cached copy after the write.</param>
internal sealed class CreateStopWordCommandHandler(SearchDbContext context, SearchVocabularyReader vocabulary)
    : ICommandHandler<CreateStopWordCommand, StopWordResponse>
{
    public async Task<Result<StopWordResponse>> HandleAsync(
        CreateStopWordCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var word = SearchTextNormalizer.Fold(command.Word);

        if (word.Length == 0 || word.Length > SearchStopWord.MaxWordLength || !word.All(char.IsLetterOrDigit))
        {
            return Result.Failure<StopWordResponse>(SearchErrors.NotSingleWord(command.Word ?? string.Empty));
        }

        var exists = await context.StopWords
            .AnyAsync(candidate => candidate.Word == word, cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            return Result.Failure<StopWordResponse>(SearchErrors.DuplicateStopWord(word));
        }

        var rule = SearchStopWord.Create(word);

        context.StopWords.Add(rule);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        vocabulary.Invalidate();

        return Result.Success(SearchVocabularyProjection.ToStopWord(rule));
    }
}

/// <summary>Switches a stop word on or off without losing it.</summary>
/// <param name="context">The Search data context.</param>
/// <param name="vocabulary">Drops this process's cached copy after the write.</param>
internal sealed class SetStopWordActiveCommandHandler(SearchDbContext context, SearchVocabularyReader vocabulary)
    : ICommandHandler<SetStopWordActiveCommand, StopWordResponse>
{
    public async Task<Result<StopWordResponse>> HandleAsync(
        SetStopWordActiveCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var rule = await context.StopWords
            .FirstOrDefaultAsync(word => word.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (rule is null)
        {
            return Result.Failure<StopWordResponse>(SearchErrors.NotFound("stop word"));
        }

        rule.SetActive(command.IsActive);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        vocabulary.Invalidate();

        return Result.Success(SearchVocabularyProjection.ToStopWord(rule));
    }
}

/// <summary>Removes a word from the stop list.</summary>
/// <param name="context">The Search data context.</param>
/// <param name="vocabulary">Drops this process's cached copy after the write.</param>
internal sealed class DeleteStopWordCommandHandler(SearchDbContext context, SearchVocabularyReader vocabulary)
    : ICommandHandler<DeleteStopWordCommand>
{
    public async Task<Result> HandleAsync(DeleteStopWordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var rule = await context.StopWords
            .FirstOrDefaultAsync(word => word.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (rule is null)
        {
            return Result.Failure(SearchErrors.NotFound("stop word"));
        }

        context.StopWords.Remove(rule);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        vocabulary.Invalidate();

        return Result.Success();
    }
}
