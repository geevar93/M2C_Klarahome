using KlaraHome.Infrastructure.Configuration;
using KlaraHome.Infrastructure.Errors;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Api.Diagnostics;

/// <summary>
/// Development-only endpoints that prove the cross-cutting contract is real rather than
/// documented: the error model, the validation pipeline and the dispatcher.
/// </summary>
/// <remarks>
/// Mapped only when the environment is Development <em>and</em>
/// <see cref="ApiOptions.EnableDiagnosticsEndpoints"/> is set. Both conditions, deliberately:
/// a misconfigured environment variable alone must not be able to expose them.
/// </remarks>
internal static class DiagnosticsEndpoints
{
    public static IEndpointRouteBuilder MapDiagnosticsEndpoints(
        this IEndpointRouteBuilder endpoints,
        IHostEnvironment environment,
        ApiOptions api)
    {
        if (!environment.IsDevelopment() || !api.EnableDiagnosticsEndpoints)
        {
            return endpoints;
        }

        var group = endpoints
            .MapGroup("/diagnostics")
            .WithTags("Diagnostics")
            .ExcludeFromDescription();

        group.MapPost("/echo", async (EchoCommand command, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);
                return result.Match(Results.Ok, error => error.ToProblemResult(context));
            })
            .WithName("diagnosticsEcho");

        group.MapGet("/error/{errorType}", (string errorType, HttpContext context) =>
            {
                if (!Enum.TryParse<ErrorType>(errorType, ignoreCase: true, out var parsed))
                {
                    return Error
                        .NotFound("DIAGNOSTIC_ERROR_TYPE_UNKNOWN", $"No error type named {errorType}.")
                        .ToProblemResult(context);
                }

                return BuildError(parsed).ToProblemResult(context);
            })
            .WithName("diagnosticsError");

        group.MapGet("/boom", (HttpContext _) => Boom())
            .WithName("diagnosticsBoom");

        return endpoints;
    }

    /// <summary>Always throws. Separated so the endpoint delegate has a declared return type.</summary>
    private static IResult Boom()
        => throw new InvalidOperationException(
            "Deliberate diagnostic failure: this exception proves the global exception handler.");

    private static Error BuildError(ErrorType type) => type switch
    {
        ErrorType.Malformed => Error.Malformed("DIAGNOSTIC_MALFORMED", "Deliberate malformed-request error."),
        ErrorType.Unauthorized => Error.Unauthorized(message: "Deliberate unauthorized error."),
        ErrorType.Forbidden => Error.Forbidden(message: "Deliberate forbidden error."),
        ErrorType.NotFound => Error.NotFound("DIAGNOSTIC_NOT_FOUND", "Deliberate not-found error."),
        ErrorType.Conflict => Error.Conflict("DIAGNOSTIC_CONFLICT", "Deliberate conflict error."),
        ErrorType.Gone => Error.Gone("DIAGNOSTIC_GONE", "Deliberate gone error."),
        ErrorType.Validation => Error.Validation(
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["message"] = ["Deliberate field error."],
            }),
        ErrorType.RateLimited => Error.RateLimited(message: "Deliberate rate-limit error."),
        ErrorType.Unavailable => Error.Unavailable("DIAGNOSTIC_UNAVAILABLE", "Deliberate dependency error."),
        _ => Error.Unexpected(message: "Deliberate unexpected error."),
    };
}
