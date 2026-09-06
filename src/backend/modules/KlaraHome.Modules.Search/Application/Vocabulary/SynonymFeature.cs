using FluentValidation;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Search.Domain;
using KlaraHome.Modules.Search.Infrastructure.Persistence;
using KlaraHome.Modules.Search.Infrastructure.Query;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Search.Application.Vocabulary;

/// <summary>Lists the store's synonym rules.</summary>
/// <param name="Search">A fragment of the term or one of its expansions.</param>
/// <param name="ActiveOnly">Only the rules currently applied.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListSynonymsQuery(string? Search, bool? ActiveOnly, string? Cursor, int? Size)
    : IQuery<PagedResult<SynonymResponse>>;

/// <summary>Adds a synonym rule.</summary>
/// <param name="Term">The word a shopper types.</param>
/// <param name="Expansions">What it is also taken to mean.</param>
/// <param name="IsBidirectional">Whether the expansions expand back to the term.</param>
/// <param name="Note">Why.</param>
internal sealed record CreateSynonymCommand(
    string? Term,
    IReadOnlyList<string>? Expansions,
    bool IsBidirectional,
    string? Note) : ICommand<SynonymResponse>;

/// <summary>Edits a synonym rule.</summary>
/// <param name="Id">The rule.</param>
/// <param name="Expansions">Its new expansions.</param>
/// <param name="IsBidirectional">Whether they expand back.</param>
/// <param name="IsActive">Whether the rule is applied.</param>
/// <param name="Note">Why.</param>
internal sealed record UpdateSynonymCommand(
    Guid Id,
    IReadOnlyList<string>? Expansions,
    bool IsBidirectional,
    bool IsActive,
    string? Note) : ICommand<SynonymResponse>;

/// <summary>Removes a synonym rule.</summary>
/// <param name="Id">The rule.</param>
internal sealed record DeleteSynonymCommand(Guid Id) : ICommand;

/// <summary>
/// Rules a synonym has to satisfy before it is written.
/// </summary>
/// <remarks>
/// The single-word rule is the one worth explaining. A phrase synonym needs a phrase operator and a
/// different index, and half-supporting one — by matching its first word — would be worse than
/// refusing it: the rule would appear to work and would quietly mean something else. Refused here,
/// and recorded in the parking lot as the thing to build if merchandising asks for it.
/// </remarks>
internal sealed class CreateSynonymCommandValidator : AbstractValidator<CreateSynonymCommand>
{
    public CreateSynonymCommandValidator()
    {
        RuleFor(command => command.Term)
            .NotEmpty()
            .MaximumLength(SearchSynonym.MaxTermLength);

        RuleFor(command => command.Expansions)
            .NotNull()
            .Must(expansions => expansions is { Count: > 0 })
            .WithMessage("A synonym rule needs at least one word to expand to.")
            .Must(expansions => expansions is null || expansions.Count <= SearchSynonym.MaxExpansions)
            .WithMessage($"A synonym rule may have at most {SearchSynonym.MaxExpansions} expansions.");

        RuleFor(command => command.Note).MaximumLength(500);
    }
}

/// <summary>Rules an edit has to satisfy. The term itself is not editable.</summary>
/// <remarks>
/// Changing the word a rule is about would be a different rule with the same id, and every report
/// that had grouped by it would silently change meaning. Delete it and add the other one.
/// </remarks>
internal sealed class UpdateSynonymCommandValidator : AbstractValidator<UpdateSynonymCommand>
{
    public UpdateSynonymCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();

        RuleFor(command => command.Expansions)
            .NotNull()
            .Must(expansions => expansions is { Count: > 0 })
            .WithMessage("A synonym rule needs at least one word to expand to.")
            .Must(expansions => expansions is null || expansions.Count <= SearchSynonym.MaxExpansions)
            .WithMessage($"A synonym rule may have at most {SearchSynonym.MaxExpansions} expansions.");

        RuleFor(command => command.Note).MaximumLength(500);
    }
}

