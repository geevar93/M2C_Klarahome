using System.Reflection;
using FluentValidation;
using KlaraHome.Infrastructure.Messaging.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KlaraHome.Infrastructure.Messaging;

/// <summary>Registers the dispatcher, its behaviours, and every handler and validator found.</summary>
public static class MessagingExtensions
{
    private static readonly Type[] HandlerInterfaces =
    [
        typeof(ICommandHandler<>),
        typeof(ICommandHandler<,>),
        typeof(IQueryHandler<,>),
    ];

    /// <summary>
    /// Scans <paramref name="assemblies"/> for handlers and validators. Behaviour order is the
    /// registration order: logging wraps validation, so a rejected request is logged too.
    /// </summary>
    public static IServiceCollection AddMessaging(this IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        services.TryAddScoped<IDispatcher, Dispatcher>();

        services.TryAddEnumerable(ServiceDescriptor.Scoped(
            typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(
            typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>)));

        foreach (var assembly in assemblies.Distinct())
        {
            RegisterHandlers(services, assembly);
            services.AddValidatorsFromAssembly(assembly, ServiceLifetime.Scoped, includeInternalTypes: true);
        }

        return services;
    }

    private static void RegisterHandlers(IServiceCollection services, Assembly assembly)
    {
        var candidates = assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false });

        foreach (var implementation in candidates)
        {
            var closedInterfaces = implementation
                .GetInterfaces()
                .Where(contract => contract.IsGenericType
                                   && HandlerInterfaces.Contains(contract.GetGenericTypeDefinition()));

            foreach (var contract in closedInterfaces)
            {
                services.TryAddScoped(contract, implementation);
            }
        }
    }
}
