using System.Diagnostics;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Search.Domain;
using KlaraHome.Modules.Search.Infrastructure;
using KlaraHome.Modules.Search.Infrastructure.Engine;
using KlaraHome.Modules.Search.Infrastructure.Logging;
using KlaraHome.Modules.Search.Infrastructure.Query;
using KlaraHome.SharedKernel.Results;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Search.Application.Products;

/// <summary>What the autocomplete box asks for (docs/04-api-specification.md §3.2).</summary>
/// <param name="Query">What has been typed so far.</param>
/// <param name="Limit">The most suggestions to return, or null for the store's own setting.</param>
internal sealed record SuggestQuery(string? Query, int? Limit) : IQuery<SuggestionResponse>;

/// <summary>
/// Answers the autocomplete box.
/// </summary>
/// <remarks>
/// <para>
/// The highest-rate endpoint this platform has: it fires on a keystroke rather than on a submit, so
/// a shopper typing "cushion cover" produces a dozen of these and one search. Everything about it is
/// shaped by that — the store's minimum query length is enforced before anything is read, the last
/// term is matched as a prefix because the shopper is half-way through typing it, and there are no
/// facets and no total.
/// </para>
/// <para>
/// A query below the minimum answers with an empty list rather than an error. The search endpoint
/// refuses one, because a shopper who pressed enter deserves to be told why nothing happened; a
/// suggestion box that returned a validation problem on the first keystroke of every search would be
/// a suggestion box the storefront had to special-case.
/// </para>
/// <para>
/// It is recorded in the query log under its own source. A prefix is not a question a shopper
/// finished asking, and counting the two together would turn the most-searched-for report into a
/// list of the first three letters of words.
/// </para>
/// </remarks>
/// <param name="engines">Chooses the engine that answers.</param>
/// <param name="settings">Supplies the store's minimum length and suggestion limit.</param>
/// <param name="vocabulary">Supplies the store's synonyms and stop words.</param>
/// <param name="recorder">Records what was typed.</param>
/// <param name="options">Supplies the ceiling on the limit.</param>
internal sealed class SuggestQueryHandler(
    SearchEngineRegistry engines,
    IStoreSettings settings,
    SearchVocabularyReader vocabulary,
    SearchQueryRecorder recorder,
    IOptions<SearchOptions> options)
    : IQueryHandler<SuggestQuery, SuggestionResponse>
{
    public async Task<Result<SuggestionResponse>> HandleAsync(
        SuggestQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var policy = await settings.GetAsync<SearchSettings>(cancellationToken).ConfigureAwait(false);
        var typed = query.Query?.Trim() ?? string.Empty;

        if (typed.Length < policy.MinimumQueryLength)
        {
            return Result.Success(new SuggestionResponse(typed, []));
        }

        var words = await vocabulary.GetAsync(cancellationToken).ConfigureAwait(false);

        // The last term is a prefix because the shopper has not finished typing it. A search would
        // rely on the stemmer instead, which is the better answer once they have.
        var parsed = SearchTextNormalizer.Parse(typed, words, policy.MaxQueryLength, prefixLastTerm: true);

        var limit = Math.Clamp(query.Limit ?? policy.SuggestionLimit, 1, options.Value.MaxPageSize);
        var engine = await engines.ResolveAsync(cancellationToken).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();

        var suggestions = await engine
            .SuggestAsync(parsed, limit, policy, cancellationToken)
            .ConfigureAwait(false);

        stopwatch.Stop();

        await recorder
            .RecordAsync(
                parsed,
                SearchQuerySources.Suggest,
                request: null,
                suggestions.Count,
                stopwatch.ElapsedMilliseconds,
                policy,
                cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new SuggestionResponse(parsed.Normalised, suggestions));
    }
}

/// <summary>
/// Records that a shopper clicked a result (docs/03-database-design.md §4.13).
/// </summary>
/// <remarks>
/// The handle rather than a query id, because the log is partitioned by month and a lookup without
/// the partition key would search every month it holds to find one row.
/// </remarks>
/// <param name="QueryToken">The handle the search answered with.</param>
/// <param name="Position">Which result, one-based.</param>
/// <param name="VariantId">What they clicked.</param>
internal sealed record RecordSearchClickCommand(string? QueryToken, int Position, Guid VariantId) : ICommand;

/// <summary>
/// Writes the click position back onto the query that produced it.
/// </summary>
/// <remarks>
/// <para>
/// The single most useful number this module collects. A query whose clicks all land on the eighth
/// result is a query whose ranking is wrong, and no amount of looking at the results by hand tells
/// anybody that.
/// </para>
/// <para>
/// A handle that names a row the log no longer holds succeeds rather than failing. The log is
/// retained for a year and partitioned by month, so a page somebody left open over a long holiday
/// eventually points at a partition that has been detached — and that is not a shopper's problem,
/// nor is it worth an error on a page they are trying to navigate away from.
/// </para>
/// </remarks>
/// <param name="recorder">Writes the click.</param>
internal sealed class RecordSearchClickCommandHandler(SearchQueryRecorder recorder)
    : ICommandHandler<RecordSearchClickCommand>
{
    public async Task<Result> HandleAsync(RecordSearchClickCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.QueryToken))
        {
            return Result.Failure(SearchErrors.InvalidQueryToken);
        }

        if (!SearchQueryRecorder.TryReadToken(command.QueryToken, out _, out _))
        {
            return Result.Failure(SearchErrors.InvalidQueryToken);
        }

        await recorder
            .RecordClickAsync(command.QueryToken, command.Position, command.VariantId, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success();
    }
}
