namespace KlaraHome.Infrastructure.Documents;

/// <summary>
/// Where the document renderer finds its fonts.
/// </summary>
/// <remarks>
/// <para>
/// PDFsharp embeds the glyphs it draws, so it needs a real font file — and the runtime container
/// is chiselled, with no shell, no package manager and no fonts. The image therefore copies a
/// redistributable family in at build time and this points at it; a developer on Windows falls
/// through to the system font directory instead.
/// </para>
/// <para>
/// The alternative — letting the renderer pick whatever the host happens to have — is how a
/// statutory document comes to look different in production from every place it was reviewed.
/// </para>
/// </remarks>
public sealed class DocumentOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = "Documents";

    /// <summary>
    /// Directories searched, in order, for a usable font family. The first is where the container
    /// image places its fonts; the rest are the conventional locations on Linux and Windows.
    /// </summary>
    public IReadOnlyList<string> FontDirectories { get; set; } =
    [
        "/app/fonts",
        "/usr/share/fonts/truetype/dejavu",
        "/usr/share/fonts",
        "C:\\Windows\\Fonts",
    ];

    /// <summary>
    /// Font families to look for, most preferred first. Each is tried as a set of four faces
    /// (regular, bold, italic, bold-italic); a family missing a face falls back to its regular one.
    /// </summary>
    public IReadOnlyList<string> PreferredFamilies { get; set; } = ["DejaVuSans", "LiberationSans", "Arial"];
}
