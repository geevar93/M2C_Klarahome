using KlaraHome.Modules.Media.Domain;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Media.Infrastructure.Scanning;

/// <summary>
/// Examines an uploaded file before it becomes reachable (docs/08-integrations.md §4).
/// </summary>
/// <remarks>
/// The seam, declared now because the decision of <em>where</em> a scan happens is architectural
/// and expensive to move later: it happens before the object is registered as servable, in the
/// request, on the bytes in memory — not on a schedule against a bucket that has already been
/// serving the file for an hour.
/// </remarks>
internal interface IVirusScanner
{
    /// <summary>Whether this implementation actually scans anything.</summary>
    bool IsRealScanner { get; }

    /// <summary>Examines the content and reports what it found.</summary>
    /// <param name="content">The complete file content.</param>
    /// <param name="fileName">The upload name, for the scanner's log.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ScanState> ScanAsync(ReadOnlyMemory<byte> content, string fileName, CancellationToken cancellationToken);
}

/// <summary>
/// The v1 scanner: it scans nothing and says so.
/// </summary>
/// <remarks>
/// <para>
/// A ClamAV container is the intended second implementation and is not in Step 8's deliverables.
/// What matters is that this one does not <em>pretend</em>: it records <see cref="ScanState.Skipped"/>
/// rather than <see cref="ScanState.Clean"/>, so a file that nobody checked is distinguishable in
/// the registry from one that was checked and found clean.
/// </para>
/// <para>
/// A deployment that cannot accept unscanned uploads sets <c>Media:RequireVirusScan</c>, and the
/// upload endpoint then refuses rather than accepting quietly.
/// </para>
/// </remarks>
/// <param name="logger">Records, once per process, that nothing is scanning.</param>
internal sealed partial class NoOpVirusScanner(ILogger<NoOpVirusScanner> logger) : IVirusScanner
{
    private int _warned;

    /// <inheritdoc />
    public bool IsRealScanner => false;

    /// <inheritdoc />
    public Task<ScanState> ScanAsync(
        ReadOnlyMemory<byte> content,
        string fileName,
        CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _warned, 1) == 0)
        {
            NoScannerConfigured(logger);
        }

        return Task.FromResult(ScanState.Skipped);
    }

    [LoggerMessage(EventId = 1510, Level = LogLevel.Warning,
        Message = "No virus scanner is configured; uploads are recorded as unscanned. Set "
                  + "Media:RequireVirusScan to refuse uploads instead of accepting them unchecked.")]
    private static partial void NoScannerConfigured(ILogger logger);
}
