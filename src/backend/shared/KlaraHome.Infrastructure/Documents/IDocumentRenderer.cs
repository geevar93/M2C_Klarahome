using KlaraHome.Contracts.Documents;

namespace KlaraHome.Infrastructure.Documents;

/// <summary>
/// Turns a <see cref="DocumentDefinition"/> into PDF bytes (ADR-015).
/// </summary>
/// <remarks>
/// <para>
/// The seam that keeps the PDF library out of every module. Nothing above this interface names
/// MigraDoc, and replacing it — with a headless browser, or with QuestPDF in a deployment that
/// has bought a licence — is one class.
/// </para>
/// <para>
/// Rendering is synchronous and CPU-bound; there is no I/O to await. Storing the result is the
/// caller's job, which is what keeps this testable without a bucket.
/// </para>
/// </remarks>
public interface IDocumentRenderer
{
    /// <summary>Renders a document to PDF.</summary>
    /// <param name="document">What to render.</param>
    byte[] Render(DocumentDefinition document);
}
