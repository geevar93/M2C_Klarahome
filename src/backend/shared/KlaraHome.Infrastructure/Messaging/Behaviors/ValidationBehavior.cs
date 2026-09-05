using System.Text;
using FluentValidation;
using FluentValidation.Results;
using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Infrastructure.Messaging.Behaviors;

/// <summary>
/// Runs every FluentValidation validator registered for the request before the handler sees it,
/// so a 422 with field-level errors is produced in exactly one place
/// (docs/04-api-specification.md §1.2).
/// </summary>
internal sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> HandleAsync(
        TRequest request,
        PipelineNext<TResponse> next,
        CancellationToken cancellationToken)
    {
        var applicable = validators as IValidator<TRequest>[] ?? [.. validators];
        if (applicable.Length == 0)
        {
            return await next().ConfigureAwait(false);
        }

        var context = new ValidationContext<TRequest>(request);
        var failures = new List<ValidationFailure>();

        foreach (var validator in applicable)
        {
            var result = await validator.ValidateAsync(context, cancellationToken).ConfigureAwait(false);
            if (!result.IsValid)
            {
                failures.AddRange(result.Errors);
            }
        }

        if (failures.Count == 0)
        {
            return await next().ConfigureAwait(false);
        }

        var fieldErrors = failures
            .GroupBy(failure => ToCamelCasePath(failure.PropertyName))
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)[.. group.Select(failure => failure.ErrorMessage).Distinct()],
                StringComparer.Ordinal);

        return ResultFactory.Failure<TResponse>(Error.Validation(fieldErrors));
    }

    /// <summary>
    /// Converts a .NET property path to the JSON path the client sees:
    /// <c>Lines[2].Quantity</c> becomes <c>lines[2].quantity</c>.
    /// </summary>
    internal static string ToCamelCasePath(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return propertyName;
        }

        var builder = new StringBuilder(propertyName.Length);
        var atSegmentStart = true;

        foreach (var character in propertyName)
        {
            if (character is '.' or '[')
            {
                atSegmentStart = character == '.';
                builder.Append(character);
                continue;
            }

            if (character == ']')
            {
                builder.Append(character);
                continue;
            }

            builder.Append(atSegmentStart ? char.ToLowerInvariant(character) : character);
            atSegmentStart = false;
        }

        return builder.ToString();
    }
}
