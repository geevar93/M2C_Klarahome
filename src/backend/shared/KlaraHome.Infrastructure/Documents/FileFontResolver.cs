using System.Collections.Concurrent;
using PdfSharp.Fonts;

namespace KlaraHome.Infrastructure.Documents;

/// <summary>
/// Supplies PDFsharp with font files found on disk, from a fixed search list rather than from
/// whatever the operating system reports.
/// </summary>
/// <remarks>
/// <para>
/// Every family the document model asks for resolves to the <em>same</em> located family. That is
/// deliberate: the model has one typeface by design (aesthetics are Step 30's, and a statutory
/// document is not where brand expression belongs), and a resolver that silently substituted a
/// different family per request would make a document's appearance depend on the host it rendered
/// on.
/// </para>
/// <para>
/// PDFsharp asks for a face once per process and caches it, so the file reads here happen at most
/// four times.
/// </para>
/// </remarks>
/// <param name="options">Where to look and what to look for.</param>
public sealed class FileFontResolver(DocumentOptions options) : IFontResolver
{
    private readonly ConcurrentDictionary<string, byte[]> _faces = new(StringComparer.Ordinal);
    private readonly Lazy<FontFamilyFiles> _family = new(() => Locate(options), isThreadSafe: true);

    /// <summary>The family actually found, for diagnostics and for the startup log line.</summary>
    public string ResolvedFamily => _family.Value.Family;

    /// <inheritdoc />
    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        var files = _family.Value;
        return new FontResolverInfo(files.FaceName(bold, italic));
    }

    /// <inheritdoc />
    public byte[]? GetFont(string faceName)
        => _faces.GetOrAdd(faceName, name => File.ReadAllBytes(_family.Value.PathFor(name)));

    /// <summary>
    /// Finds the first preferred family that has at least a regular face, and refuses to continue
    /// if there is none.
    /// </summary>
    /// <remarks>
    /// Throwing here is the point. A renderer with no font produces either an exception at the
    /// first invoice or, worse, a PDF of empty boxes; failing when the resolver is first used —
    /// which the module does at startup — turns a production surprise into a deployment error.
    /// </remarks>
    /// <param name="options">Where to look and what to look for.</param>
    private static FontFamilyFiles Locate(DocumentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (var directory in options.FontDirectories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var family in options.PreferredFamilies)
            {
                var regular = FindFace(directory, family, bold: false, italic: false);

                if (regular is null)
                {
                    continue;
                }

                return new FontFamilyFiles(
                    family,
                    regular,
                    FindFace(directory, family, bold: true, italic: false) ?? regular,
                    FindFace(directory, family, bold: false, italic: true) ?? regular,
                    FindFace(directory, family, bold: true, italic: true) ?? regular);
            }
        }

        throw new InvalidOperationException(
            "No usable font was found for PDF rendering. Looked for "
            + string.Join(", ", options.PreferredFamilies)
            + " in "
            + string.Join(", ", options.FontDirectories)
            + ". The container image copies a font family in at build time; set Documents:FontDirectories "
            + "if this deployment keeps them somewhere else.");
    }

    /// <summary>
    /// Looks for one face of a family under the naming conventions the three candidate families
    /// actually use — DejaVu and Liberation suffix the style, Microsoft's fonts abbreviate it.
    /// </summary>
    private static string? FindFace(string directory, string family, bool bold, bool italic)
    {
        string[] candidates = (bold, italic) switch
        {
            (false, false) => [$"{family}.ttf", $"{family}-Regular.ttf"],
            (true, false) => [$"{family}-Bold.ttf", $"{family}bd.ttf"],
            (false, true) => [$"{family}-Oblique.ttf", $"{family}-Italic.ttf", $"{family}i.ttf"],
            (true, true) => [$"{family}-BoldOblique.ttf", $"{family}-BoldItalic.ttf", $"{family}bi.ttf"],
        };

        foreach (var candidate in candidates)
        {
            var path = Path.Combine(directory, candidate);

            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>The four faces of the family that was found, and the names PDFsharp asks for them by.</summary>
    private sealed record FontFamilyFiles(string Family, string Regular, string Bold, string Italic, string BoldItalic)
    {
        public string FaceName(bool bold, bool italic) => (bold, italic) switch
        {
            (false, false) => Family + "#regular",
            (true, false) => Family + "#bold",
            (false, true) => Family + "#italic",
            (true, true) => Family + "#bolditalic",
        };

        public string PathFor(string faceName) => faceName[(faceName.IndexOf('#', StringComparison.Ordinal) + 1)..] switch
        {
            "bold" => Bold,
            "italic" => Italic,
            "bolditalic" => BoldItalic,
            _ => Regular,
        };
    }
}
