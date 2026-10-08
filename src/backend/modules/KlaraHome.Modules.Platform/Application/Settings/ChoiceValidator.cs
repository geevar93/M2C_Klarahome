using FluentValidation;
using FluentValidation.Validators;

namespace KlaraHome.Modules.Platform.Application.Settings;

/// <summary>A validator that accepts one of a fixed set of words, and can say which.</summary>
/// <remarks>
/// Exists so a string setting can publish its choices in the settings schema the way an enum would.
/// Settings stay strings because the value lives in a JSON document the storefront reads; the schema
/// reader looks for this interface to fill the field's <c>choices</c>.
/// </remarks>
internal interface IChoiceValidator
{
    /// <summary>The words accepted.</summary>
    IReadOnlyList<string> Choices { get; }
}

/// <summary>Accepts one of a fixed set of words, case-sensitively.</summary>
/// <typeparam name="T">The type being validated.</typeparam>
/// <param name="choices">The words accepted.</param>
internal sealed class ChoiceValidator<T>(IReadOnlyList<string> choices)
    : PropertyValidator<T, string?>, IChoiceValidator
{
    /// <inheritdoc />
    public override string Name => "ChoiceValidator";

    /// <inheritdoc />
    public IReadOnlyList<string> Choices { get; } = choices;

    /// <inheritdoc />
    public override bool IsValid(ValidationContext<T> context, string? value)
        => value is not null && Choices.Contains(value, StringComparer.Ordinal);

    /// <inheritdoc />
    protected override string GetDefaultMessageTemplate(string errorCode)
        => "Must be one of: " + string.Join(", ", Choices) + ".";
}

/// <summary>Rule builder extension for <see cref="ChoiceValidator{T}"/>.</summary>
internal static class ChoiceValidatorExtensions
{
    /// <summary>Requires the value to be one of the given words.</summary>
    /// <typeparam name="T">The type being validated.</typeparam>
    /// <param name="rule">The rule builder.</param>
    /// <param name="choices">The words accepted.</param>
    public static IRuleBuilderOptions<T, string?> MustBeOneOf<T>(
        this IRuleBuilder<T, string?> rule,
        IReadOnlyList<string> choices)
        => rule.SetValidator(new ChoiceValidator<T>(choices));
}
