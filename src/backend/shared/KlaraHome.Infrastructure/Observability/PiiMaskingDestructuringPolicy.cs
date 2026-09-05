using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Serilog.Core;
using Serilog.Events;

namespace KlaraHome.Infrastructure.Observability;

/// <summary>
/// Destructures objects for logging with personal data masked. Registered globally, so masking
/// happens whether or not the caller thought about it.
/// </summary>
internal sealed class PiiMaskingDestructuringPolicy : IDestructuringPolicy
{
    private static readonly ConcurrentDictionary<Type, PropertyPlan[]> Plans = new();

    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(propertyValueFactory);

        var type = value.GetType();

        if (!IsOwnType(type))
        {
            result = null;
            return false;
        }

        var plans = Plans.GetOrAdd(type, BuildPlan);
        if (plans.Length == 0)
        {
            result = null;
            return false;
        }

        var properties = new List<LogEventProperty>(plans.Length);

        foreach (var plan in plans)
        {
            var raw = plan.Read(value);
            var logged = plan.Mask is { } mask
                ? new ScalarValue(PiiMasker.Apply(raw?.ToString(), mask))
                : propertyValueFactory.CreatePropertyValue(raw, destructureObjects: true);

            properties.Add(new LogEventProperty(plan.Name, logged));
        }

        result = new StructureValue(properties, type.Name);
        return true;
    }

    /// <summary>Only our own types are inspected; framework and third-party types are left alone.</summary>
    private static bool IsOwnType(Type type)
        => type.FullName?.StartsWith("KlaraHome.", StringComparison.Ordinal) == true;

    private static PropertyPlan[] BuildPlan(Type type)
        => [.. type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .Select(property => new PropertyPlan(
                property.Name,
                property,
                property.GetCustomAttribute<PiiAttribute>()?.Mask
                ?? (PiiMasker.AlwaysMaskedNames.Contains(property.Name) ? PiiMask.Full : null)))];

    private sealed record PropertyPlan(string Name, PropertyInfo Property, PiiMask? Mask)
    {
        public object? Read(object instance)
        {
            try
            {
                return Property.GetValue(instance);
            }
            catch (TargetInvocationException)
            {
                // A property that throws must not take the log line down with it.
                return null;
            }
        }
    }
}
