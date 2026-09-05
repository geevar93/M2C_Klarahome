using System.Buffers.Text;
using System.Text;

namespace KlaraHome.Infrastructure.Http;

/// <summary>
/// The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
/// Defined once so no two endpoints invent their own shape and no client has to special-case one.
/// </summary>
/// <typeparam name="TItem">The item type.</typeparam>
/// <param name="Items">The page of results, in the endpoint's declared order.</param>
/// <param name="Page">Where this page sits and how to ask for the next one.</param>
public sealed record PagedResult<TItem>(IReadOnlyList<TItem> Items, PageInfo Page);

/// <summary>Pagination metadata.</summary>
/// <param name="Size">How many items were requested for this page.</param>
/// <param name="NextCursor">
/// Opaque token for the following page, or null when this is the last one. Keyset rather than
/// offset: a page that is fetched while rows are being inserted must not repeat or skip a row.
/// </param>
/// <param name="PrevCursor">
/// Opaque token for the preceding page, or null. Null on forward-only endpoints; the UI keeps the
/// cursors it has already seen rather than asking the server to walk backwards.
/// </param>
/// <param name="Total">
/// Total matching rows, or null where counting is too expensive to do on every page. The UI must
/// not depend on it (docs/04-api-specification.md §1.1).
/// </param>
public sealed record PageInfo(int Size, string? NextCursor, string? PrevCursor = null, long? Total = null);

/// <summary>
/// Encodes and decodes the opaque page cursors. The value is base64url of the raw key so it
/// survives a query string, and it is deliberately opaque so its shape can change without
/// breaking a client that stored one.
/// </summary>
/// <remarks>
/// It is an encoding, not a signature: a cursor carries no authority and reveals nothing a caller
/// could not already see in the results it came with. Every endpoint still applies its own filter
/// and its own tenant scope to the decoded key.
/// </remarks>
public static class Cursor
{
    /// <summary>The largest page any endpoint will serve, however large a size is requested.</summary>
    public const int MaxPageSize = 200;

    /// <summary>The page size used when the caller does not ask for one.</summary>
    public const int DefaultPageSize = 25;

    /// <summary>Clamps a requested page size into the range the API will actually serve.</summary>
    /// <param name="requested">The size the caller asked for, if any.</param>
    public static int NormalizeSize(int? requested)
        => requested is null or < 1 ? DefaultPageSize : Math.Min(requested.Value, MaxPageSize);

    /// <summary>Encodes a raw key as a cursor token.</summary>
    /// <param name="key">The key that identifies the last row of the current page.</param>
    public static string Encode(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(key));
    }

    /// <summary>
    /// Decodes a cursor token, or returns false. A malformed cursor is a client error, never an
    /// exception: the endpoint answers 400 with the reason rather than a stack trace.
    /// </summary>
    /// <param name="token">The token supplied by the caller.</param>
    /// <param name="key">The decoded key.</param>
    public static bool TryDecode(string? token, out string key)
    {
        key = string.Empty;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            key = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
