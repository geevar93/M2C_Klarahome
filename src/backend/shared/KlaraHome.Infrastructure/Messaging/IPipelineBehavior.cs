using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Infrastructure.Messaging;

/// <summary>Continues the pipeline. Calling it invokes the next behaviour, or the handler.</summary>
public delegate Task<TResponse> PipelineNext<TResponse>()
    where TResponse : Result;

/// <summary>
/// A cross-cutting step wrapped around every handler — validation, logging, and later
/// transactions and idempotency. Registered as an open generic, so one implementation covers
/// every request type.
/// </summary>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    Task<TResponse> HandleAsync(
        TRequest request,
        PipelineNext<TResponse> next,
        CancellationToken cancellationToken);
}
