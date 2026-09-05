using KlaraHome.Infrastructure.Configuration;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace KlaraHome.Infrastructure.OpenApi;

/// <summary>Fills in the document-level metadata a generated client needs.</summary>
internal sealed class ApiDocumentTransformer(ApiOptions api) : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Info = new OpenApiInfo
        {
            Title = "Klara Home API",
            Version = api.Version,
            Description =
                "REST API for the Klara Home multi-vendor commerce platform. "
                + "Errors follow RFC 9457 and carry a stable machine-readable code; "
                + "clients switch on that code, never on the message text.",
        };

        return Task.CompletedTask;
    }
}
