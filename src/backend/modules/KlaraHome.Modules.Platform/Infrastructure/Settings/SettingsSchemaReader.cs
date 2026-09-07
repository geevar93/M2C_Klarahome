using System.Collections;
using System.Globalization;
using System.Reflection;
using FluentValidation;
using FluentValidation.Validators;
using KlaraHome.Modules.Platform.Application.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Platform.Infrastructure.Settings;

/// <summary>
/// Reads a settings section's schema off the two things that already define it: the CLR record,
/// which says what shape a value has, and the FluentValidation rules, which say what a valid one is.
/// </summary>
/// <remarks>
/// <para>
/// Serving the schema rather than compiling it into the admin app is the same decision the CMS
/// block types made, for the same reason: the store-settings screen walks the JSON it was returned
/// and renders a control per leaf, so without this it knows a field is a number but not that the
/// number must be positive, and the operator discovers the rule by being refused.
/// </para>
/// <para>
/// It is deliberately a <em>description</em> rather than a second validator. Everything here is
/// advisory — the server still runs the real rules on every write — so a rule this reader cannot
/// express (a conditional <c>When</c>, a cross-field comparison) is simply left out rather than
/// approximated. An approximated rule is worse than a missing one: the form would refuse a value
/// the server accepts, and nobody would be able to say which was right.
/// </para>
/// </remarks>
internal static class SettingsSchemaReader
{
    /// <summary>
    /// How deep a nested section is walked. Settings sections are one or two levels deep; the cap
    /// exists so a type that ever referred to itself produces a short answer rather than a hang.
    /// </summary>
    private const int MaxDepth = 3;

    /// <summary>Describes one section.</summary>
    /// <param name="sectionType">The section's CLR type.</param>
    /// <param name="validator">The section's validator, or null if it has none.</param>
    public static IReadOnlyList<SettingsFieldSchema> Describe(Type sectionType, IValidator? validator)
    {
        ArgumentNullException.ThrowIfNull(sectionType);

        var rules = RulesOf(validator);
        var fields = new List<SettingsFieldSchema>();

        Walk(sectionType, prefix: string.Empty, depth: 0, rules, fields);
        return fields;
    }

    private static void Walk(
        Type type,
        string prefix,
        int depth,
        Dictionary<string, Constraints> rules,
        List<SettingsFieldSchema> fields)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var name = prefix.Length == 0
                ? Camel(property.Name)
                : prefix + "." + Camel(property.Name);

            var (kind, isList, elementType) = Classify(property.PropertyType);

            // A nested object is expanded rather than described as "object", because the screen
            // renders a control per leaf and an unexpanded object is a leaf it cannot draw. A list
            // of objects is not expanded: its shape repeats, and one path cannot name two rows.
            if (kind == FieldKinds.Object && !isList && depth + 1 < MaxDepth)
            {
                Walk(elementType, name, depth + 1, rules, fields);
                continue;
            }

            var constraint = rules.GetValueOrDefault(prefix.Length == 0 ? property.Name : prefix + "." + property.Name);

