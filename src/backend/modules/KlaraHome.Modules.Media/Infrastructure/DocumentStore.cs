using KlaraHome.Contracts.Documents;
using KlaraHome.Contracts.Media;
using KlaraHome.Infrastructure.Documents;
using KlaraHome.Modules.Media.Infrastructure.Validation;

namespace KlaraHome.Modules.Media.Infrastructure;

/// <summary>
/// Renders a document, stores it privately and registers it — the pipeline invoices, credit notes
/// and shipping labels are produced through (ADR-015, ADR-016).
/// </summary>
/// <remarks>
/// <para>
/// A generated document goes into the <em>private</em> bucket without the caller being asked. There
/// is no legitimate reason for an invoice to be world-readable at a guessable URL, and making it a
/// parameter would eventually see one passed wrongly.
/// </para>
/// <para>
/// It goes through the same validation and registration path as an upload, which is not ceremony:
/// the checksum on the row is what later proves the stored PDF is the one that was rendered, and
/// the registry row is what gives it an id an invoice can point at.
/// </para>
/// </remarks>
/// <param name="renderer">Turns the document model into PDF bytes.</param>
/// <param name="media">Stores and registers the result.</param>
internal sealed class DocumentStore(IDocumentRenderer renderer, MediaStorageService media) : IDocumentStore
{
    /// <inheritdoc />
    public async Task<MediaFile> RenderAsync(
        DocumentDefinition document,
        string fileName,
        string ownerType,
        Guid? ownerId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var bytes = renderer.Render(document);

        var result = await media
            .StoreAsync(
                bytes,
                fileName,
                MediaIntent.Document,
                MediaVisibility.Private,
                ownerType,
                ownerId,
                cancellationToken)
            .ConfigureAwait(false);

        // A failure here is not a user's mistake — nobody uploaded anything, this service rendered
        // the bytes itself. Storage being down or the registry refusing our own PDF is an
        // infrastructure fault, and the caller (an order being invoiced) should fail loudly rather
        // than carry on with no document.
        return result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException(
                $"Storing the generated document '{fileName}' failed: {result.Error}");
    }
}
