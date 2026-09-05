using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Infrastructure.Messaging;

/// <summary>
/// Routes a request to its single handler through the pipeline behaviours. Deliberately
/// in-house: MediatR moved to a commercial licence and this product is redistributed
/// (ADR-009, docs/01-architecture.md §4.3).
/// </summary>
public interface IDispatcher
{
    Task<Result> SendAsync(ICommand command, CancellationToken cancellationToken = default);

    Task<Result<TResponse>> SendAsync<TResponse>(
        ICommand<TResponse> command,
        CancellationToken cancellationToken = default);

    Task<Result<TResponse>> QueryAsync<TResponse>(
        IQuery<TResponse> query,
        CancellationToken cancellationToken = default);
}
