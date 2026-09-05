using System.Text.RegularExpressions;

namespace KlaraHome.Modules.Notifications.Infrastructure.Templating;

/// <summary>Why an SMS template would be dropped by an Indian operator.</summary>
internal enum DltViolation
{
    /// <summary>Nothing wrong.</summary>
    None = 0,

    /// <summary>No DLT template id has been registered against it.</summary>
    MissingTemplateId = 1,

    /// <summary>The id is not the shape TRAI issues.</summary>
    MalformedTemplateId = 2,

    /// <summary>The body is longer than an operator will accept.</summary>
    BodyTooLong = 3,

    /// <summary>More variables than a registered template may declare.</summary>
    TooManyVariables = 4,

    /// <summary>A variable is longer than the 30 characters DLT allows.</summary>
    VariableTooLong = 5,
}

/// <summary>
/// The TRAI/DLT rules an Indian transactional SMS has to satisfy
/// (docs/08-integrations.md §3.1).
/// </summary>
/// <remarks>
/// <para>
/// These are checked here, before a provider is called, because the failure mode otherwise is the
/// worst kind: the operator does not reject a non-conforming message, it <b>drops</b> it. The API
/// returns success, the delivery report says nothing, and the customer simply never receives their
/// code. Every one of these rules exists because somebody has lost a day to that.
/// </para>
/// <para>
/// The platform cannot verify that our text matches the <em>registered</em> text — only the
/// operator holds that. What it can do is refuse the cases that are certainly wrong, and record
/// the id that was used so a mismatch is diagnosable.
/// </para>
/// </remarks>
internal static partial class DltRules
{
    /// <summary>Longest body an operator accepts for a single registered template.</summary>
    public const int MaxBodyLength = 1000;

    /// <summary>Most variables a registered template may carry.</summary>
    public const int MaxVariables = 10;

    /// <summary>Longest value a DLT variable may hold. Longer values are rejected by the operator.</summary>
    public const int MaxVariableLength = 30;

    /// <summary>Checks a template's registration and shape.</summary>
    /// <param name="body">The template body.</param>
    /// <param name="providerTemplateId">The registered DLT id.</param>
    public static DltViolation Validate(string body, string? providerTemplateId)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (string.IsNullOrWhiteSpace(providerTemplateId))
        {
            return DltViolation.MissingTemplateId;
        }

        if (!TemplateIdPattern().IsMatch(providerTemplateId.Trim()))
        {
            return DltViolation.MalformedTemplateId;
        }

        if (body.Length > MaxBodyLength)
        {
            return DltViolation.BodyTooLong;
        }

        return TemplateRenderer.PlaceholdersIn(body).Count > MaxVariables
            ? DltViolation.TooManyVariables
            : DltViolation.None;
    }

    /// <summary>Checks the values about to be substituted.</summary>
    /// <param name="variables">The values.</param>
    /// <param name="offending">The variable that broke the rule.</param>
    public static DltViolation ValidateValues(
        IReadOnlyDictionary<string, string> variables,
        out string? offending)
    {
        ArgumentNullException.ThrowIfNull(variables);

        foreach (var (name, value) in variables)
        {
            if (value.Length > MaxVariableLength)
            {
                offending = name;
                return DltViolation.VariableTooLong;
            }
        }

        offending = null;
        return DltViolation.None;
    }

    /// <summary>
    /// A DLT content-template id: digits, and long. Registrars issue 19-digit ids, but the length
    /// has changed before, so the check is a range rather than an exact count — tight enough to
    /// catch a placeholder left in configuration, loose enough not to reject a valid registration.
    /// </summary>
    [GeneratedRegex(@"^[0-9]{10,25}$", RegexOptions.CultureInvariant)]
    private static partial Regex TemplateIdPattern();
}
