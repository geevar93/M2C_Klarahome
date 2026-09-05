using System.Reflection;

namespace KlaraHome.Api;

/// <summary>
/// The module assemblies this host composes. Adding a module means adding a project reference
/// and one line here — the host never learns anything else about it.
/// </summary>
internal static class ModuleAssemblies
{
    public static readonly Assembly[] All =
    [
        typeof(KlaraHome.Modules.Platform.PlatformModule).Assembly,
    ];
}
