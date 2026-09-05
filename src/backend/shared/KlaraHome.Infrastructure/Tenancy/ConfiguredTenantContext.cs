using System.Security.Cryptography;
using System.Text;
using KlaraHome.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KlaraHome.Infrastructure.Tenancy;

/// <summary>
/// The single-tenant implementation used until the Platform module owns tenancy at Step 6.
/// </summary>
/// <remarks>
/// <para>
/// <c>Tenant:Id</c> should be set explicitly for any deployment that holds data. When it is not,
/// the id is <b>derived deterministically from the tenant code</b> so that restarting the
/// container does not orphan every row it wrote — the failure mode a random id would produce.
/// The derived value is logged at startup precisely because the derivation is a convenience, not
/// a guarantee: changing <c>Tenant:Code</c> changes the id and strands the existing data.
/// </para>
/// </remarks>
internal sealed class ConfiguredTenantContext : ITenantContext
{
    private static readonly Guid Namespace = new("6b1b8f2e-2f7f-4d3a-9f2a-2c9a1f0b7d51");

    public ConfiguredTenantContext(IOptions<TenantOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Code = options.Value.Code;
        TenantId = options.Value.Id ?? Derive(Code);
        IsDerived = options.Value.Id is null;
    }

    public Guid TenantId { get; }

    public string Code { get; }

    /// <summary>True when <c>Tenant:Id</c> was not configured and the id was derived.</summary>
    public bool IsDerived { get; }

    /// <summary>
    /// A name-based UUID over (namespace, code), version 8 per RFC 9562 §5.8. Version 5 would
    /// require SHA-1, which this codebase does not use anywhere; the guarantee needed here is
    /// determinism, not interoperability with another v5 generator.
    /// </summary>
    /// <param name="code">The tenant code to derive from.</param>
    internal static Guid Derive(string code)
    {
        Span<byte> input = stackalloc byte[16 + 64];
        Namespace.TryWriteBytes(input[..16], bigEndian: true, out _);

        var codeLength = Encoding.UTF8.GetBytes(code, input[16..]);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input[..(16 + codeLength)], hash);

        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80); // version 8: custom
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 9562 variant

        return new Guid(bytes, bigEndian: true);
    }
}
