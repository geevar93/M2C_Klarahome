using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace KlaraHome.Infrastructure.Storage;

/// <summary>
/// Proves object storage is reachable and that the buckets this deployment was told to use
/// actually exist.
/// </summary>
/// <remarks>
/// A read of one absent key rather than a bucket listing: it exercises the same code path an
/// upload takes — credentials, endpoint, path style, bucket name — and costs one small request.
/// A missing bucket answers differently from a missing key, which is the distinction that matters:
/// "MinIO is up but nobody ran the bucket bootstrap" is the failure this check exists to name.
/// </remarks>
/// <param name="storage">The storage client.</param>
/// <param name="options">Bucket names, for the message.</param>
internal sealed class StorageHealthCheck(IFileStorage storage, IOptions<StorageOptions> options) : IHealthCheck
{
    /// <summary>A key that is never written, so the probe reads nothing a caller owns.</summary>
    private const string ProbeKey = ".klarahome-health-probe";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var buckets = options.Value;

        if (!storage.IsAvailable)
        {
            return HealthCheckResult.Degraded("Object storage has no credentials configured.");
        }

        try
        {
            // A null result means "reachable, and that key is not there", which is the answer this
            // probe wants. A missing bucket throws instead.
            await storage.GetAsync(ProbeKey, StorageVisibility.Public, cancellationToken).ConfigureAwait(false);
            await storage.GetAsync(ProbeKey, StorageVisibility.Private, cancellationToken).ConfigureAwait(false);

            return HealthCheckResult.Healthy(
                $"Buckets '{buckets.Bucket}' and '{buckets.PrivateBucket}' are reachable.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Degraded(
                $"Object storage did not answer for '{buckets.Bucket}' / '{buckets.PrivateBucket}'.",
                exception);
        }
    }
}
