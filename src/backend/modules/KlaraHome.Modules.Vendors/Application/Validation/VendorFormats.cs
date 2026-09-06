using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KlaraHome.Modules.Vendors.Application.Validation;

/// <summary>
/// The Indian identifier formats this module validates before it stores anything.
/// </summary>
/// <remarks>
/// <para>
/// Source-generated regular expressions: compiled at build time, so there is no pattern parsing at
/// startup and no cache to miss. Every pattern is anchored — an unanchored check would accept
/// <c>not-a-pan-ABCDE1234F-either</c>.
/// </para>
/// <para>
/// These are shape checks, not proof. A well-formed GSTIN belongs to somebody, but not necessarily
/// to the person typing it; that is what the KYC document and its human reviewer are for. What
/// this prevents is a malformed number reaching a tax invoice, where it is the platform's problem.
/// </para>
/// </remarks>
internal static partial class VendorFormats
{
    /// <summary>
    /// PAN: five letters, four digits, a letter. The fourth character encodes the holder type —
    /// <c>P</c> for an individual, <c>C</c> for a company — which is why it is not simply
    /// <c>[A-Z]{5}</c>.
    /// </summary>
    [GeneratedRegex(@"^[A-Z]{5}[0-9]{4}[A-Z]$", RegexOptions.CultureInvariant)]
    public static partial Regex Pan();

    /// <summary>
    /// GSTIN: a two-digit state code, the holder's ten-character PAN, an entity number, the
    /// literal <c>Z</c>, and a checksum character.
    /// </summary>
    [GeneratedRegex(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][0-9A-Z]Z[0-9A-Z]$", RegexOptions.CultureInvariant)]
    public static partial Regex Gstin();

    /// <summary>IFSC: four letters, a zero, then six alphanumerics identifying the branch.</summary>
    [GeneratedRegex(@"^[A-Z]{4}0[0-9A-Z]{6}$", RegexOptions.CultureInvariant)]
    public static partial Regex Ifsc();

    /// <summary>A bank account number: nine to eighteen digits, which covers every Indian bank.</summary>
    [GeneratedRegex(@"^[0-9]{9,18}$", RegexOptions.CultureInvariant)]
    public static partial Regex BankAccountNumber();

    /// <summary>An Indian PIN code. Never begins with zero.</summary>
    [GeneratedRegex(@"^[1-9][0-9]{5}$", RegexOptions.CultureInvariant)]
    public static partial Regex Pincode();

    /// <summary>A PIN code prefix: two to six digits, the first non-zero.</summary>
    [GeneratedRegex(@"^[1-9][0-9]{1,5}$", RegexOptions.CultureInvariant)]
    public static partial Regex PincodePrefix();

    /// <summary>A vendor or plan code, and a storefront slug: lowercase, digits and hyphens.</summary>
    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    public static partial Regex Slug();

    /// <summary>
    /// The characters a slug may be built from, once everything else has been removed.
    /// </summary>
    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonSlug();

    /// <summary>
    /// Whether the state code a GSTIN begins with is one that exists.
    /// </summary>
    /// <remarks>
    /// The codes run 01–38, plus 97 for "other territory" and 99 for a centralised registration.
    /// Worth checking separately from the shape, because the first two digits are the ones that
    /// decide CGST + SGST versus IGST — a GSTIN that is well-formed but claims state 55 would put
    /// the wrong tax on every invoice the seller issues.
    /// </remarks>
    /// <param name="gstin">A GSTIN that has already passed <see cref="Gstin"/>.</param>
    public static bool HasKnownStateCode(string gstin)
    {
        if (string.IsNullOrWhiteSpace(gstin) || gstin.Length < 2)
        {
            return false;
        }

        return int.TryParse(gstin.AsSpan(0, 2), CultureInfo.InvariantCulture, out var code)
               && (code is >= 1 and <= 38 || code is 97 or 99);
    }

    /// <summary>
    /// Whether a PAN is consistent with the GSTIN that claims to embed it.
    /// </summary>
    /// <remarks>
    /// Characters three to twelve of a GSTIN <em>are</em> the holder's PAN. Checking the two against
    /// each other costs nothing and catches the commonest onboarding mistake there is: a seller who
    /// pasted their accountant's GSTIN, or their own from a different entity.
    /// </remarks>
    /// <param name="pan">The PAN, normalised to upper case.</param>
    /// <param name="gstin">The GSTIN, normalised to upper case.</param>
    public static bool PanMatchesGstin(string? pan, string? gstin)
    {
        if (string.IsNullOrWhiteSpace(pan) || string.IsNullOrWhiteSpace(gstin) || gstin.Length != 15)
        {
            // Nothing to contradict. The individual format rules have already had their say.
            return true;
        }

        return string.Equals(gstin.Substring(2, 10), pan, StringComparison.Ordinal);
    }

    /// <summary>
    /// The accented Latin letters this folds to their unaccented form, as pairs of
    /// "characters" and "what each becomes".
    /// </summary>
    /// <remarks>
    /// An explicit table rather than Unicode decomposition. This product runs with
    /// <c>InvariantGlobalization</c>, where <see cref="string.Normalize(NormalizationForm)"/> does
    /// not decompose — so a normalise-and-strip-marks implementation silently turns "Café" into
    /// "caf-" instead of "cafe", which is the kind of bug that is only ever found by the one seller
    /// it affects. A table is deterministic, needs no ICU, and covers the accented Latin a seller
    /// name in this market realistically carries.
    /// </remarks>
    private const string Accented = "àáâãäåāăąèéêëēĕėęěìíîïĩīĭįòóôõöøōŏőùúûüũūŭůűñńņňçćĉċčýÿŷšśŝžźżđðþß";

    private static readonly string[] Folded =
    [
        "a", "a", "a", "a", "a", "a", "a", "a", "a",
        "e", "e", "e", "e", "e", "e", "e", "e", "e",
        "i", "i", "i", "i", "i", "i", "i", "i",
        "o", "o", "o", "o", "o", "o", "o", "o", "o",
        "u", "u", "u", "u", "u", "u", "u", "u", "u",
        "n", "n", "n", "n",
        "c", "c", "c", "c", "c",
        "y", "y", "y",
        "s", "s", "s",
        "z", "z", "z",
        "d", "d", "th", "ss",
    ];

    /// <summary>
    /// Turns a display name into a storefront slug.
    /// </summary>
    /// <remarks>
    /// Diacritics are folded rather than dropped, so "Café Décor" becomes <c>cafe-decor</c> instead
    /// of <c>caf-d-cor</c>. Anything else that is not a letter or a digit — an ampersand, a comma,
    /// a script this table does not cover — becomes a separator, and runs of separators collapse.
    /// The result is a suggestion: uniqueness is settled by the handler, which is the only place
    /// that can see the other sellers.
    /// </remarks>
    /// <param name="value">The name to derive a slug from.</param>
    /// <param name="maxLength">The longest slug to produce.</param>
    public static string ToSlug(string value, int maxLength = 140)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var lowered = value.Trim().ToLowerInvariant();
        var folded = new StringBuilder(lowered.Length);

        foreach (var character in lowered)
        {
            var index = Accented.IndexOf(character, StringComparison.Ordinal);
            folded.Append(index >= 0 ? Folded[index] : character);
        }

        var slug = NonSlug().Replace(folded.ToString(), "-").Trim('-');

        return slug.Length <= maxLength ? slug : slug[..maxLength].TrimEnd('-');
    }
}
