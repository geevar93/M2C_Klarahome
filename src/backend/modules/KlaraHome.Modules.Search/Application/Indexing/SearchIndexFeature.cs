using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Search.Infrastructure.Projection;
using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Modules.Search.Application.Indexing;

/// <summary>What the index looks like right now.</summary>
internal sealed record GetIndexStatusQuery : IQuery<SearchIndexStatus>;

/// <summary>
/// Rebuilds index rows from the catalogue.
/// </summary>
/// <remarks>
/// Bounded and resumable rather than "rebuild everything". A rebuild of a fifty-thousand product
/// catalogue in one request is a request that times out, cannot be watched and cannot be stopped;
/// this one answers with where it reached, and the operator — or a script — asks again.
/// </remarks>
/// <param name="AfterVariantId">Resume after this variant, or null to start at the beginning.</param>
/// <param name="MaxVariants">The most variants this run walks, or null for the configured ceiling.</param>
internal sealed record RebuildIndexCommand(Guid? AfterVariantId, int? MaxVariants) : ICommand<ReindexResponse>;

/// <summary>
/// Rebuilds the index rows for named variants.
/// </summary>
/// <remarks>
/// The surgical version, for the row somebody can see is wrong. It exists because the alternative an
/// operator reaches for otherwise is a full rebuild, which is a large hammer for one product.
/// </remarks>
/// <param name="VariantIds">The variants to rebuild.</param>
internal sealed record ReindexVariantsCommand(IReadOnlyList<Guid>? VariantIds) : ICommand<ReindexResponse>;

/// <summary>Reports the state of the index.</summary>
/// <param name="index">Reads the counts and the engine in use.</param>
internal sealed class GetIndexStatusQueryHandler(SearchIndexService index)
    : IQueryHandler<GetIndexStatusQuery, SearchIndexStatus>
{
    public async Task<Result<SearchIndexStatus>> HandleAsync(
        GetIndexStatusQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var status = await index.StatusAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(status);
    }
}

/// <summary>Walks the catalogue and rebuilds what it finds.</summary>
/// <param name="index">Does the walking.</param>
internal sealed class RebuildIndexCommandHandler(SearchIndexService index)
    : ICommandHandler<RebuildIndexCommand, ReindexResponse>
{
    public async Task<Result<ReindexResponse>> HandleAsync(
        RebuildIndexCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var outcome = await index
            .RebuildAsync(command.AfterVariantId, command.MaxVariants, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(outcome);
    }
}

/// <summary>Rebuilds the rows for named variants.</summary>
/// <param name="writer">The single place an index row is written.</param>
internal sealed class ReindexVariantsCommandHandler(SearchProjectionWriter writer)
    : ICommandHandler<ReindexVariantsCommand, ReindexResponse>
{
    /// <summary>The most variants one surgical rebuild will take.</summary>
    /// <remarks>
    /// A ceiling rather than a page, because this is not the endpoint for rebuilding a catalogue —
    /// that one is, and it is resumable. A request naming five hundred variants is somebody using
    /// the wrong tool.
    /// </remarks>
    private const int MaxVariants = 200;

    public async Task<Result<ReindexResponse>> HandleAsync(
        ReindexVariantsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var variantIds = (command.VariantIds ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(MaxVariants)
            .ToList();

        if (variantIds.Count == 0)
        {
            return Result.Success(new ReindexResponse(0, 0, 0, true, null));
        }

        var outcome = await writer
            .RefreshVariantsAsync(variantIds, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new ReindexResponse(
            variantIds.Count,
            outcome.Written,
            outcome.Retired,
            IsComplete: true,
            ResumeAfterVariantId: null));
    }
}
