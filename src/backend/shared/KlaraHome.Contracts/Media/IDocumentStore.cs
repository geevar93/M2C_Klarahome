using KlaraHome.Contracts.Documents;

namespace KlaraHome.Contracts.Media;

/// <summary>
/// Renders a document to PDF, stores it in the private bucket and registers it, in one call.
/// </summary>
/// <remarks>
/// <para>
/// The three steps are together because doing two of them is always a mistake: a rendered document
/// nobody stored is lost, and a stored one nobody registered has no id for an invoice row to point
/// at and no way to be reached later.
/// </para>
/// <para>
/// Nothing here decides what an invoice says. Orders builds the <see cref="DocumentDefinition"/>
/// at Step 14, Shipping builds a label at Step 16, and Returns builds a credit note at Step 17;
/// this turns any of them into a file id.
/// </para>
/// </remarks>
public interface IDocumentStore
{
    /// <summary>Renders, stores and registers a document, returning the file that resulted.</summary>
    /// <param name="document">What to render.</param>
    /// <param name="fileName">
    /// The download name, which is also the tail of the storage key. Use something a human can
    /// recognise in a support conversation — <c>invoice-KH-2026-000123.pdf</c>, not a GUID.
    /// </param>
    /// <param name="ownerType">What the document belongs to, e.g. <c>Invoice</c>. Used by the orphan sweep.</param>
    /// <param name="ownerId">The id of the thing it belongs to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<MediaFile> RenderAsync(
        DocumentDefinition document,
        string fileName,
        string ownerType,
        Guid? ownerId = null,
        CancellationToken cancellationToken = default);
}
