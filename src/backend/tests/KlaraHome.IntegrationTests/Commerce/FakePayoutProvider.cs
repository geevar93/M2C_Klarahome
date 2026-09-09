using System.Collections.Concurrent;
using KlaraHome.Modules.Settlements.Infrastructure.Payouts;
using KlaraHome.SharedKernel.Results;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// A payout rail at the network boundary, and nothing below it.
/// </summary>
/// <remarks>
/// <para>
/// The default test configuration leaves <c>Payouts:Provider</c> blank, exactly as
/// <c>UnconfiguredPayoutProvider</c>'s own remarks describe production doing by the User's standing
/// instruction — which is what keeps the honest-adapter row (TEST_DEBT.md, row 245) provable against
/// the real default rather than against a fake standing in for it.
/// </para>
/// <para>
/// This fake exists for the rows beside it that need a rail which actually answers: a resumable
/// partial send, a completed transfer that posts a ledger debit and marks its cycle paid, and a
/// failed one that releases its cycle. A test opts in explicitly, by setting
/// <c>Overrides["Payouts:Provider"] = Name</c> on the factory before it is first used — everything
/// else about the batch, the maker-checker control and the ledger posting is the same production code
/// that runs against a real gateway.
/// </para>
/// <para>
/// Outcomes are decided per vendor, by whatever the test put in <see cref="Outcomes"/> before sending;
/// a vendor with no entry completes, which keeps the common case a one-line arrange.
/// </para>
/// </remarks>
internal sealed class FakePayoutProvider : IPayoutProvider
{
    /// <summary>The rail name this fake answers to. Deliberately not one of the two real ones.</summary>
    public const string Name = "fake";

    private readonly ConcurrentDictionary<string, ProviderPayout> _sent = new(StringComparer.Ordinal);
    private int _sequence;

    /// <summary>What each vendor's transfer should do when sent, keyed by vendor id.</summary>
    public ConcurrentDictionary<Guid, PayoutOutcome> Outcomes { get; } = new();

    /// <summary>Every request this fake was asked to send, in order.</summary>
    public List<PayoutRequest> Sent { get; } = [];

    /// <inheritdoc />
    string IPayoutProvider.Name => Name;

    /// <inheritdoc />
    public bool IsConfigured { get; set; } = true;

    /// <inheritdoc />
    public Task<Result<ProviderPayout>> SendAsync(PayoutRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (Sent)
        {
            Sent.Add(request);
        }

        var outcome = Outcomes.GetValueOrDefault(request.VendorId, PayoutOutcome.Completed);
        var providerId = $"payout_fake_{Interlocked.Increment(ref _sequence)}";

        var answer = outcome switch
        {
            PayoutOutcome.Failed => new ProviderPayout(
                providerId, "failed", IsProcessed: false, IsFailed: true, Utr: null,
                Error: "The beneficiary account was closed.", DateTimeOffset.UtcNow),

            PayoutOutcome.StillMoving => new ProviderPayout(
                providerId, "queued", IsProcessed: false, IsFailed: false, Utr: null, Error: null,
                OccurredAt: null),

            _ => new ProviderPayout(
                providerId, "processed", IsProcessed: true, IsFailed: false, $"UTR{providerId}", Error: null,
                DateTimeOffset.UtcNow),
        };

        _sent[providerId] = answer;

        return Task.FromResult(Result.Success(answer));
    }

    /// <inheritdoc />
    public Task<Result<ProviderPayout>> FetchAsync(string providerPayoutId, CancellationToken cancellationToken)
        => Task.FromResult(
            _sent.TryGetValue(providerPayoutId, out var answer)
                ? Result.Success(answer)
                : Result.Failure<ProviderPayout>(Error.NotFound("PAYOUT_NOT_FOUND", "No such transfer.")));

    /// <summary>Overwrites what a transfer already sent will answer on the next re-fetch.</summary>
    /// <param name="providerPayoutId">The gateway's id for it.</param>
    /// <param name="answer">What it should say next.</param>
    public void Reanswer(string providerPayoutId, ProviderPayout answer) => _sent[providerPayoutId] = answer;
}

/// <summary>What <see cref="FakePayoutProvider"/> should do with one seller's transfer.</summary>
internal enum PayoutOutcome
{
    /// <summary>The gateway confirms it immediately.</summary>
    Completed,

    /// <summary>The gateway refuses it immediately.</summary>
    Failed,

    /// <summary>The gateway accepts it but has not settled it — still in flight for the sweep to find.</summary>
    StillMoving,
}
