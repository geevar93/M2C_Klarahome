using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Platform.Domain;

/// <summary>
/// One section of store configuration, stored as the JSON document the section's typed record
/// serialises to (docs/03-database-design.md §4.1).
/// </summary>
/// <remarks>
/// The value is opaque here on purpose. The Domain layer knows that a setting has a key, a
/// document and a visibility; what shape that document has is the section type's business, and
/// keeping it out of the entity is what lets a section gain a field without a migration.
/// </remarks>
internal sealed class StoreSetting : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private StoreSetting(Guid id, string key, string value, bool isPublic)
        : base(id)
    {
        Key = Guard.NotNullOrWhiteSpace(key);
        Value = Guard.NotNullOrWhiteSpace(value);
        IsPublic = isPublic;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private StoreSetting()
    {
        Key = string.Empty;
        Value = string.Empty;
    }

    /// <summary>The section key, unique within the tenant.</summary>
    public string Key { get; private set; }

    /// <summary>The section value as a JSON document.</summary>
    public string Value { get; private set; }

    /// <summary>Whether the storefront may read this section without authenticating.</summary>
    public bool IsPublic { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; private set; }

    /// <summary>Creates a section row. The tenant is stamped by the persistence layer.</summary>
    /// <param name="key">The section key.</param>
    /// <param name="value">The section value as JSON.</param>
    /// <param name="isPublic">Whether the storefront may read it.</param>
    public static StoreSetting Create(string key, string value, bool isPublic)
        => new(UuidV7.New(), key, value, isPublic);

    /// <summary>Replaces the whole document. A section is edited as a unit, never field by field.</summary>
    /// <param name="value">The new value as JSON.</param>
    public void Replace(string value) => Value = Guard.NotNullOrWhiteSpace(value);

    /// <summary>Aligns the stored visibility with the section type's current declaration.</summary>
    /// <param name="isPublic">Whether the storefront may read it.</param>
    public void SetVisibility(bool isPublic) => IsPublic = isPublic;
}
