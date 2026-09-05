using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.UnitTests.Persistence;

/// <summary>
/// Key generation is a database decision, not a formatting one: a v4 key would scatter every
/// insert across the primary-key B-tree, which is the cost these tests exist to protect against.
/// </summary>
public sealed class UuidV7Tests
{
    [Fact]
    public void A_generated_id_is_a_version_7_uuid()
        => Assert.NotNull(UuidV7.TimestampOf(UuidV7.New()));

    [Fact]
    public void Guid_NewGuid_is_not_mistaken_for_one()
    {
        // The check has to be able to fail, or it proves nothing about what it accepts.
        Assert.Null(UuidV7.TimestampOf(Guid.NewGuid()));
        Assert.Null(UuidV7.TimestampOf(Guid.Empty));
    }

    [Fact]
    public void Ids_generated_in_order_sort_in_order()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var ids = Enumerable
            .Range(0, 64)
            .Select(offset => UuidV7.NewAt(start.AddMilliseconds(offset)))
            .ToList();

        // Ordinal string ordering matches index ordering for the timestamp prefix, which is the
        // property that keeps inserts at the right-hand edge of the index.
        Assert.Equal(
            ids.Select(id => id.ToString("n")).Order(StringComparer.Ordinal),
            ids.Select(id => id.ToString("n")));
    }

    [Fact]
    public void The_encoded_timestamp_is_the_one_supplied()
    {
        var when = new DateTimeOffset(2026, 9, 5, 10, 30, 0, TimeSpan.Zero);

        Assert.Equal(when, UuidV7.TimestampOf(UuidV7.NewAt(when)));
    }
}

/// <summary>
/// A deployment that loses its tenant id loses every row it wrote. These tests pin the fallback
/// so the behaviour is a decision rather than an accident.
/// </summary>
public sealed class TenantIdDerivationTests
{
    [Fact]
    public void A_derived_id_is_stable_across_restarts()
        => Assert.Equal(ConfiguredTenantContext.Derive("klarahome"), ConfiguredTenantContext.Derive("klarahome"));

    [Fact]
    public void Different_codes_derive_different_ids()
        => Assert.NotEqual(ConfiguredTenantContext.Derive("klarahome"), ConfiguredTenantContext.Derive("otherstore"));

    [Fact]
    public void A_derived_id_is_a_well_formed_rfc_9562_uuid()
    {
        Span<byte> bytes = stackalloc byte[16];
        Assert.True(ConfiguredTenantContext.Derive("klarahome").TryWriteBytes(bytes, bigEndian: true, out _));

        Assert.Equal(0x80, bytes[6] & 0xF0); // version 8, custom
        Assert.Equal(0x80, bytes[8] & 0xC0); // RFC 9562 variant
    }

    [Fact]
    public void A_derived_id_is_never_empty()
        => Assert.NotEqual(Guid.Empty, ConfiguredTenantContext.Derive("klarahome"));
}
