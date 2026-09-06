using System.Text;
using System.Text.RegularExpressions;

namespace KlaraHome.Modules.Catalog.Application.Validation;

/// <summary>
/// The formats this module validates before it stores anything.
/// </summary>
/// <remarks>
/// <para>
/// Source-generated regular expressions: compiled at build time, so there is no pattern parsing at
/// startup and no cache to miss. Every pattern is anchored — an unanchored check would accept
/// <c>not-an-hsn-6109-either</c>.
/// </para>
/// <para>
/// <see cref="ToSlug"/> is the same algorithm the Vendors module uses, and the duplication is
/// deliberate: a module may not reference another module, and the alternative — promoting a
/// slugger into the shared kernel — would put a presentation concern in the layer that is supposed
/// to hold only domain primitives. Two thirty-line copies are cheaper than that boundary breach.
/// </para>
/// </remarks>
internal static partial class CatalogFormats
{
    /// <summary>
    /// An HSN code: four, six or eight digits.
    /// </summary>
    /// <remarks>
    /// The length is not cosmetic. Under GST, a supplier below ₹5 crore turnover declares four
    /// digits and one above it declares six; eight is the full customs tariff line. Five or seven
    /// digits is always a typo, and it is a typo that lands on a tax invoice.
    /// </remarks>
    [GeneratedRegex(@"^([0-9]{4}|[0-9]{6}|[0-9]{8})$", RegexOptions.CultureInvariant)]
    public static partial Regex HsnCode();

    /// <summary>An ISO 3166-1 alpha-2 country code.</summary>
    [GeneratedRegex(@"^[A-Z]{2}$", RegexOptions.CultureInvariant)]
    public static partial Regex CountryCode();

    /// <summary>A slug: lowercase, digits and single hyphens.</summary>
    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    public static partial Regex Slug();

    /// <summary>An attribute or attribute-set code: lowercase, digits and single underscores.</summary>
    [GeneratedRegex(@"^[a-z][a-z0-9]*(_[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    public static partial Regex Code();

    /// <summary>A SKU: upper-case letters, digits, hyphens and underscores.</summary>
    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9_-]{1,63}$", RegexOptions.CultureInvariant)]
    public static partial Regex Sku();

    /// <summary>A CSS hex colour, three or six digits, with the hash.</summary>
    [GeneratedRegex(@"^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$", RegexOptions.CultureInvariant)]
    public static partial Regex HexColour();

    /// <summary>The characters a slug may be built from, once everything else has been removed.</summary>
    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonSlug();

    /// <summary>
    /// The accented Latin letters this folds to their unaccented form.
    /// </summary>
    /// <remarks>
    /// An explicit table rather than Unicode decomposition, because this product runs with
    /// <c>InvariantGlobalization</c>, where <see cref="string.Normalize(NormalizationForm)"/> does
    /// not decompose — a normalise-and-strip-marks implementation would silently turn "Café" into
    /// "caf-".
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
    /// Turns a name into a URL slug.
    /// </summary>
    /// <remarks>
    /// The result is a suggestion. Uniqueness is settled by the handler, which is the only place
    /// that can see the other rows.
    /// </remarks>
    /// <param name="value">The name to derive a slug from.</param>
    /// <param name="maxLength">The longest slug to produce.</param>
    public static string ToSlug(string value, int maxLength = 180)
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

    /// <summary>Turns a label into a machine code — lowercase, digits and underscores.</summary>
    /// <param name="value">The label to derive a code from.</param>
    /// <param name="maxLength">The longest code to produce.</param>
    public static string ToCode(string value, int maxLength = 64)
        => ToSlug(value, maxLength).Replace('-', '_').Trim('_');
}
