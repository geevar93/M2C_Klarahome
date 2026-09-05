using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace KlaraHome.Infrastructure.Observability.Enrichers;

/// <summary>
/// Derives the owning module from the logger source context, so log volume can be attributed and
/// verbosity raised for one module without touching the rest.
/// </summary>
internal sealed class ModuleEnricher : ILogEventEnricher
{
    private const string ModulesNamespace = "KlaraHome.Modules.";

    private static readonly ConcurrentDictionary<string, string?> Cache = new(StringComparer.Ordinal);

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        if (!logEvent.Properties.TryGetValue("SourceContext", out var value)
            || value is not ScalarValue { Value: string sourceContext })
        {
            return;
        }

        var module = Cache.GetOrAdd(sourceContext, static context =>
        {
            if (!context.StartsWith(ModulesNamespace, StringComparison.Ordinal))
            {
                return null;
            }

            var remainder = context[ModulesNamespace.Length..];
            var dot = remainder.IndexOf('.', StringComparison.Ordinal);
            return dot < 0 ? remainder : remainder[..dot];
        });

        if (module is not null)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("module", module));
        }
    }
}
