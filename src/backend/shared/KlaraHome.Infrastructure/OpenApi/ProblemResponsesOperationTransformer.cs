using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace KlaraHome.Infrastructure.OpenApi;

/// <summary>
/// Guarantees two things on every operation: a client-friendly <c>operationId</c>, and the
/// error responses that any endpoint can return regardless of what it declares
/// (docs/04-api-specification.md §7).
/// </summary>
internal sealed class ProblemResponsesOperationTransformer : IOpenApiOperationTransformer
{
    private static readonly (string Status, string Description)[] UniversalResponses =
    [
        ("429", "Rate limited. Retry after the interval in the Retry-After header."),
        ("500", "Unexpected error. Carries a correlation id and never any internal detail."),
    ];

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        operation.OperationId ??= context.Description.ActionDescriptor.AttributeRouteInfo?.Name;

        operation.Responses ??= [];

        foreach (var (status, description) in UniversalResponses)
        {
            if (!operation.Responses.ContainsKey(status))
            {
                operation.Responses[status] = new OpenApiResponse { Description = description };
            }
        }

        return Task.CompletedTask;
    }
}
