using FluentValidation;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Api.Diagnostics;

/// <summary>
/// The command behind the Development-only echo endpoint. It exists to exercise the dispatcher,
/// the validation behaviour and the error contract with a real request, in this host and in the
/// integration tests — it carries no business meaning and never ships enabled.
/// </summary>
internal sealed record EchoCommand(string Message, int Repeat) : ICommand<EchoResponse>;

/// <summary>Result of <see cref="EchoCommand"/>.</summary>
internal sealed record EchoResponse(string Message, int Length);

/// <summary>Rules chosen to produce both a single-field and a multi-field 422.</summary>
internal sealed class EchoCommandValidator : AbstractValidator<EchoCommand>
{
    public EchoCommandValidator()
    {
        RuleFor(command => command.Message)
            .NotEmpty().WithMessage("Message is required.")
            .MaximumLength(200).WithMessage("Message must be at most 200 characters.");

        RuleFor(command => command.Repeat)
            .InclusiveBetween(1, 10).WithMessage("Repeat must be between 1 and 10.");
    }
}

/// <summary>Echoes the message, or refuses it with a stable business code.</summary>
internal sealed class EchoCommandHandler : ICommandHandler<EchoCommand, EchoResponse>
{
    /// <summary>The refusal used to demonstrate a business rule failing after validation passes.</summary>
    internal const string RefusedMessage = "refuse";

    public Task<Result<EchoResponse>> HandleAsync(EchoCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.Equals(command.Message, RefusedMessage, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Result.Failure<EchoResponse>(
                Error.Validation("ECHO_REFUSED", "The echo service refuses that message.")));
        }

        var echoed = string.Concat(Enumerable.Repeat(command.Message, command.Repeat));
        return Task.FromResult(Result.Success(new EchoResponse(echoed, echoed.Length)));
    }
}