/// <summary>Lists the store's synonym rules, alphabetically.</summary>
/// <param name="context">The Search data context.</param>
internal sealed class ListSynonymsQueryHandler(SearchDbContext context)
    : IQueryHandler<ListSynonymsQuery, PagedResult<SynonymResponse>>
{
    public async Task<Result<PagedResult<SynonymResponse>>> HandleAsync(
        ListSynonymsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);
        var rows = context.Synonyms.AsNoTracking().AsQueryable();

        if (query.Search is { Length: > 0 } search)
        {
            var folded = SearchTextNormalizer.Fold(search);

            rows = rows.Where(synonym =>
                synonym.Term.Contains(folded) || synonym.Expansions.Any(value => value.Contains(folded)));
        }

        if (query.ActiveOnly == true)
        {
            rows = rows.Where(synonym => synonym.IsActive);
        }

        // Alphabetical rather than newest-first: this is a dictionary, and a merchandiser looking for
        // "sofa" is looking it up rather than scrolling back through when it was added.
        if (Cursor.TryDecode(query.Cursor, out var key))
        {
            rows = rows.Where(synonym => string.Compare(synonym.Term, key, StringComparison.Ordinal) > 0);
        }

        var page = await rows
            .OrderBy(synonym => synonym.Term)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var items = page.Take(size).Select(SearchVocabularyProjection.ToSynonym).ToArray();
        var next = hasMore && items.Length > 0 ? Cursor.Encode(items[^1].Term) : null;

        return Result.Success(new PagedResult<SynonymResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>Adds a synonym rule and drops the cached vocabulary so it takes effect.</summary>
/// <param name="context">The Search data context.</param>
/// <param name="vocabulary">Drops this process's cached copy after the write.</param>
internal sealed class CreateSynonymCommandHandler(SearchDbContext context, SearchVocabularyReader vocabulary)
    : ICommandHandler<CreateSynonymCommand, SynonymResponse>
{
    public async Task<Result<SynonymResponse>> HandleAsync(
        CreateSynonymCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var term = SearchTextNormalizer.Fold(command.Term);

        if (!IsSingleWord(term))
        {
            return Result.Failure<SynonymResponse>(SearchErrors.NotSingleWord(command.Term ?? string.Empty));
        }

        var expansions = new List<string>();

        foreach (var raw in command.Expansions ?? [])
        {
            var folded = SearchTextNormalizer.Fold(raw);

            if (!IsSingleWord(folded))
            {
                return Result.Failure<SynonymResponse>(SearchErrors.NotSingleWord(raw));
            }

            // A term that expands to itself would put the same lexeme in the query twice: harmless,
            // and it looks like a bug in every plan anybody ever reads.
            if (!string.Equals(folded, term, StringComparison.Ordinal)
                && !expansions.Contains(folded, StringComparer.Ordinal))
            {
                expansions.Add(folded);
            }
        }

        if (expansions.Count == 0)
        {
            return Result.Failure<SynonymResponse>(SearchErrors.NoExpansions);
        }

        var exists = await context.Synonyms
            .AnyAsync(synonym => synonym.Term == term, cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            return Result.Failure<SynonymResponse>(SearchErrors.DuplicateTerm(term));
        }

        var rule = SearchSynonym.Create(term, expansions, command.IsBidirectional, command.Note);

        context.Synonyms.Add(rule);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        vocabulary.Invalidate();

        return Result.Success(SearchVocabularyProjection.ToSynonym(rule));
    }

    /// <summary>Whether a folded value is one word of letters and digits.</summary>
    /// <param name="value">The folded value.</param>
    internal static bool IsSingleWord(string value)
        => value.Length > 0
           && value.Length <= SearchSynonym.MaxTermLength
           && value.All(char.IsLetterOrDigit);
}

/// <summary>Edits a synonym rule.</summary>
/// <param name="context">The Search data context.</param>
/// <param name="vocabulary">Drops this process's cached copy after the write.</param>
internal sealed class UpdateSynonymCommandHandler(SearchDbContext context, SearchVocabularyReader vocabulary)
    : ICommandHandler<UpdateSynonymCommand, SynonymResponse>
{
    public async Task<Result<SynonymResponse>> HandleAsync(
        UpdateSynonymCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var rule = await context.Synonyms
            .FirstOrDefaultAsync(synonym => synonym.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (rule is null)
        {
            return Result.Failure<SynonymResponse>(SearchErrors.NotFound("synonym rule"));
        }

        var expansions = new List<string>();

        foreach (var raw in command.Expansions ?? [])
        {
            var folded = SearchTextNormalizer.Fold(raw);

            if (!CreateSynonymCommandHandler.IsSingleWord(folded))
            {
                return Result.Failure<SynonymResponse>(SearchErrors.NotSingleWord(raw));
            }

            if (!string.Equals(folded, rule.Term, StringComparison.Ordinal)
                && !expansions.Contains(folded, StringComparer.Ordinal))
            {
                expansions.Add(folded);
            }
        }

        if (expansions.Count == 0)
        {
            return Result.Failure<SynonymResponse>(SearchErrors.NoExpansions);
        }

        rule.Update(expansions, command.IsBidirectional, command.IsActive, command.Note);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        vocabulary.Invalidate();

        return Result.Success(SearchVocabularyProjection.ToSynonym(rule));
    }
}

/// <summary>Removes a synonym rule.</summary>
/// <remarks>
/// A hard delete, unlike most things on this platform. A synonym is a rule and not a record: there
/// is no history to preserve, nothing points at it, and a merchandiser who wants to keep one without
/// applying it switches it off instead.
/// </remarks>
/// <param name="context">The Search data context.</param>
/// <param name="vocabulary">Drops this process's cached copy after the write.</param>
internal sealed class DeleteSynonymCommandHandler(SearchDbContext context, SearchVocabularyReader vocabulary)
    : ICommandHandler<DeleteSynonymCommand>
{
    public async Task<Result> HandleAsync(DeleteSynonymCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var rule = await context.Synonyms
            .FirstOrDefaultAsync(synonym => synonym.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (rule is null)
        {
            return Result.Failure(SearchErrors.NotFound("synonym rule"));
        }

        context.Synonyms.Remove(rule);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        vocabulary.Invalidate();

        return Result.Success();
    }
}

/// <summary>Turns vocabulary entities into the shapes the admin screens read.</summary>
internal static class SearchVocabularyProjection
{
    /// <summary>One synonym rule.</summary>
    /// <param name="synonym">The rule.</param>
    public static SynonymResponse ToSynonym(SearchSynonym synonym)
    {
        ArgumentNullException.ThrowIfNull(synonym);

        return new SynonymResponse(
            synonym.Id,
            synonym.Term,
            synonym.Expansions,
            synonym.IsBidirectional,
            synonym.IsActive,
            synonym.Note,
            synonym.CreatedAt,
            synonym.UpdatedAt);
    }

    /// <summary>One stop word.</summary>
    /// <param name="word">The rule.</param>
    public static StopWordResponse ToStopWord(SearchStopWord word)
    {
        ArgumentNullException.ThrowIfNull(word);

        return new StopWordResponse(word.Id, word.Word, word.IsActive, word.CreatedAt);
    }
}
