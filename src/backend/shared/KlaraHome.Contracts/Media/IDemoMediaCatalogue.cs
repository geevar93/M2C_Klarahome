namespace KlaraHome.Contracts.Media;

/// <summary>
/// The demonstration imagery already in the media library, by key, for the other modules'
/// demonstration seeders to attach.
/// </summary>
/// <remarks>
/// <para>
/// **Why this exists.** Only the Media module may write a stored file, and no module may join to
/// <c>media.files</c>. The demonstration catalogue, categories and home page all want pictures, so
/// the Media module's seeder uploads them once - through the same storage service the admin upload
/// uses - and this contract is how the rest find the ids afterwards.
/// </para>
/// <para>
/// Keys are stable and meaningful: <c>demo-hero</c>, <c>demo-category-&lt;slug&gt;</c> and
/// <c>demo-product-&lt;SKU&gt;</c>. An empty result is a normal answer - demonstration data may be
/// switched off, or object storage unconfigured - and a seeder that gets one simply attaches nothing.
/// </para>
/// </remarks>
public interface IDemoMediaCatalogue
{
    /// <summary>Every demonstration image that is stored and servable, keyed as above.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyDictionary<string, Guid>> ListAsync(CancellationToken cancellationToken = default);
}
