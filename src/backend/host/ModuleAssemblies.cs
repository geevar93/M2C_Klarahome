using System.Reflection;

namespace KlaraHome.Hosting;

/// <summary>
/// The module assemblies this platform is composed of. Adding a module means adding a project
/// reference to each host and one line here — the hosts never learn anything else about it.
/// </summary>
/// <remarks>
/// <para>
/// One file, linked into all three hosts rather than copied into each. The API, the worker and
/// the migrator must agree on the module set: a module the API serves but the migrator does not
/// know about is a module whose tables are never created, and that mistake would be invisible
/// until the first query against it failed in production.
/// </para>
/// <para>
/// A linked source file rather than a shared project, because the alternative is a project that
/// references every module and is itself referenced by every host — which is exactly the
/// dependency the module boundary rules exist to prevent
/// (<c>Infrastructure_never_depends_on_a_module</c>).
/// </para>
/// </remarks>
internal static class ModuleAssemblies
{
    public static readonly Assembly[] All =
    [
        typeof(Modules.Platform.PlatformModule).Assembly,
        typeof(Modules.Identity.IdentityModule).Assembly,
        typeof(Modules.Media.MediaModule).Assembly,
        typeof(Modules.Notifications.NotificationsModule).Assembly,
        typeof(Modules.Vendors.VendorsModule).Assembly,
        typeof(Modules.Catalog.CatalogModule).Assembly,
        typeof(Modules.Inventory.InventoryModule).Assembly,
        typeof(Modules.Pricing.PricingModule).Assembly,
        typeof(Modules.Carts.CartsModule).Assembly,
        typeof(Modules.Orders.OrdersModule).Assembly,
        typeof(Modules.Payments.PaymentsModule).Assembly,
        typeof(Modules.Shipping.ShippingModule).Assembly,
    ];
}
