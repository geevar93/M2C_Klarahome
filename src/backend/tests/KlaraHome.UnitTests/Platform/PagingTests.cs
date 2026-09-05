using KlaraHome.Infrastructure.Http;
using KlaraHome.Modules.Platform.Application.Auditing;

namespace KlaraHome.UnitTests.Platform;

/// <summary>
/// A cursor is handed back to a client and returned unchanged. Anything it cannot round-trip is a
/// page the caller can never reach.
/// </summary>
public sealed class PagingTests
{
    [Fact]
    public void A_missing_page_size_becomes_the_default()
    {
        Assert.Equal(Cursor.DefaultPageSize, Cursor.NormalizeSize(null));
        Assert.Equal(Cursor.DefaultPageSize, Cursor.NormalizeSize(0));
        Assert.Equal(Cursor.DefaultPageSize, Cursor.NormalizeSize(-5));
    }

    [Fact]
    public void A_page_size_beyond_the_maximum_is_clamped_rather_than_refused()
    {
        Assert.Equal(Cursor.MaxPageSize, Cursor.NormalizeSize(10_000));
        Assert.Equal(50, Cursor.NormalizeSize(50));
    }

    [Fact]
    public void A_cursor_round_trips()
    {
        const string Key = "2026-09-05T12:00:00.1234567+00:00|0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b";

        Assert.True(Cursor.TryDecode(Cursor.Encode(Key), out var decoded));
        Assert.Equal(Key, decoded);
    }

    [Fact]
    public void A_cursor_survives_a_query_string_without_escaping()
    {
        var token = Cursor.Encode("2026-09-05T12:00:00.0000000+00:00|0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b");

        // base64url, so no '+', '/' or '=' to be mangled by a client that forgets to escape it.
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
    }

    [Fact]
    public void A_malformed_cursor_is_rejected_rather_than_thrown()
    {
        // It arrives in a URL, so it is a 400, never a 500.
        Assert.False(Cursor.TryDecode(null, out _));
        Assert.False(Cursor.TryDecode(string.Empty, out _));
        Assert.False(Cursor.TryDecode("!!!not-base64!!!", out _));
    }

    [Fact]
    public void An_audit_cursor_round_trips_to_the_microsecond()
    {
        // Postgres stores timestamptz to the microsecond. If the cursor lost precision, the row it
        // points at would be re-read on the next page.
        var occurredAt = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero).AddTicks(1234);
        var id = Guid.CreateVersion7();

        Assert.True(AuditCursor.TryDecode(AuditCursor.Encode(occurredAt, id), out var time, out var decodedId));

        Assert.Equal(occurredAt, time);
        Assert.Equal(id, decodedId);
    }

    [Fact]
    public void An_audit_cursor_that_is_not_a_timestamp_and_an_id_is_rejected()
    {
        Assert.False(AuditCursor.TryDecode(Cursor.Encode("not-a-cursor"), out _, out _));
        Assert.False(AuditCursor.TryDecode(Cursor.Encode("2026-09-05T12:00:00Z|not-a-guid"), out _, out _));
        Assert.False(AuditCursor.TryDecode("!!!", out _, out _));
    }
}
