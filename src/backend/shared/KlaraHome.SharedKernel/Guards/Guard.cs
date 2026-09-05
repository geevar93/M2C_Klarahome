using System.Runtime.CompilerServices;

namespace KlaraHome.SharedKernel.Guards;

/// <summary>
/// Invariant checks for domain constructors. A guard failure is a programming error, so it
/// throws; a rule the caller is expected to hit is an <c>Error</c>, not a guard.
/// </summary>
public static class Guard
{
    public static string NotNullOrWhiteSpace(
        string? value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }

    public static T NotNull<T>(
        T? value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        return value;
    }

    public static Guid NotEmpty(
        Guid value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
        => value == Guid.Empty
            ? throw new ArgumentException("Value must not be an empty GUID.", parameterName)
            : value;

    public static int Positive(
        int value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
        => value > 0
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, value, "Value must be greater than zero.");

    public static decimal NotNegative(
        decimal value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
        => value >= 0m
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, value, "Value must not be negative.");

    public static string MaxLength(
        string value,
        int maxLength,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
        => value.Length <= maxLength
            ? value
            : throw new ArgumentException($"Value must be at most {maxLength} characters.", parameterName);
}
