namespace KlaraHome.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Refuses passwords that are already in every attacker's word list
/// (docs/07-security-compliance.md §1).
/// </summary>
/// <remarks>
/// <para>
/// A compiled-in list of the passwords that actually appear at the top of credential dumps, plus
/// the ones this deployment invites — the store name, the product name — because "klarahome123" is
/// the first thing a new administrator types and it is in no public breach list at all.
/// </para>
/// <para>
/// This is deliberately not the Have I Been Pwned range API. That call is an outbound HTTP request
/// on the registration path, which needs the SSRF allow-list from §3, a timeout policy, a decision
/// about what to do when it is down, and a test suite that does not depend on the internet. It is
/// the right thing to add, and it is added by the step that builds the outbound HTTP policy — with
/// this list staying as the offline floor underneath it.
/// </para>
/// </remarks>
internal sealed class BreachedPasswords
{
    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        "123456", "123456789", "12345678", "1234567890", "1234567", "password", "password1",
        "password123", "qwerty", "qwerty123", "qwertyuiop", "abc123", "abcd1234", "111111",
        "123123", "000000", "iloveyou", "admin", "admin123", "administrator", "welcome",
        "welcome1", "welcome123", "letmein", "monkey", "dragon", "sunshine", "princess",
        "football", "baseball", "master", "shadow", "michael", "superman", "trustno1",
        "passw0rd", "p@ssw0rd", "changeme", "change-me", "changeit", "secret", "default",
        "temp1234", "test1234", "asdfghjkl", "zxcvbnm", "1q2w3e4r", "1qaz2wsx", "qazwsxedc",

        // India's own recurring entries, which a global list under-weights.
        "india123", "indian123", "krishna", "ganesh", "bharat123", "namaste123", "chennai123",
        "mumbai123", "delhi123", "bangalore", "9876543210",

        // What this deployment invites by existing.
        "klarahome", "klarahome1", "klarahome123", "klara123", "klarahome@123",
    };

    /// <summary>Whether a password is one nobody should be allowed to choose.</summary>
    /// <remarks>
    /// A trailing run of digits is stripped before the comparison, so <c>dragon2024</c> is refused
    /// for the same reason <c>dragon</c> is: appending a year is the first thing a cracking rule
    /// tries, and it buys the account nothing.
    /// </remarks>
    /// <param name="password">The candidate password.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "An instance member on purpose: this is the seam the range-API lookup "
                        + "replaces, and that implementation will hold an HttpClient.")]
    public bool IsBreached(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        if (Known.Contains(password))
        {
            return true;
        }

        var trimmed = password.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '!', '@', '#');

        return trimmed.Length >= 4 && Known.Contains(trimmed);
    }
}
