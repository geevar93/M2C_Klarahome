namespace KlaraHome.SharedKernel.Primitives;

/// <summary>
/// The single sanctioned way to mint a primary key. Every <c>id</c> in the database is a
/// time-ordered UUIDv7 generated in application code (docs/03-database-design.md §1): the row
/// knows its own identity before it is saved, inserts stay at the right-hand edge of the B-tree
/// instead of scattering across it, and the value is safe to expose in a URL.
/// </summary>
/// <remarks>
/// <para>
/// A wrapper over <see cref="Guid.CreateVersion7()"/> rather than a re-implementation. It exists
/// so key generation has one greppable call site, so a test can pin the timestamp, and so
/// <c>Guid.NewGuid()</c> — which would produce a random v4 and fragment every index — is easy to
/// spot in review.
/// </para>
/// </remarks>
public static class UuidV7
{
    /// <summary>A new time-ordered identifier stamped with the current UTC instant.</summary>
    public static Guid New() => Guid.CreateVersion7();

    /// <summary>
    /// A new time-ordered identifier stamped with <paramref name="timestamp"/>. Used by tests
    /// that need ordering to be deterministic, and by back-fills that must preserve the original
    /// event order rather than the order rows happened to be written.
    /// </summary>
    /// <param name="timestamp">The instant to encode into the identifier.</param>
    public static Guid NewAt(DateTimeOffset timestamp) => Guid.CreateVersion7(timestamp);

    /// <summary>
    /// The instant encoded in a UUIDv7, or <see langword="null"/> if the value is not a v7 —
    /// which makes "was this key generated the way we require?" answerable in a test.
    /// </summary>
    /// <param name="id">The identifier to read.</param>
    public static DateTimeOffset? TimestampOf(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        if (!id.TryWriteBytes(bytes, bigEndian: true, out _))
        {
            return null;
        }

        // Version lives in the high nibble of octet 6; variant in the top bits of octet 8.
        var isVersion7 = (bytes[6] & 0xF0) == 0x70;
        var isRfcVariant = (bytes[8] & 0xC0) == 0x80;

        if (!isVersion7 || !isRfcVariant)
        {
            return null;
        }

        // Octets 0-5 are a big-endian count of milliseconds since the Unix epoch.
        long milliseconds = 0;
        for (var i = 0; i < 6; i++)
        {
            milliseconds = (milliseconds << 8) | bytes[i];
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    }
}
