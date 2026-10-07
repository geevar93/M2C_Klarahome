namespace KlaraHome.Contracts.Platform;

/// <summary>
/// Turns the ids an audit entry carries into words a person can read, for one kind of entity.
/// </summary>
/// <remarks>
/// <para>
/// An audit row stores <em>what was touched</em> as a type name and an id, because that is all the
/// trail may rely on: it is append-only, outlives the thing it describes, and sits in the Platform
/// module, which may not read another module's tables. A screen that shows "User · 01a10867…" is
/// correct and useless. So the owner of each kind of entity implements this for the types it
/// audits, and the Platform module asks every implementation, once per page, for the ids on it.
/// </para>
/// <para>
/// The labels are read live, not frozen into the row. That is deliberate: the trail records the
/// fact, and the label is a courtesy for the reader of the day, so a product renamed since is shown
/// by its current name. An id with no live entity — deleted, or never a row — is simply absent from
/// the answer and the screen falls back to the id.
/// </para>
/// </remarks>
public interface IAuditLabelSource
{
    /// <summary>
    /// The <c>EntityType</c> this source labels, exactly as the audit entries spell it
    /// (<c>Product</c>, <c>User</c>). The identity user type also labels the <em>actor</em> of every entry.
    /// </summary>
    string EntityType { get; }

    /// <summary>One label per id that resolves, keyed by the id as the audit entry stores it.</summary>
    /// <param name="entityIds">The ids to resolve. Never empty; ids that are not this entity's shape are ignored.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyDictionary<string, string>> LabelsAsync(
        IReadOnlyCollection<string> entityIds,
        CancellationToken cancellationToken = default);
}
