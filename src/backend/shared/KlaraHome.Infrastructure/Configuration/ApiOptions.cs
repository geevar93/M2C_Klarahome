using System.ComponentModel.DataAnnotations;

namespace KlaraHome.Infrastructure.Configuration;

/// <summary>Host-level switches for the HTTP API.</summary>
public sealed class ApiOptions
{
    public const string SectionName = "Api";

    /// <summary>Version segment of the base path. A breaking change means a new document, not an edit.</summary>
    [Required]
    [RegularExpression("^v[0-9]+$", ErrorMessage = "Api version must look like v1.")]
    public string Version { get; set; } = "v1";

    /// <summary>
    /// Serves the OpenAPI document and the Scalar reference UI. Forced off in Production by the
    /// host regardless of this value.
    /// </summary>
    public bool EnableApiReference { get; set; } = true;

    /// <summary>Path the Scalar reference UI is served from.</summary>
    [Required]
    public string ApiReferencePath { get; set; } = "/scalar";

    /// <summary>
    /// Exposes the Development-only diagnostics endpoints used to prove the error contract.
    /// Never enabled outside Development.
    /// </summary>
    public bool EnableDiagnosticsEndpoints { get; set; }

    /// <summary>
    /// Honour X-Forwarded-For / -Proto. The API publishes no ports of its own, so the only
    /// possible source of these headers is Traefik on the internal network
    /// (docs/06-infrastructure-devops.md §1). Turn off if that ever stops being true.
    /// </summary>
    public bool TrustProxyHeaders { get; set; } = true;

    /// <summary>The versioned prefix every module endpoint is mapped beneath.</summary>
    public string BasePath => "/api/" + Version;
}
