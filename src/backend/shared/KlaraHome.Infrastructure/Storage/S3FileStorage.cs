using System.Globalization;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Infrastructure.Storage;

/// <summary>
/// <see cref="IFileStorage"/> over the S3 API — MinIO on the VPS today, AWS S3 or Cloudflare R2
/// with a configuration change (ADR-010).
/// </summary>
/// <remarks>
/// <para>
/// One <see cref="AmazonS3Client"/> for the process. The client is thread-safe and holds the
/// connection pool; constructing one per request is the standard way to exhaust sockets under
/// load.
/// </para>
/// <para>
/// A host with no credentials gets an instance whose <see cref="IsAvailable"/> is false and whose
/// operations throw a named exception rather than a null reference. That is deliberate: the
/// migrator has no storage and should not fail to start because of it.
/// </para>
/// </remarks>
public sealed partial class S3FileStorage : IFileStorage, IDisposable
{
    private readonly StorageOptions _options;
    private readonly ILogger<S3FileStorage> _logger;
    private readonly AmazonS3Client? _client;
    private readonly AmazonS3Client? _signingClient;

    /// <param name="options">Endpoint, credentials and bucket names.</param>
    /// <param name="logger">Reports what was stored and what could not be.</param>
    public S3FileStorage(IOptions<StorageOptions> options, ILogger<S3FileStorage> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _logger = logger;

        if (!_options.IsConfigured)
        {
            StorageNotConfigured(logger);
            return;
        }

        var config = new AmazonS3Config
        {
            ForcePathStyle = _options.ForcePathStyle,
            AuthenticationRegion = _options.Region,
        };

        if (string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(_options.Region);
        }
        else
        {
            // A custom endpoint and a RegionEndpoint are mutually exclusive in the SDK; setting
            // both silently ignores one of them, and which one is not obvious from the call site.
            config.ServiceURL = _options.Endpoint;
        }

        var credentials = new BasicAWSCredentials(_options.AccessKey, _options.SecretKey);
        _client = new AmazonS3Client(credentials, config);

        // A second client, differing only in the endpoint it signs for. SigV4 covers the host, so a
        // URL signed for the internal address is unreachable from a browser and cannot be rewritten
        // to a reachable one without invalidating it. Where the two addresses are the same - AWS S3,
        // R2 - there is no second client.
        _signingClient = string.IsNullOrWhiteSpace(_options.SignedUrlEndpoint)
            ? null
            : new AmazonS3Client(
                credentials,
                new AmazonS3Config
                {
                    ForcePathStyle = _options.ForcePathStyle,
                    AuthenticationRegion = _options.Region,
                    ServiceURL = _options.SignedUrlEndpoint,
                });
    }

    /// <inheritdoc />
    public bool IsAvailable => _client is not null;

    /// <inheritdoc />
    public async Task<StoredObject> PutAsync(
        string key,
        Stream content,
        string contentType,
        StorageVisibility visibility,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(content);

        var client = Required();
        var bucket = BucketFor(visibility);
        var size = content.CanSeek ? content.Length - content.Position : 0;

        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
        };

