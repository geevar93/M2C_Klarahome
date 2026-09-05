using System.Collections.ObjectModel;

namespace KlaraHome.SharedKernel.Results;

/// <summary>
/// A failure with a stable, machine-readable <see cref="Code"/>. The frontend switches on the
/// code, never on <see cref="Message"/> (docs/04-api-specification.md §1.2).
/// </summary>
public sealed record Error
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoFieldErrors =
        ReadOnlyDictionary<string, IReadOnlyList<string>>.Empty;

    private Error(
        string code,
        string message,
        ErrorType type,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? fieldErrors = null)
    {
        Code = code;
        Message = message;
        Type = type;
        FieldErrors = fieldErrors ?? NoFieldErrors;
    }

    /// <summary>Stable SCREAMING_SNAKE_CASE identifier, e.g. <c>CART_ITEM_OUT_OF_STOCK</c>.</summary>
    public string Code { get; }

    /// <summary>Human-readable explanation. Safe to show a user; never contains internals.</summary>
    public string Message { get; }

    /// <summary>Category, used to choose the HTTP status code.</summary>
    public ErrorType Type { get; }

    /// <summary>Field-level messages for validation failures. Empty for every other type.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> FieldErrors { get; }

    /// <summary>The canonical error used when nothing failed but a <c>Result</c> demands a value.</summary>
    public static readonly Error None = new("NONE", string.Empty, ErrorType.Unexpected);

    public static Error Malformed(string code, string message) => new(code, message, ErrorType.Malformed);

    public static Error Unauthorized(string code = "UNAUTHORIZED", string message = "Authentication is required.")
        => new(code, message, ErrorType.Unauthorized);

    public static Error Forbidden(string code = "FORBIDDEN", string message = "You do not have access to this resource.")
        => new(code, message, ErrorType.Forbidden);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Gone(string code, string message) => new(code, message, ErrorType.Gone);

    public static Error Unexpected(string code = "UNEXPECTED_ERROR", string message = "An unexpected error occurred.")
        => new(code, message, ErrorType.Unexpected);

    public static Error Unavailable(string code, string message) => new(code, message, ErrorType.Unavailable);

    public static Error RateLimited(string code = "RATE_LIMITED", string message = "Too many requests.")
        => new(code, message, ErrorType.RateLimited);

    /// <summary>A business-rule refusal with no field attached (422).</summary>
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    /// <summary>A field-level validation failure (422) — the shape FluentValidation produces.</summary>
    public static Error Validation(
        IReadOnlyDictionary<string, IReadOnlyList<string>> fieldErrors,
        string code = "VALIDATION_FAILED",
        string message = "One or more fields are invalid.")
        => new(code, message, ErrorType.Validation, fieldErrors);

    public override string ToString() => $"{Code}: {Message}";
}
