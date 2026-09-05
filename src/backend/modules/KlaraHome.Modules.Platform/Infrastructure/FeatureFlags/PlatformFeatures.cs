using KlaraHome.Modules.Platform.Domain;

namespace KlaraHome.Modules.Platform.Infrastructure.FeatureFlags;

/// <summary>One flag as the module declares it, before an operator has changed anything.</summary>
/// <param name="Key">The dotted key.</param>
/// <param name="Enabled">Whether it ships on.</param>
/// <param name="Description">What it controls, shown in the admin UI.</param>
internal sealed record FeatureFlagDeclaration(string Key, bool Enabled, string Description);

/// <summary>
/// The feature flags the Platform module owns.
/// </summary>
/// <remarks>
/// A flag is declared in code and seeded into the table; an operator then toggles the row. Declaring
/// them means the admin UI lists every switch that exists rather than only the ones somebody has
/// already touched, and it means a flag key is a compile-time constant at the point it is checked.
/// Each module declares its own; nothing here is a general-purpose registry for other modules.
/// </remarks>
internal static class PlatformFeatures
{
    /// <summary>
    /// Gates <c>GET /api/v1/store/config</c>. Turning it off takes the anonymous configuration
    /// document off the internet without a deploy — the switch to reach for if a section is ever
    /// found to be leaking something it should not.
    /// </summary>
    public const string PublicStoreConfig = "platform.public-store-config";

    /// <summary>
    /// Gates <c>GET /api/v1/store/pincodes/{pincode}</c>. The PIN code dataset is imported by an
    /// operator rather than shipped, so a deployment that has not imported it yet turns the lookup
    /// off instead of answering "no such PIN code" to every valid one.
    /// </summary>
    public const string PincodeLookup = "platform.pincode-lookup";

    /// <summary>Every flag this module declares, seeded on each deploy.</summary>
    public static IReadOnlyList<FeatureFlagDeclaration> All { get; } =
    [
        new(PublicStoreConfig, true, "Serve the anonymous store configuration document to the storefront."),
        new(PincodeLookup, true, "Serve PIN code lookups for address autofill."),
    ];

    /// <summary>The flag rows a fresh deployment starts with.</summary>
    public static IEnumerable<FeatureFlag> Declared()
        => All.Select(flag => FeatureFlag.Declare(flag.Key, flag.Enabled, flag.Description));
}
