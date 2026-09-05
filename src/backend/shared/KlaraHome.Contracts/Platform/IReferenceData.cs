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
}
