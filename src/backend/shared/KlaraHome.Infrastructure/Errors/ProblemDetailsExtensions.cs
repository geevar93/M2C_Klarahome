using KlaraHome.Infrastructure.Correlation;
using KlaraHome.SharedKernel.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Infrastructure.Errors;

/// <summary>Turns an <see cref="Error"/> into the RFC 9457 payload the API contract promises.</summary>
public static class ProblemDetailsExtensions
{
    /// <summary>Extension member carrying the stable machine-readable code the frontend switches on.</summary>
    public const string CodeExtension = "code";

    /// <summary>Extension member echoing the request's correlation id.</summary>
    public const string CorrelationIdExtension = "correlationId";

    /// <summary>Extension member carrying field-level validation messages.</summary>
    public const string ErrorsExtension = "errors";

    private const string FrameworkTypeUriPrefix = "https://tools.ietf.org/";

    /// <summary>Builds the ProblemDetails document for an error, in the context of a request.</summary>
    public static ProblemDetails ToProblemDetails(this Error error, HttpContext? httpContext)
    {
        ArgumentNullException.ThrowIfNull(error);

        var problem = new ProblemDetails
        {
            Type = ProblemTypes.TypeUriFor(error.Type),
            Title = ProblemTypes.TitleFor(error.Type),
            Status = ProblemTypes.StatusCodeFor(error.Type),
            Detail = error.Message,
            Instance = httpContext?.Request.Path.Value,
        };

        problem.Extensions[CodeExtension] = error.Code;

        if (error.FieldErrors.Count > 0)
        {
            problem.Extensions[ErrorsExtension] = error.FieldErrors;
        }

        return problem.Enrich(httpContext);
    }

    /// <summary>Writes the error as an <c>application/problem+json</c> response.</summary>
    public static IResult ToProblemResult(this Error error, HttpContext? httpContext)
        => Results.Problem(error.ToProblemDetails(httpContext));

    /// <summary>
    /// Adds the members every KlaraHome problem document carries, whoever produced it — including
    /// the framework's own 404s, 405s and 415s.
    /// </summary>
    public static ProblemDetails Enrich(this ProblemDetails problem, HttpContext? httpContext)
    {
        ArgumentNullException.ThrowIfNull(problem);

        var status = problem.Status ?? StatusCodes.Status500InternalServerError;
        problem.Status = status;
        problem.Extensions.TryAdd(CodeExtension, ProblemTypes.CodeFor(status));

        // The framework points `type` at the RFC 9110 status registry for the responses it
        // produces itself. Rewrite it so every problem document in this API is discoverable from
        // one place, whoever produced it.
        if (problem.Type is null || problem.Type.StartsWith(FrameworkTypeUriPrefix, StringComparison.Ordinal))
        {
            problem.Type = ProblemTypes.TypeUriForStatus(status);
        }

        var correlationId = ResolveCorrelationId(httpContext);
        if (correlationId is not null)
        {
            problem.Extensions[CorrelationIdExtension] = correlationId;
        }

        problem.Instance ??= httpContext?.Request.Path.Value;
        return problem;
    }

    private static string? ResolveCorrelationId(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return null;
        }

        if (httpContext.Items.TryGetValue(CorrelationHeaders.CorrelationId, out var item) && item is string fromItems)
        {
            return fromItems;
        }

        return httpContext.RequestServices?.GetService<ICorrelationContext>()?.CorrelationId;
    }
}
