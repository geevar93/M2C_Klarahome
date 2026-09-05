using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Platform.Domain;

/// <summary>
/// An Indian state or union territory, with the GST state code that drives place of supply
/// (docs/03-database-design.md §4.1).
/// </summary>
/// <remarks>
/// Reference data, not business data: it is the same for every deployment, nobody edits it, and it
/// therefore carries no <c>tenant_id</c>. That is a deliberate exception to the tenancy convention
/// in §1 — which governs business tables — and it is why these rows can be seeded once and shared.
/// </remarks>
internal sealed class StateOrUnionTerritory
{
    private StateOrUnionTerritory(Guid id, string code, string name, StateKind kind)
    {
        Id = id;
        Code = Guard.NotNullOrWhiteSpace(code);
        Name = Guard.NotNullOrWhiteSpace(name);
        Kind = kind;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private StateOrUnionTerritory()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Surrogate key. The GST code is the natural key, but a UUID keeps the FK shape uniform.</summary>
    public Guid Id { get; private set; }

    /// <summary>Two-digit GST state code, for example <c>27</c> for Maharashtra. Unique.</summary>
    public string Code { get; private set; }

    /// <summary>Official name.</summary>
    public string Name { get; private set; }

    /// <summary>Whether it is a state or a union territory.</summary>
    public StateKind Kind { get; private set; }

    /// <summary>Builds a reference row with a deterministic id, so re-seeding never duplicates one.</summary>
    /// <param name="code">The GST state code.</param>
    /// <param name="name">Official name.</param>
    /// <param name="kind">State or union territory.</param>
    public static StateOrUnionTerritory Define(string code, string name, StateKind kind)
        => new(ReferenceIds.For("state", code), code, name, kind);

    /// <summary>Corrects the name or classification of an existing jurisdiction.</summary>
    /// <remarks>
    /// The GST code is the identity and never changes; the name and the classification do. Orissa
    /// became Odisha, and Jammu and Kashmir went from state to union territory, both keeping their
    /// codes.
    /// </remarks>
    /// <param name="name">The current official name.</param>
    /// <param name="kind">State or union territory.</param>
    public void Rename(string name, StateKind kind)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        Kind = kind;
    }
}

/// <summary>Whether a jurisdiction is a state or a union territory.</summary>
internal enum StateKind
{
    /// <summary>One of the 28 states.</summary>
    State = 0,

    /// <summary>One of the 8 union territories.</summary>
    UnionTerritory = 1,
}

