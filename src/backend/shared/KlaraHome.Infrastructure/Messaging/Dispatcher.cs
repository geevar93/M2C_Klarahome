using System.Collections.Concurrent;
using KlaraHome.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Infrastructure.Messaging;

/// <summary>
/// The default <see cref="IDispatcher"/>. The only reflection is a one-off wrapper construction
/// per request type, cached for the process lifetime; dispatching itself is a virtual call.
/// </summary>
internal sealed class Dispatcher(IServiceProvider serviceProvider) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, object> Wrappers = new();

    public Task<Result> SendAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var wrapper = (CommandWrapper)Wrappers.GetOrAdd(
            command.GetType(),
            static type => Instantiate(typeof(CommandWrapper<>).MakeGenericType(type)));

        return wrapper.HandleAsync(command, serviceProvider, cancellationToken);
    }

    public Task<Result<TResponse>> SendAsync<TResponse>(
        ICommand<TResponse> command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var wrapper = (ResultCommandWrapper<TResponse>)Wrappers.GetOrAdd(
            command.GetType(),
            static type => Instantiate(typeof(ResultCommandWrapper<,>).MakeGenericType(type, typeof(TResponse))));

        return wrapper.HandleAsync(command, serviceProvider, cancellationToken);
    }

    public Task<Result<TResponse>> QueryAsync<TResponse>(
        IQuery<TResponse> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var wrapper = (QueryWrapper<TResponse>)Wrappers.GetOrAdd(
            query.GetType(),
            static type => Instantiate(typeof(QueryWrapper<,>).MakeGenericType(type, typeof(TResponse))));

        return wrapper.HandleAsync(query, serviceProvider, cancellationToken);
    }

    private static object Instantiate(Type wrapperType)
        => Activator.CreateInstance(wrapperType)
           ?? throw new InvalidOperationException($"Could not create dispatcher wrapper '{wrapperType}'.");

    /// <summary>Builds the behaviour chain around a handler invocation.</summary>
    private static Task<TResponse> Pipeline<TRequest, TResponse>(
        TRequest request,
        IServiceProvider services,
        PipelineNext<TResponse> handler,
        CancellationToken cancellationToken)
        where TRequest : notnull
        where TResponse : Result
    {
        var behaviors = services.GetServices<IPipelineBehavior<TRequest, TResponse>>().ToArray();

        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var next = handler;
            handler = () => behavior.HandleAsync(request, next, cancellationToken);
        }

        return handler();
    }

    private abstract class CommandWrapper
    {
        public abstract Task<Result> HandleAsync(
            ICommand command,
            IServiceProvider services,
            CancellationToken cancellationToken);
    }

    private sealed class CommandWrapper<TCommand> : CommandWrapper
        where TCommand : ICommand
    {
        public override Task<Result> HandleAsync(
            ICommand command,
            IServiceProvider services,
            CancellationToken cancellationToken)
        {
            var typed = (TCommand)command;
            var handler = services.GetRequiredService<ICommandHandler<TCommand>>();
            return Pipeline<TCommand, Result>(
                typed,
                services,
                () => handler.HandleAsync(typed, cancellationToken),
                cancellationToken);
        }
    }

    private abstract class ResultCommandWrapper<TResponse>
    {
        public abstract Task<Result<TResponse>> HandleAsync(
            ICommand<TResponse> command,
            IServiceProvider services,
            CancellationToken cancellationToken);
    }

    private sealed class ResultCommandWrapper<TCommand, TResponse> : ResultCommandWrapper<TResponse>
        where TCommand : ICommand<TResponse>
    {
        public override Task<Result<TResponse>> HandleAsync(
            ICommand<TResponse> command,
            IServiceProvider services,
            CancellationToken cancellationToken)
        {
            var typed = (TCommand)command;
            var handler = services.GetRequiredService<ICommandHandler<TCommand, TResponse>>();
            return Pipeline<TCommand, Result<TResponse>>(
                typed,
                services,
                () => handler.HandleAsync(typed, cancellationToken),
                cancellationToken);
        }
    }

    private abstract class QueryWrapper<TResponse>
    {
        public abstract Task<Result<TResponse>> HandleAsync(
            IQuery<TResponse> query,
            IServiceProvider services,
            CancellationToken cancellationToken);
    }

    private sealed class QueryWrapper<TQuery, TResponse> : QueryWrapper<TResponse>
        where TQuery : IQuery<TResponse>
    {
        public override Task<Result<TResponse>> HandleAsync(
            IQuery<TResponse> query,
            IServiceProvider services,
            CancellationToken cancellationToken)
        {
            var typed = (TQuery)query;
            var handler = services.GetRequiredService<IQueryHandler<TQuery, TResponse>>();
            return Pipeline<TQuery, Result<TResponse>>(
                typed,
                services,
                () => handler.HandleAsync(typed, cancellationToken),
                cancellationToken);
        }
    }
}
