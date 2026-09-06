using KlaraHome.Modules.Settlements.Application;
using KlaraHome.SharedKernel.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Settlements.Infrastructure.Payouts;

/// <summary>
/// The honest adapter for a deployment with no payout account: it sends nothing and says so.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as the Media module's no-op virus scanner (ADR-016) and the Vendors module's
/// unprovisioned payout accounts, and for the same reason: a seam whose default implementation
/// pretends to have worked is worse than no seam at all, because the gap stops being visible.
/// </para>
/// <para>
/// It is not a degraded mode and it does not break anything. Sales still earn, cycles still close,
/// batches are still built and approved, and the ledger still says to the paisa what every seller is
/// owed. The only thing that does not happen is the transfer, and the batch records that as an
/// unavailable provider rather than a failure the seller caused.
/// </para>
/// <para>
/// It logs at Information with the seller and the amount, so an operator reading the log after
/// credentials arrive can see exactly what is waiting to go out.
/// </para>
/// </remarks>
/// <param name="logger">Reports each transfer that would have been sent.</param>
internal sealed partial class UnconfiguredPayoutProvider(ILogger<UnconfiguredPayoutProvider> logger)
    : IPayoutProvider
{
    /// <inheritdoc />
    public string Name => PayoutProviders.None;

    /// <inheritdoc />
    public bool IsConfigured => false;

    /// <inheritdoc />
    public Task<Result<ProviderPayout>> SendAsync(PayoutRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        PayoutNotSent(logger, request.VendorId, request.Amount, request.Reference);

        return Task.FromResult(Result.Failure<ProviderPayout>(SettlementsErrors.ProviderUnavailable));
    }

    /// <inheritdoc />
    public Task<Result<ProviderPayout>> FetchAsync(string providerPayoutId, CancellationToken cancellationToken)
        => Task.FromResult(Result.Failure<ProviderPayout>(SettlementsErrors.ProviderUnavailable));

    [LoggerMessage(EventId = 1825, Level = LogLevel.Information,
        Message = "No payout provider is configured, so {Amount} owed to vendor {VendorId} in batch "
                  + "{BatchReference} was not sent. The ledger still says it is owed.")]
    private static partial void PayoutNotSent(
        ILogger logger,
        Guid vendorId,
        decimal amount,
        string batchReference);
}

/// <summary>
/// Picks the adapter a payout goes through.
/// </summary>
/// <remarks>
/// <para>
/// Adapters are keyed by the rail they are, and <c>Payouts:Provider</c> names one — the same
/// arrangement ADR-018 settled on for couriers, and for the same reasons. Switching rails is a
/// configuration value; a batch already sent through one rail is still readable through the adapter
/// that holds it, whatever the default has since become; and adding a rail is one registration.
/// </para>
/// <para>
/// Falling through to <see cref="UnconfiguredPayoutProvider"/> rather than throwing is deliberate. A
/// deployment that names a rail this build does not have, or names one whose credentials are blank,
/// gets a batch that refuses to send with a named error — not a container that will not start.
/// </para>
/// </remarks>
/// <param name="providers">Every adapter this build has.</param>
/// <param name="options">Names the rail.</param>
internal sealed class PayoutProviderRegistry(
    IEnumerable<IPayoutProvider> providers,
    IOptionsMonitor<PayoutOptions> options)
{
    private readonly IReadOnlyList<IPayoutProvider> _providers = [.. providers];

    /// <summary>The adapter new transfers go to: the configured rail where it works, nothing where it does not.</summary>
    public IPayoutProvider Default
    {
        get
        {
            var configured = Find(options.CurrentValue.Provider);

            return configured is { IsConfigured: true } and not UnconfiguredPayoutProvider
                ? configured
                : Unconfigured;
        }
    }

    /// <summary>The adapter that sends nothing, which is always available.</summary>
    public IPayoutProvider Unconfigured
        => _providers.First(provider => provider.Name == PayoutProviders.None);

    /// <summary>Whether transfers will actually reach a gateway.</summary>
    public bool CanSend => Default.Name != PayoutProviders.None;

    /// <summary>The adapter for a rail name, or null when this build has none.</summary>
    /// <param name="name">The rail as it is configured, or as it is stored on a batch.</param>
    public IPayoutProvider? Find(string? name)
        => string.IsNullOrWhiteSpace(name)
            ? null
            : _providers.FirstOrDefault(provider =>
                string.Equals(provider.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The adapter for a batch, falling back to the one that sends nothing rather than failing.
    /// </summary>
    /// <remarks>
    /// A batch sent by an adapter this build no longer has is still a batch, and an operator has to be
    /// able to read what happened to it. Asking the unconfigured adapter returns an honest "not
    /// available" rather than an exception in a reconciliation sweep.
    /// </remarks>
    /// <param name="providerName">The rail recorded on the batch.</param>
    public IPayoutProvider For(string? providerName) => Find(providerName) ?? Unconfigured;
}