/// <summary>
/// A postal index number and the place it identifies, used to autofill an address and — from
/// Step 16 — to answer serviceability (docs/04-api-specification.md §3.6).
/// </summary>
/// <remarks>
/// The full dataset is roughly nineteen thousand rows published by India Post. It is imported by
/// an operator rather than compiled into the product: it changes without notice, and a stale copy
/// baked into a release would be worse than an empty table an operator knows to fill.
/// </remarks>
internal sealed class Pincode
{
    private Pincode(Guid id, string code, string city, string district, Guid stateId, string zone)
    {
        Id = id;
        Code = Guard.NotNullOrWhiteSpace(code);
        City = Guard.NotNullOrWhiteSpace(city);
        District = district ?? string.Empty;
        StateId = stateId;
        Zone = zone ?? string.Empty;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Pincode()
    {
        Code = string.Empty;
        City = string.Empty;
        District = string.Empty;
        Zone = string.Empty;
    }

    /// <summary>Surrogate key.</summary>
    public Guid Id { get; private set; }

    /// <summary>The six-digit PIN code. Unique.</summary>
    public string Code { get; private set; }

    /// <summary>City or town the PIN code serves.</summary>
    public string City { get; private set; }

    /// <summary>Revenue district.</summary>
    public string District { get; private set; }

    /// <summary>The owning state or union territory.</summary>
    public Guid StateId { get; private set; }

    /// <summary>Logistics zone, used later to band shipping rates.</summary>
    public string Zone { get; private set; }

    /// <summary>Builds a row with a deterministic id, so re-importing the dataset updates in place.</summary>
    /// <param name="code">The six-digit PIN code.</param>
    /// <param name="city">City or town.</param>
    /// <param name="district">Revenue district.</param>
    /// <param name="stateId">The owning state.</param>
    /// <param name="zone">Logistics zone.</param>
    public static Pincode Define(string code, string city, string district, Guid stateId, string zone)
        => new(ReferenceIds.For("pincode", code), code, city, district, stateId, zone);

    /// <summary>Updates the place details of an existing PIN code.</summary>
    /// <param name="city">City or town.</param>
    /// <param name="district">Revenue district.</param>
    /// <param name="stateId">The owning state.</param>
    /// <param name="zone">Logistics zone.</param>
    public void Update(string city, string district, Guid stateId, string zone)
    {
        City = Guard.NotNullOrWhiteSpace(city);
        District = district ?? string.Empty;
        StateId = stateId;
        Zone = zone ?? string.Empty;
    }
}

/// <summary>
/// A Harmonized System code. Seeded at chapter level — the 99 two-digit chapters — which is what a
/// catalogue needs to classify a product before a tax rate is attached to it.
/// </summary>
/// <remarks>
/// <see cref="DefaultGstRate"/> is null for every seeded chapter, deliberately. GST rates are set
/// at four, six and eight digits, not at chapter level, and a plausible-looking chapter rate is
/// exactly the kind of wrong number that reaches an invoice. The Pricing and Tax module fills in
/// real rates against real codes at Step 12.
/// </remarks>
internal sealed class HsnCode
{
    private HsnCode(Guid id, string code, string description, decimal? defaultGstRate)
    {
        Id = id;
        Code = Guard.NotNullOrWhiteSpace(code);
        Description = Guard.NotNullOrWhiteSpace(description);
        DefaultGstRate = defaultGstRate;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private HsnCode()
    {
        Code = string.Empty;
        Description = string.Empty;
    }

    /// <summary>Surrogate key.</summary>
    public Guid Id { get; private set; }

    /// <summary>The HSN code. Two digits for a chapter, up to eight for a tariff item. Unique.</summary>
    public string Code { get; private set; }

    /// <summary>What the code covers.</summary>
    public string Description { get; private set; }

    /// <summary>Default GST rate as a percentage, or null where no single rate applies.</summary>
    public decimal? DefaultGstRate { get; private set; }

    /// <summary>Builds a row with a deterministic id, so re-seeding updates rather than duplicates.</summary>
    /// <param name="code">The HSN code.</param>
    /// <param name="description">What it covers.</param>
    /// <param name="defaultGstRate">Default GST rate, or null.</param>
    public static HsnCode Define(string code, string description, decimal? defaultGstRate = null)
        => new(ReferenceIds.For("hsn", code), code, description, defaultGstRate);

    /// <summary>Updates the description and rate of an existing code.</summary>
    /// <param name="description">What it covers.</param>
    /// <param name="defaultGstRate">Default GST rate, or null.</param>
    public void Update(string description, decimal? defaultGstRate)
    {
        Description = Guard.NotNullOrWhiteSpace(description);
        DefaultGstRate = defaultGstRate;
    }
}

/// <summary>
/// Derives a stable id from a reference row's natural key.
/// </summary>
/// <remarks>
/// Reference data is seeded on every deploy and must land on the same rows each time. A UUIDv7
/// would be a new key on every run, so these rows use a name-based UUID over (kind, natural key)
/// instead — version 8 per RFC 9562 §5.8, the same construction
/// <c>ConfiguredTenantContext</c> uses. It is the one sanctioned exception to
/// <see cref="UuidV7"/>: these keys are not time-ordered because they are not inserted over time.
/// </remarks>
internal static class ReferenceIds
{
    private static readonly Guid Namespace = new("3f1c5c2a-7d4e-4a51-9b1c-0d8b2f6a4e37");

    /// <summary>The deterministic id for a reference row.</summary>
    /// <param name="kind">The row's table, so a state and a PIN code with the same code differ.</param>
    /// <param name="naturalKey">The row's natural key.</param>
    public static Guid For(string kind, string naturalKey)
    {
        var input = System.Text.Encoding.UTF8.GetBytes($"{kind}:{naturalKey}");

        Span<byte> seed = stackalloc byte[16 + 64];
        Namespace.TryWriteBytes(seed[..16], bigEndian: true, out _);
        input.AsSpan(0, Math.Min(input.Length, 64)).CopyTo(seed[16..]);

        Span<byte> hash = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(seed[..(16 + Math.Min(input.Length, 64))], hash);

        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80); // version 8: custom
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 9562 variant

        return new Guid(bytes, bigEndian: true);
    }
}
