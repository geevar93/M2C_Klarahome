namespace KlaraHome.Infrastructure.Storage;

/// <summary>Which bucket an object lives in, and therefore who may read it.</summary>
public enum StorageVisibility
{
    /// <summary>The world-readable bucket. Served straight from a CDN or reverse proxy.</summary>
    Public = 0,

    /// <summary>The private bucket. Reachable only through a short-lived signed URL.</summary>
    Private = 1,
}

/// <summary>An object as it was stored.</summary>
/// <param name="Key">The object key within its bucket.</param>
/// <param name="Visibility">Which bucket it went to.</param>
/// <param name="ByteSize">Size of the stored object.</param>
/// <param name="ETag">The provider's entity tag, kept for diagnostics.</param>
public sealed record StoredObject(string Key, StorageVisibility Visibility, long ByteSize, string? ETag);

/// <summary>
/// Object storage, as this platform uses it (docs/08-integrations.md §4).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately four operations and no more. Everything S3 can do beyond put, get, delete and
/// sign is either a feature we would have to reimplement when moving provider, or a feature the
/// Media module should be deciding about rather than the transport.
/// </para>
/// <para>
/// Nothing here knows what a product image is. Validation, dimensions, derivatives, scanning and
/// the registry are the Media module's (ADR-016); this is the wire.
/// </para>
/// </remarks>
public interface IFileStorage
{
    /// <summary>Whether a client is configured. False in a host with no storage credentials.</summary>
    bool IsAvailable { get; }

    /// <summary>Stores an object, overwriting any object already at that key.</summary>
    /// <param name="key">The object key.</param>
    /// <param name="content">The bytes. Read from the current position to the end.</param>
    /// <param name="contentType">The MIME type recorded on the object.</param>
    /// <param name="visibility">Which bucket to write to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<StoredObject> PutAsync(
        string key,
        Stream content,
        string contentType,
        StorageVisibility visibility,
        CancellationToken cancellationToken = default);

    /// <summary>Reads an object back, or null when there is nothing at that key.</summary>
    /// <param name="key">The object key.</param>
    /// <param name="visibility">Which bucket to read from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Stream?> GetAsync(
        string key,
        StorageVisibility visibility,
        CancellationToken cancellationToken = default);

    /// <summary>Removes an object. Deleting one that is not there is not an error.</summary>
    /// <param name="key">The object key.</param>
    /// <param name="visibility">Which bucket to delete from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(
        string key,
        StorageVisibility visibility,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A time-limited URL that grants read access to one object without a credential.
    /// </summary>
    /// <remarks>
    /// The only way a private document reaches a browser, and it is issued <em>after</em> the
    /// caller's authorisation has been checked — the URL itself carries no authorisation of its
    /// own beyond its expiry, so it must never be minted before the decision to allow the read.
    /// </remarks>
    /// <param name="key">The object key.</param>
    /// <param name="visibility">Which bucket the object is in.</param>
    /// <param name="lifetime">How long the URL should remain valid.</param>
    /// <param name="downloadFileName">Filename to force as an attachment, or null to leave it inline.</param>
    string GetSignedUrl(
        string key,
        StorageVisibility visibility,
        TimeSpan lifetime,
        string? downloadFileName = null);

    /// <summary>The public URL of an object in the world-readable bucket.</summary>
    /// <param name="key">The object key.</param>
    string GetPublicUrl(string key);
}
