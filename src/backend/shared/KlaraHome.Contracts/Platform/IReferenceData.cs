namespace KlaraHome.Contracts.Platform;

/// <summary>
/// Reads the Indian reference data the Platform module owns: the states and union territories,
/// and the PIN codes that map to them.
/// </summary>
/// <remarks>
/// <para>
/// Cross-schema foreign keys are not allowed (docs/01-architecture.md §2.1), so a module that
/// stores a <c>state_id</c> holds a plain UUID and validates it through this contract. Without
/// one, "is this a real state" is either an unchecked assumption or a query across a boundary,
/// and both of those end at a GST return.
/// </para>
/// <para>
/// The list changes when Parliament creates a state. Implementations cache accordingly.
/// </para>
/// </remarks>
public interface IReferenceData
{
    /// <summary>Whether an id identifies a state or union territory.</summary>
    /// <param name="stateId">The id stored against an address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<bool> StateExistsAsync(Guid stateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The GST state code for a state id, or null if the id is unknown.
    /// </summary>
    /// <remarks>
    /// The code, not the name: place of supply is computed from it, and it is the value that
    /// appears on the invoice.
    /// </remarks>
    /// <param name="stateId">The id stored against an address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<string?> StateCodeAsync(Guid stateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What a PIN code is, from the platform's own seeded reference data, or null when it has no row
    /// for it.
    /// </summary>
    /// <remarks>
    /// Added at Step 16A for the delivery-coverage check (ADR-018), which has to know what city a
    /// destination is in before it can say whether the store delivers there. This is the
    /// authoritative answer; a courier's opinion about the same PIN code is the fallback, and the
    /// order matters — reference data does not change when an API has a bad day.
    /// </remarks>
    /// <param name="pincode">The six-digit PIN code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<PincodeInfo?> PincodeAsync(string pincode, CancellationToken cancellationToken = default);
}

/// <summary>What the platform knows about one PIN code.</summary>
/// <param name="Code">The six-digit PIN code.</param>
/// <param name="City">The city or town it serves.</param>
/// <param name="District">The revenue district.</param>
/// <param name="StateId">The state it is in, as other modules store it.</param>
/// <param name="StateName">That state's name.</param>
/// <param name="StateCode">That state's two-digit GST code.</param>
public sealed record PincodeInfo(
    string Code,
    string City,
    string District,
    Guid StateId,
    string StateName,
    string StateCode);
