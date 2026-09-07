using System.Globalization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace KlaraHome.Infrastructure.OpenApi;

/// <summary>
/// Declares every query parameter in <c>camelCase</c>, which is what the specification says the
/// query string looks like (docs/04-api-specification.md §1).
/// </summary>
/// <remarks>
/// <para>
/// A query record bound with <c>[AsParameters]</c> is declared under its property names, and those
/// are PascalCase because they are C# properties. The binder does not care — it matches
/// case-insensitively, so <c>?q=chair</c> has always reached <c>Q</c> — but the document is what the
/// Angular client is generated from, so the declared spelling is the spelling that goes on the wire.
/// Left alone, the storefront's most-shared URL would read <c>?Q=chair&amp;MinPrice=2000</c> while
/// this repository's own API specification documents <c>?q=chair&amp;minPrice=2000</c>.
/// </para>
/// <para>
/// Done here rather than by writing <c>[FromQuery(Name = "…")]</c> onto every property of every
/// query record: there are two dozen of them, the attribute would say nothing a convention cannot,
/// and the twenty-fifth would be the one that forgot. This is the same choice already made for
/// bodies, where a JSON naming policy camel-cases the properties rather than each one being
/// annotated.
/// </para>
/// <para>
/// <strong>Query parameters only.</strong> A path parameter's name must match the route template
/// character for character or the document is invalid, and a header's case belongs to the header —
/// <c>X-Correlation-Id</c> is not <c>x-Correlation-Id</c>. Anything already lowercase, and anything
/// a developer named explicitly — <c>attr.color</c> — passes through untouched.
/// </para>
/// </remarks>
internal sealed class QueryParameterCasingTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (operation.Parameters is null)
        {
            return Task.CompletedTask;
        }

        // Only the concrete parameter carries a settable name; a $ref to a shared one is left alone,
        // because renaming it here would rename it for every operation that points at it.
        foreach (var parameter in operation.Parameters.OfType<OpenApiParameter>())
        {
            if (parameter.In == ParameterLocation.Query && parameter.Name is { Length: > 0 } name)
            {
                parameter.Name = char.ToLower(name[0], CultureInfo.InvariantCulture) + name[1..];
            }
        }

        return Task.CompletedTask;
    }
}
