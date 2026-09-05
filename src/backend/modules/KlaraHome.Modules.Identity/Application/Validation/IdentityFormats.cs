using System.Text.RegularExpressions;

namespace KlaraHome.Modules.Identity.Application.Validation;

/// <summary>
/// Indian mobile numbers, normalised to E.164 (docs/03-database-design.md §4.2).
/// </summary>
/// <remarks>
/// Customers type their number as ten digits, with a leading zero, with <c>+91</c>, or with spaces
/// and hyphens in between. All of those are the same number, and storing them as typed would mean
/// one person holding several accounts and a uniqueness constraint that never fires — so the
/// number is normalised before it is ever compared or stored.
/// </remarks>
internal static partial class IndianMobile
{
    /// <summary>India's country calling code.</summary>
    public const string CountryCode = "+91";

    /// <summary>Whether a value can be read as an Indian mobile number.</summary>
    /// <param name="value">The number as the caller typed it.</param>
    public static bool IsValid(string? value) => TryNormalize(value, out _);

    /// <summary>Normalises a number, or throws. Use after validation.</summary>
    /// <param name="value">The number as the caller typed it.</param>
    /// <exception cref="ArgumentException">The value is not an Indian mobile number.</exception>
    public static string Normalize(string? value)
        => TryNormalize(value, out var normalized)
            ? normalized
            : throw new ArgumentException($"'{value}' is not an Indian mobile number.", nameof(value));

    /// <summary>Normalises a number to E.164, or returns false.</summary>
    /// <param name="value">The number as the caller typed it.</param>
    /// <param name="normalized">The number as <c>+91XXXXXXXXXX</c>.</param>
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var digits = new string([.. value.Where(char.IsAsciiDigit)]);

        // 91 is stripped only from a 12-digit number: an Indian mobile number can itself start
        // with 91, and dropping those two digits from a ten-digit number would silently corrupt it.
        if (digits.Length == 12 && digits.StartsWith("91", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }
        else if (digits.Length == 11 && digits[0] == '0')
        {
            digits = digits[1..];
        }

        // TRAI allocates mobile numbers in the 6-9 series; 10 digits, no exceptions.
        if (digits.Length != 10 || digits[0] is < '6' or > '9')
        {
            return false;
        }

        normalized = CountryCode + digits;
        return true;
    }
}

/// <summary>Formats this module validates but does not own.</summary>
internal static partial class IdentityFormats
{
    /// <summary>
    /// GSTIN: two state-code digits, a ten-character PAN, an entity digit, a fixed 'Z', and a
    /// checksum character. Validated by shape only — the checksum belongs to the Pricing module,
    /// which is where a wrong GSTIN becomes a wrong invoice.
    /// </summary>
    [GeneratedRegex("^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][0-9A-Z]Z[0-9A-Z]$", RegexOptions.CultureInvariant)]
    public static partial Regex Gstin();

    /// <summary>A six-digit Indian PIN code. The first digit is never zero.</summary>
    [GeneratedRegex("^[1-9][0-9]{5}$", RegexOptions.CultureInvariant)]
    public static partial Regex Pincode();

    /// <summary>
    /// An email address, checked for shape only.
    /// </summary>
    /// <remarks>
    /// Deliberately permissive. The only test that establishes an address is real is sending
    /// something to it and having somebody click, which is what the verification flow does; a
    /// stricter pattern here would reject valid addresses in exchange for nothing.
    /// </remarks>
    [GeneratedRegex("^[^@\\s]+@[^@\\s.]+(\\.[^@\\s.]+)+$", RegexOptions.CultureInvariant)]
    public static partial Regex Email();
}
