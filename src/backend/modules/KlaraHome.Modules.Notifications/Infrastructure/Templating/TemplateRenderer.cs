using System.Text;
using System.Text.RegularExpressions;

namespace KlaraHome.Modules.Notifications.Infrastructure.Templating;

/// <summary>What came out of a render, or what stopped it.</summary>
/// <param name="Text">The rendered text, when it rendered.</param>
/// <param name="MissingVariables">Placeholders the caller did not supply. Non-empty means failure.</param>
internal sealed record RenderResult(string? Text, IReadOnlyList<string> MissingVariables)
{
    /// <summary>Whether every placeholder was satisfied.</summary>
    public bool IsSuccess => MissingVariables.Count == 0;
}

/// <summary>
/// Substitutes <c>{{placeholder}}</c> values into a template.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not a templating language. There are no conditionals, no loops and no expressions,
/// and that is the feature rather than the limitation: India's DLT rules require the text of a
/// registered SMS template to match what was approved, with only the declared variables differing,
/// so a template that could branch is a template whose output cannot be checked against a
/// registration. It also removes a whole class of injection from operator-editable content.
/// </para>
/// <para>
/// A missing variable is a failure, never an empty string. "Your order  has shipped" is worse than
/// no message at all, and silently rendering it would make a caller's mistake invisible.
/// </para>
/// </remarks>
internal static partial class TemplateRenderer
{
    /// <summary>Renders a template against a set of variables.</summary>
    /// <param name="template">The template text.</param>
    /// <param name="variables">The values to substitute.</param>
    /// <param name="escapeHtml">
    /// Whether substituted values are HTML-encoded. True for an email body, false for SMS —
    /// encoding an ampersand into <c>&amp;amp;</c> in a text message is a visible defect.
    /// </param>
    public static RenderResult Render(
        string template,
        IReadOnlyDictionary<string, string> variables,
        bool escapeHtml)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(variables);

        List<string>? missing = null;
        var output = new StringBuilder(template.Length + 64);
        var position = 0;

        foreach (var match in PlaceholderPattern().EnumerateMatches(template))
        {
            output.Append(template, position, match.Index - position);

            var name = template.Substring(match.Index + 2, match.Length - 4).Trim();

            if (variables.TryGetValue(name, out var value))
            {
                output.Append(escapeHtml ? System.Net.WebUtility.HtmlEncode(value) : value);
            }
            else
            {
                (missing ??= []).Add(name);
            }

            position = match.Index + match.Length;
        }

        output.Append(template, position, template.Length - position);

        return missing is null
            ? new RenderResult(output.ToString(), [])
            : new RenderResult(null, missing);
    }

    /// <summary>The placeholder names a template uses, in the order they appear, without duplicates.</summary>
    /// <param name="template">The template text.</param>
    public static IReadOnlyList<string> PlaceholdersIn(string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var names = new List<string>();

        foreach (var match in PlaceholderPattern().EnumerateMatches(template))
        {
            var name = template.Substring(match.Index + 2, match.Length - 4).Trim();

            if (!names.Contains(name, StringComparer.Ordinal))
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>
    /// <c>{{name}}</c>, with optional inner whitespace. Letters, digits, underscore and dot only —
    /// a placeholder is a variable name, and anything else in those braces is a typo that should
    /// stay visible in the output rather than be interpreted.
    /// </summary>
    [GeneratedRegex(@"\{\{\s*[A-Za-z0-9_.]+\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();
}
