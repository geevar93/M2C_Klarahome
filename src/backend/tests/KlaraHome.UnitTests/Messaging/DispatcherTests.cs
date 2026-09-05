using FluentValidation;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KlaraHome.UnitTests.Messaging;

public sealed class DispatcherTests
{
    [Fact]
    public async Task Routes_a_command_to_its_handler()
    {
        await using var provider = Build();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.SendAsync(new RecordCommand("noted"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("noted", Assert.Single(provider.GetRequiredService<CommandLog>().Entries));
    }

    [Fact]
    public async Task Routes_a_command_with_a_response_to_its_handler()
    {
        await using var provider = Build();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.SendAsync(new DoubleCommand(21), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public async Task Routes_a_query_to_its_handler()
    {
        await using var provider = Build();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.QueryAsync(new GreetQuery("Klara"), TestContext.Current.CancellationToken);

        Assert.Equal("Hello Klara", result.Value);
    }

    [Fact]
    public async Task Surfaces_a_handler_failure_as_a_failed_result_not_an_exception()
    {
        await using var provider = Build();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.SendAsync(new DoubleCommand(-1), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("NEGATIVE_INPUT", result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public async Task Runs_validators_before_the_handler_and_reports_every_field()
    {
        await using var provider = Build();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.SendAsync(new RecordCommand(""), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("VALIDATION_FAILED", result.Error.Code);
        Assert.Contains("text", result.Error.FieldErrors);
        Assert.True(
            provider.GetRequiredService<CommandLog>().Entries.Count == 0,
            "the handler must not run when validation fails");
    }

    [Fact]
    public async Task Behaviours_wrap_the_handler_in_registration_order()
    {
        await using var provider = Build();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        await dispatcher.SendAsync(new DoubleCommand(1), TestContext.Current.CancellationToken);

        Assert.Equal(
            ["outer:before", "inner:before", "inner:after", "outer:after"],
            provider.GetRequiredService<CommandLog>().Behaviours);
    }

    [Fact]
    public async Task A_command_with_no_registered_handler_fails_loudly()
    {
        await using var provider = Build();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await dispatcher.SendAsync(new UnhandledCommand(), TestContext.Current.CancellationToken));
    }

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.None));
        services.AddSingleton<CommandLog>();
        services.AddMessaging(typeof(DispatcherTests).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(OuterBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(InnerBehavior<,>));
        return services.BuildServiceProvider();
    }
}

internal sealed class CommandLog
{
    public List<string> Entries { get; } = [];

    public List<string> Behaviours { get; } = [];
}

internal sealed record RecordCommand(string Text) : ICommand;

internal sealed class RecordCommandValidator : AbstractValidator<RecordCommand>
{
    public RecordCommandValidator() => RuleFor(command => command.Text).NotEmpty();
}

internal sealed class RecordCommandHandler(CommandLog log) : ICommandHandler<RecordCommand>
{
    public Task<Result> HandleAsync(RecordCommand command, CancellationToken cancellationToken)
    {
        log.Entries.Add(command.Text);
        return Task.FromResult(Result.Success());
    }
}

internal sealed record DoubleCommand(int Value) : ICommand<int>;

internal sealed class DoubleCommandHandler : ICommandHandler<DoubleCommand, int>
{
    public Task<Result<int>> HandleAsync(DoubleCommand command, CancellationToken cancellationToken)
        => Task.FromResult(command.Value < 0
            ? Result.Failure<int>(Error.Validation("NEGATIVE_INPUT", "Value must not be negative."))
            : Result.Success(command.Value * 2));
}

internal sealed record GreetQuery(string Name) : IQuery<string>;

internal sealed class GreetQueryHandler : IQueryHandler<GreetQuery, string>
{
    public Task<Result<string>> HandleAsync(GreetQuery query, CancellationToken cancellationToken)
        => Task.FromResult(Result.Success($"Hello {query.Name}"));
}

internal sealed record UnhandledCommand : ICommand;

internal sealed class OuterBehavior<TRequest, TResponse>(CommandLog log) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> HandleAsync(
        TRequest request,
        PipelineNext<TResponse> next,
        CancellationToken cancellationToken)
    {
        log.Behaviours.Add("outer:before");
        var response = await next();
        log.Behaviours.Add("outer:after");
        return response;
    }
}

internal sealed class InnerBehavior<TRequest, TResponse>(CommandLog log) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> HandleAsync(
        TRequest request,
        PipelineNext<TResponse> next,
        CancellationToken cancellationToken)
    {
        log.Behaviours.Add("inner:before");
        var response = await next();
        log.Behaviours.Add("inner:after");
        return response;
    }
}