        var response = await client.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);

        ObjectStored(_logger, bucket, key, size);

        return new StoredObject(key, visibility, size, response.ETag);
    }

    /// <inheritdoc />
    public async Task<Stream?> GetAsync(
        string key,
        StorageVisibility visibility,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var client = Required();

        try
        {
            using var response = await client
                .GetObjectAsync(BucketFor(visibility), key, cancellationToken)
                .ConfigureAwait(false);

            // Copied out rather than handed over: the response stream keeps the HTTP connection
            // open, and a caller that forgets to dispose it holds a socket until the pool starves.
            var buffer = new MemoryStream();
            await response.ResponseStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            buffer.Position = 0;
            return buffer;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        string key,
        StorageVisibility visibility,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var client = Required();
        var bucket = BucketFor(visibility);

        // S3 answers 204 for a key that was never there, so this is idempotent without a check.
        await client.DeleteObjectAsync(bucket, key, cancellationToken).ConfigureAwait(false);

        ObjectDeleted(_logger, bucket, key);
    }

    /// <inheritdoc />
    public string GetSignedUrl(
        string key,
        StorageVisibility visibility,
        TimeSpan lifetime,
        string? downloadFileName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var client = _signingClient ?? Required();

        // The presigner does not take its scheme from ServiceURL: it defaults to https whatever the
        // endpoint says, and a URL signed for https://s3.example is refused by the same host over
        // http (SigV4 does not cover the scheme, but the browser cannot connect). The local edge
        // is plain http (docker-compose.local.yml), so the scheme follows the endpoint it was
        // signed for.
        var signedFor = _signingClient is null ? _options.Endpoint : _options.SignedUrlEndpoint;
        var protocol = signedFor.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? Protocol.HTTP
            : Protocol.HTTPS;

        var request = new GetPreSignedUrlRequest
        {
            BucketName = BucketFor(visibility),
            Key = key,
            Verb = HttpVerb.GET,
            Protocol = protocol,
            Expires = DateTime.UtcNow.Add(lifetime),
        };

        if (!string.IsNullOrWhiteSpace(downloadFileName))
        {
            // Quoted, and with anything that could terminate the header stripped first: a filename
            // carrying a quote or a newline would otherwise let the uploader write their own
            // response headers.
            var safe = Sanitise(downloadFileName);
            request.ResponseHeaderOverrides.ContentDisposition =
                string.Create(CultureInfo.InvariantCulture, $"attachment; filename=\"{safe}\"");
        }

        return client.GetPreSignedURL(request);
    }

    /// <inheritdoc />
    public string GetPublicUrl(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var baseUrl = string.IsNullOrWhiteSpace(_options.PublicBaseUrl)
            ? $"{_options.Endpoint.TrimEnd('/')}/{_options.Bucket}"
            : _options.PublicBaseUrl.TrimEnd('/');

        // The key's slashes are path separators and must survive; everything else is escaped.
        var escaped = string.Join('/', key.Split('/').Select(Uri.EscapeDataString));
        return $"{baseUrl}/{escaped}";
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _client?.Dispose();
        _signingClient?.Dispose();
    }

    /// <summary>Strips anything that could break out of the Content-Disposition header.</summary>
    /// <param name="fileName">The proposed download name.</param>
    internal static string Sanitise(string fileName)
    {
        Span<char> buffer = stackalloc char[Math.Min(fileName.Length, 120)];
        var length = 0;

        foreach (var character in fileName)
        {
            if (length == buffer.Length)
            {
                break;
            }

            // A control character is dropped rather than substituted: it carries no information a
            // reader would miss, and a name made entirely of underscores is worse than the
            // fallback. A quote or a backslash becomes an underscore, which keeps the rest of a
            // recognisable filename intact.
            if (char.IsControl(character))
            {
                continue;
            }

            buffer[length++] = character is '"' or '\\' ? '_' : character;
        }

        return length == 0 ? "download" : new string(buffer[..length]);
    }

    private string BucketFor(StorageVisibility visibility)
        => visibility == StorageVisibility.Public ? _options.Bucket : _options.PrivateBucket;

    private AmazonS3Client Required()
        => _client ?? throw new InvalidOperationException(
            "Object storage is not configured. Set Storage:AccessKey and Storage:SecretKey, or do "
            + "not resolve IFileStorage in a host that has no storage.");

    [LoggerMessage(EventId = 1500, Level = LogLevel.Warning,
        Message = "Object storage is not configured; IFileStorage will refuse every operation. "
                  + "This is expected in the migrator, and a misconfiguration anywhere else.")]
    private static partial void StorageNotConfigured(ILogger logger);

    [LoggerMessage(EventId = 1501, Level = LogLevel.Debug,
        Message = "Stored {StorageBucket}/{StorageKey} ({StorageByteSize} bytes)")]
    private static partial void ObjectStored(
        ILogger logger,
        string storageBucket,
        string storageKey,
        long storageByteSize);

    [LoggerMessage(EventId = 1502, Level = LogLevel.Information,
        Message = "Deleted {StorageBucket}/{StorageKey}")]
    private static partial void ObjectDeleted(ILogger logger, string storageBucket, string storageKey);
}
