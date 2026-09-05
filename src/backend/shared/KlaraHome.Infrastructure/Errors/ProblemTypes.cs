using KlaraHome.SharedKernel.Results;
using Microsoft.AspNetCore.Http;

namespace KlaraHome.Infrastructure.Errors;

/// <summary>
/// Maps an <see cref="ErrorType"/> onto the wire: HTTP status, the RFC 9457 <c>type</c> URI and
/// the human title. The status table is docs/04-api-specification.md §1.2; this is the single
/// implementation of it.
/// </summary>
public static class ProblemTypes
{
    /// <summary>Base URI for documented error types. Resolvable documentation is a Step 33 task.</summary>
    public const string BaseUri = "https://klarahome.dev/errors/";

    public static int StatusCodeFor(ErrorType type) => type switch
    {
        ErrorType.Malformed => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Gone => StatusCodes.Status410Gone,
        ErrorType.Validation => StatusCodes.Status422UnprocessableEntity,
        ErrorType.RateLimited => StatusCodes.Status429TooManyRequests,
        ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };

    public static string TypeUriFor(ErrorType type) => BaseUri + type switch
    {
        ErrorType.Malformed => "malformed-request",
        ErrorType.Unauthorized => "unauthorized",
        ErrorType.Forbidden => "forbidden",
        ErrorType.NotFound => "not-found",
        ErrorType.Conflict => "conflict",
        ErrorType.Gone => "gone",
        ErrorType.Validation => "validation-failed",
        ErrorType.RateLimited => "rate-limited",
        ErrorType.Unavailable => "dependency-unavailable",
        _ => "unexpected-error",
    };

    public static string TitleFor(ErrorType type) => type switch
    {
        ErrorType.Malformed => "Malformed request",
        ErrorType.Unauthorized => "Authentication required",
        ErrorType.Forbidden => "Forbidden",
        ErrorType.NotFound => "Not found",
        ErrorType.Conflict => "Conflict",
        ErrorType.Gone => "No longer available",
        ErrorType.Validation => "Validation failed",
        ErrorType.RateLimited => "Too many requests",
        ErrorType.Unavailable => "Dependency unavailable",
        _ => "Unexpected error",
    };

    /// <summary>
    /// The <c>type</c> URI for a status code produced by the framework rather than by a handler,
    /// so a 404 from routing and a 404 from a handler are indistinguishable to a client.
    /// </summary>
    public static string TypeUriForStatus(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => TypeUriFor(ErrorType.Malformed),
        StatusCodes.Status401Unauthorized => TypeUriFor(ErrorType.Unauthorized),
        StatusCodes.Status403Forbidden => TypeUriFor(ErrorType.Forbidden),
        StatusCodes.Status404NotFound => TypeUriFor(ErrorType.NotFound),
        StatusCodes.Status409Conflict => TypeUriFor(ErrorType.Conflict),
        StatusCodes.Status410Gone => TypeUriFor(ErrorType.Gone),
        StatusCodes.Status422UnprocessableEntity => TypeUriFor(ErrorType.Validation),
        StatusCodes.Status429TooManyRequests => TypeUriFor(ErrorType.RateLimited),
        StatusCodes.Status503ServiceUnavailable => TypeUriFor(ErrorType.Unavailable),
        _ => BaseUri + (statusCode >= 500 ? "unexpected-error" : "request-rejected"),
    };

    /// <summary>The fallback <c>code</c> for a response produced by the framework, not by a handler.</summary>
    public static string CodeFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "BAD_REQUEST",
        StatusCodes.Status401Unauthorized => "UNAUTHORIZED",
        StatusCodes.Status403Forbidden => "FORBIDDEN",
        StatusCodes.Status404NotFound => "NOT_FOUND",
        StatusCodes.Status405MethodNotAllowed => "METHOD_NOT_ALLOWED",
        StatusCodes.Status406NotAcceptable => "NOT_ACCEPTABLE",
        StatusCodes.Status409Conflict => "CONFLICT",
        StatusCodes.Status410Gone => "GONE",
        StatusCodes.Status415UnsupportedMediaType => "UNSUPPORTED_MEDIA_TYPE",
        StatusCodes.Status422UnprocessableEntity => "VALIDATION_FAILED",
        StatusCodes.Status429TooManyRequests => "RATE_LIMITED",
        StatusCodes.Status503ServiceUnavailable => "DEPENDENCY_UNAVAILABLE",
        _ => statusCode >= 500 ? "UNEXPECTED_ERROR" : "REQUEST_REJECTED",
    };
}
