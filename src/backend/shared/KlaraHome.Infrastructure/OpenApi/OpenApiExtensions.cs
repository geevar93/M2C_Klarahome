using KlaraHome.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Infrastructure.OpenApi;

/// <summary>
/// OpenAPI 3.1 document generation. The document is the contract: the Angular client is generated
/// from it and CI fails if the committed client drifts (docs/04-api-specification.md §7).
/// </summary>
public static class OpenApiExtensions
{
    /// <summary>Route the document is served from.</summary>
    public const string DocumentRoute = "/openapi/{documentName}.json";

    public static IServiceCollection AddKlaraHomeOpenApi(this IServiceCollection services, ApiOptions api)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(api);

        services.AddOpenApi(api.Version, options =>
        {
            options.AddDocumentTransformer(new ApiDocumentTransformer(api));
            options.AddOperationTransformer(new ProblemResponsesOperationTransformer());
        });

        return services;
    }
}
