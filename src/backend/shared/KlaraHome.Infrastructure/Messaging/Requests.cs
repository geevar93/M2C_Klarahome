using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Infrastructure.Messaging;

/// <summary>A request that changes state and returns nothing but success or failure.</summary>
public interface ICommand;

/// <summary>A request that changes state and returns a value.</summary>
public interface ICommand<TResponse>;

/// <summary>A request that reads state. Queries never mutate and never raise domain events.</summary>
public interface IQuery<TResponse>;

/// <summary>Handles a <see cref="ICommand"/>. One handler per command, registered scoped.</summary>
public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    Task<Result> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

/// <summary>Handles a <see cref="ICommand{TResponse}"/>.</summary>
public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

/// <summary>Handles an <see cref="IQuery{TResponse}"/>.</summary>
public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