            fields.Add(new SettingsFieldSchema(
                name,
                kind,
                constraint?.IsRequired ?? false,
                isList,
                constraint?.Minimum,
                constraint?.Maximum,
                constraint?.MaxLength,
                constraint?.Pattern,
                ChoicesOf(elementType)));
        }
    }

    /// <summary>Turns a CLR type into the kind of control the screen should draw.</summary>
    private static (string Kind, bool IsList, Type ElementType) Classify(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        var isList = false;

        if (underlying != typeof(string) && typeof(IEnumerable).IsAssignableFrom(underlying))
        {
            var element = underlying.IsArray
                ? underlying.GetElementType()
                : underlying.GetInterfaces()
                    .FirstOrDefault(candidate => candidate.IsGenericType
                                                 && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    ?.GetGenericArguments()[0];

            if (element is not null)
            {
                isList = true;
                underlying = Nullable.GetUnderlyingType(element) ?? element;
            }
        }

        var kind = underlying switch
        {
            _ when underlying.IsEnum => FieldKinds.Choice,
            _ when underlying == typeof(string) => FieldKinds.Text,
            _ when underlying == typeof(bool) => FieldKinds.Boolean,
            _ when underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(short) =>
                FieldKinds.Integer,
            _ when underlying == typeof(decimal) || underlying == typeof(double) || underlying == typeof(float) =>
                FieldKinds.Number,
            _ when underlying == typeof(DateTimeOffset) || underlying == typeof(DateTime) => FieldKinds.DateTime,
            _ when underlying == typeof(DateOnly) => FieldKinds.Date,
            _ when underlying == typeof(Guid) => FieldKinds.Id,
            _ when underlying.IsClass || underlying.IsValueType && !underlying.IsPrimitive => FieldKinds.Object,
            _ => FieldKinds.Text,
        };

        return (kind, isList, underlying);
    }

    private static string[]? ChoicesOf(Type type)
        => type.IsEnum ? Enum.GetNames(type) : null;

    /// <summary>
    /// Reads the constraints FluentValidation already holds, for the rule kinds that describe a
    /// single field on their own.
    /// </summary>
    /// <remarks>
    /// Rules carrying a condition are skipped: <c>When(...)</c> means the rule applies sometimes,
    /// and a form that enforced it always would refuse values the server accepts.
    /// </remarks>
    private static Dictionary<string, Constraints> RulesOf(IValidator? validator)
    {
        var constraints = new Dictionary<string, Constraints>(StringComparer.Ordinal);

        if (validator is null)
        {
            return constraints;
        }

        foreach (var rule in validator.CreateDescriptor().Rules)
        {
            if (rule.PropertyName is not { Length: > 0 } property)
            {
                continue;
            }

            foreach (var component in rule.Components)
            {
                if (component.HasCondition || component.HasAsyncCondition)
                {
                    continue;
                }

                var entry = constraints.TryGetValue(property, out var existing) ? existing : new Constraints();
                Apply(entry, component.Validator);
                constraints[property] = entry;
            }
        }

        return constraints;
    }

    /// <summary>
    /// Folds one validator into a field's constraints.
    /// </summary>
    /// <remarks>
    /// The bounds and the length are read off the concrete validators by name rather than through
    /// an interface, because FluentValidation exposes only <see cref="IComparisonValidator"/>
    /// publicly and the length and range validators carry their numbers on plain properties. The
    /// lookup is by property name and it fails softly: a validator that does not carry what is
    /// expected contributes nothing, which is the same outcome as a rule this reader does not know.
    /// </remarks>
    private static void Apply(Constraints entry, IPropertyValidator validator)
    {
        switch (validator.Name)
        {
            case "NotNullValidator":
            case "NotEmptyValidator":
                entry.IsRequired = true;
                break;

            case "MaximumLengthValidator":
            case "ExactLengthValidator":
            case "LengthValidator":
                entry.MaxLength = Number(validator, "Max") is { } max ? (int)max : entry.MaxLength;
                break;

            case "RegularExpressionValidator":
                entry.Pattern ??= Text(validator, "Expression");
                break;

            case "InclusiveBetweenValidator":
            case "ExclusiveBetweenValidator":
                entry.Minimum ??= Number(validator, "From");
                entry.Maximum ??= Number(validator, "To");
                break;

            default:
                if (validator is IComparisonValidator comparison)
                {
                    ApplyComparison(entry, comparison);
                }

                break;
        }
    }

    private static void ApplyComparison(Constraints entry, IComparisonValidator comparison)
    {
        if (comparison.ValueToCompare is not { } value || !TryNumber(value, out var bound))
        {
            return;
        }

        switch (comparison.Comparison)
        {
            case Comparison.GreaterThan:
            case Comparison.GreaterThanOrEqual:
                entry.Minimum ??= bound;
                break;

            case Comparison.LessThan:
            case Comparison.LessThanOrEqual:
                entry.Maximum ??= bound;
                break;

            default:
                break;
        }
    }

    private static decimal? Number(IPropertyValidator validator, string property)
    {
        var value = validator.GetType().GetProperty(property)?.GetValue(validator);
        return value is not null && TryNumber(value, out var number) ? number : null;
    }

    private static string? Text(IPropertyValidator validator, string property)
        => validator.GetType().GetProperty(property)?.GetValue(validator) as string;

    private static bool TryNumber(object value, out decimal number)
    {
        try
        {
            number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            number = 0;
            return false;
        }
    }

    /// <summary>The JSON spelling of a property name, matching <c>JsonSerializerDefaults.Web</c>.</summary>
    private static string Camel(string name)
        => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    /// <summary>What one field's rules add up to. Mutable while it is being folded together.</summary>
    private sealed class Constraints
    {
        public bool IsRequired { get; set; }

        public decimal? Minimum { get; set; }

        public decimal? Maximum { get; set; }

        public int? MaxLength { get; set; }

        public string? Pattern { get; set; }
    }
}

/// <summary>The control kinds a settings field can be, as the admin form reads them.</summary>
internal static class FieldKinds
{
    /// <summary>A single-line or multi-line string.</summary>
    public const string Text = "text";

    /// <summary>A whole number.</summary>
    public const string Integer = "integer";

    /// <summary>A number with a fractional part — money, a rate, a weight.</summary>
    public const string Number = "number";

    /// <summary>A switch.</summary>
    public const string Boolean = "boolean";

    /// <summary>An instant.</summary>
    public const string DateTime = "datetime";

    /// <summary>A calendar day.</summary>
    public const string Date = "date";

    /// <summary>An identifier of something else.</summary>
    public const string Id = "id";

    /// <summary>One of a fixed set of words.</summary>
    public const string Choice = "choice";

    /// <summary>A nested object. Only ever seen on a list, since a scalar one is expanded.</summary>
    public const string Object = "object";
}
