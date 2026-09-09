using System.Collections.Concurrent;
using KlaraHome.Contracts.Notifications;
using KlaraHome.Infrastructure.Storage;
using KlaraHome.Modules.Notifications.Infrastructure.Channels;

namespace KlaraHome.IntegrationTests.Step8;

/// <summary>
/// Object storage in a dictionary.
/// </summary>
/// <remarks>
/// The S3 client is the one thing in the media path that genuinely needs a server. Everything that
/// decides whether an upload is accepted — the content inspection, the limits, the checksum, the
/// registry write — runs unchanged above this.
/// </remarks>
internal sealed class InMemoryFileStorage : IFileStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _objects = new(StringComparer.Ordinal);

    /// <summary>How many objects are held.</summary>
    public int Count => _objects.Count;

    /// <summary>
    /// Whether this deployment can reach its bucket. Settable, so an outage stays provable.
    /// </summary>
    /// <remarks>
    /// Several criteria are about what happens when object storage is <em>not</em> there — an
    /// invoice that still gets its number, a seller's bank details that are refused rather than
    /// stored in clear — and none of them can be shown against a store that is always up.
    /// </remarks>
    public bool IsAvailable { get; set; } = true;

    /// <summary>Whether an object exists at a key.</summary>
    /// <param name="key">The object key.</param>
    /// <param name="visibility">Which bucket.</param>
    public bool Contains(string key, StorageVisibility visibility) => _objects.ContainsKey(Compose(key, visibility));

    /// <inheritdoc />
    public async Task<StoredObject> PutAsync(
        string key,
        Stream content,
        string contentType,
        StorageVisibility visibility,
        CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        var bytes = buffer.ToArray();
        _objects[Compose(key, visibility)] = bytes;

        return new StoredObject(key, visibility, bytes.Length, "\"test\"");
    }

    /// <inheritdoc />
    public Task<Stream?> GetAsync(
        string key,
        StorageVisibility visibility,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Stream?>(
            _objects.TryGetValue(Compose(key, visibility), out var bytes) ? new MemoryStream(bytes) : null);

    /// <inheritdoc />
    public Task DeleteAsync(string key, StorageVisibility visibility, CancellationToken cancellationToken = default)
    {
        _objects.TryRemove(Compose(key, visibility), out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public string GetSignedUrl(
        string key,
        StorageVisibility visibility,
        TimeSpan lifetime,
        string? downloadFileName = null)
        => $"https://signed.example.test/{key}?expires={(int)lifetime.TotalSeconds}";

    /// <inheritdoc />
    public string GetPublicUrl(string key) => $"https://cdn.example.test/media-public/{key}";

    private static string Compose(string key, StorageVisibility visibility) => $"{visibility}:{key}";
}

/// <summary>
/// A channel sender that records instead of sending, and can be told to fail.
/// </summary>
/// <remarks>
/// Stands where a provider would. It is what lets a test assert the delivery log, the retry
/// schedule and — the point of most of these tests — that a one-time code reaches the provider and
/// is nowhere else afterwards.
/// </remarks>
/// <param name="channel">The channel it carries.</param>
internal sealed class RecordingChannelSender(NotificationChannel channel) : IChannelSender
{
    private readonly ConcurrentQueue<OutboundMessage> _sent = new();

    /// <inheritdoc />
    public NotificationChannel Channel => channel;

    /// <summary>Whether the router should treat this channel as available.</summary>
    public bool IsConfigured { get; set; } = true;

    /// <summary>What to answer. Null means acceptance.</summary>
    public SendOutcome? Outcome { get; set; }

    /// <summary>Everything handed to this sender, in order.</summary>
    public IReadOnlyCollection<OutboundMessage> Sent => _sent;

    /// <summary>The most recent message, or null.</summary>
    public OutboundMessage? Last => _sent.LastOrDefault();

    /// <inheritdoc />
    public Task<SendOutcome> SendAsync(OutboundMessage message, CancellationToken cancellationToken)
    {
        _sent.Enqueue(message);
        return Task.FromResult(Outcome ?? SendOutcome.Accepted("provider-" + Guid.NewGuid().ToString("n")[..8]));
    }
}
