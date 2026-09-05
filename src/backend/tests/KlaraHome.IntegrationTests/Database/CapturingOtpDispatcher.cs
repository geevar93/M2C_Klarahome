using System.Collections.Concurrent;
using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Infrastructure.Access;

namespace KlaraHome.IntegrationTests.Database;

/// <summary>
/// Keeps the codes and links the API sends, so a test can present one the way a person would.
/// </summary>
/// <remarks>
/// This is the only shortcut these tests take, and it stands in for a phone and an inbox rather
/// than for any part of the system under test: the code is still generated, hashed, stored,
/// throttled, expired and verified by the real implementation. Reading it from the log instead
/// would be the same shortcut with a parser in front of it.
/// </remarks>
internal sealed class CapturingOtpDispatcher : IOtpDispatcher
{
    private readonly ConcurrentDictionary<string, string> _latest = new(StringComparer.Ordinal);

    /// <summary>How many codes have been sent, so a throttle test can count them.</summary>
    public int Sent { get; private set; }

    /// <inheritdoc />
    public Task DispatchAsync(
        string destination,
        OtpChannel channel,
        OtpPurpose purpose,
        string code,
        CancellationToken cancellationToken)
    {
        _latest[Key(destination, purpose)] = code;
        Sent++;

        return Task.CompletedTask;
    }

    /// <summary>The most recent code sent to a destination for a purpose.</summary>
    /// <param name="destination">The mobile number or email address.</param>
    /// <param name="purpose">What the code proves.</param>
    /// <exception cref="InvalidOperationException">Nothing was ever sent there.</exception>
    public string Latest(string destination, OtpPurpose purpose)
        => _latest.TryGetValue(Key(destination, purpose), out var code)
            ? code
            : throw new InvalidOperationException(
                $"No {purpose} code was sent to '{destination}'. Sent so far: {string.Join(", ", _latest.Keys)}.");

    /// <summary>Whether anything has been sent to a destination for a purpose.</summary>
    /// <param name="destination">The mobile number or email address.</param>
    /// <param name="purpose">What the code proves.</param>
    public bool Has(string destination, OtpPurpose purpose) => _latest.ContainsKey(Key(destination, purpose));

    /// <summary>Forgets everything, so one test's codes cannot satisfy another's assertion.</summary>
    public void Clear()
    {
        _latest.Clear();
        Sent = 0;
    }

    private static string Key(string destination, OtpPurpose purpose) => $"{purpose}:{destination}";
}
