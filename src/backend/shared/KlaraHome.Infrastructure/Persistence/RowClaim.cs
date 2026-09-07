using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Infrastructure.Persistence;

/// <summary>
/// Claims rows for one worker with <c>FOR UPDATE SKIP LOCKED</c>, correctly, in one place.
/// </summary>
/// <remarks>
/// <para>
/// Every polling loop on this platform does the same thing: take a batch of rows nobody else is
/// working on, hold them for the length of a transaction, and let a second worker step over them.
/// Two details make it correct and both are easy to leave out. The rows have to be locked with
/// <c>FOR UPDATE SKIP LOCKED</c>, and the projection has to be <c>SELECT *, xmin</c> rather than
/// <c>SELECT *</c> — <c>xmin</c> is a system column, so the star omits it, and it is what every
/// entity here maps its concurrency token to.
/// </para>
/// <para>
/// Step 28A found ten loops that had written <c>SELECT *</c> and fixed all ten. This exists so
/// there cannot be an eleventh: a caller supplies the predicate, the order and the batch size, and
/// the two things that were forgotten are not theirs to write (Step 28B, deliverable 24).
/// </para>
/// <para>
/// The predicate arrives as an interpolated string and is parameterised by EF, exactly as it was at
/// each call site. The table and the ordering are <em>not</em> parameterisable in SQL, so they are
/// checked against a strict identifier pattern instead and a value that does not match throws at the
/// call rather than reaching the database. Neither ever comes from a request — both are constants a
/// developer wrote — and the check is there so that stays true.
/// </para>
/// </remarks>
public static partial class RowClaim
{
    /// <summary>
    /// Claims a batch of rows for this worker.
    /// </summary>
    /// <remarks>
    /// The caller must already be in a transaction, and must keep it open until the work is
    /// committed: the lock is held until the transaction ends, and that is the whole mechanism. A
    /// claim taken outside one is released the instant the query returns, and two workers will then
    /// happily process the same row.
    /// </remarks>
    /// <typeparam name="TEntity">The entity being claimed.</typeparam>
    /// <param name="context">The context to claim through.</param>
    /// <param name="table">
    /// The schema-qualified table, as SQL spells it — <c>carts.carts</c>. A constant, never a value
    /// from a request.
    /// </param>
    /// <param name="where">The predicate. Interpolated values become query parameters.</param>
    /// <param name="orderBy">
    /// The ordering, as SQL spells it — <c>occurred_at</c>, <c>expires_at DESC</c>. Oldest first is
    /// almost always what a queue wants.
    /// </param>
    /// <param name="limit">How many rows to claim.</param>
    /// <returns>The claimed rows, tracked, and locked until the transaction ends.</returns>
    /// <exception cref="ArgumentException">The table or ordering is not a plain SQL identifier.</exception>
    public static IQueryable<TEntity> Claim<TEntity>(
        this DbContext context,
        string table,
        FormattableString where,
        string orderBy,
        int limit)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(where);

        Validate(table, nameof(table), TableName());
        Validate(orderBy, nameof(orderBy), OrderByClause());

        // The limit is appended as one more parameter rather than written into the text, so a batch
        // size read from configuration cannot become SQL. Its placeholder is the next free index,
        // which is exactly the number of arguments the predicate already carries.
        var arguments = new object?[where.ArgumentCount + 1];
        where.GetArguments().CopyTo(arguments, 0);
        arguments[^1] = limit;

        var limitPlaceholder = "{" + where.ArgumentCount.ToString(CultureInfo.InvariantCulture) + "}";

        var sql = $"""
                   SELECT {Projection<TEntity>(context)} FROM {table}
                   WHERE {where.Format}
                   ORDER BY {orderBy}
                   LIMIT {limitPlaceholder}
                   FOR UPDATE SKIP LOCKED
                   """;

        return context.Set<TEntity>().FromSql(FormattableStringFactory.Create(sql, arguments));
    }

    /// <summary>
    /// The column list: <c>*, xmin</c> where the entity maps a concurrency token, and <c>*</c> where
    /// it does not.
    /// </summary>
    /// <remarks>
    /// Read off the model rather than chosen by the caller, which is the whole reason this helper is
    /// worth having. Both spellings are correct for some table and wrong for others: naming
    /// <c>xmin</c> is required wherever it is the concurrency token, because <c>SELECT *</c> omits a
    /// system column and EF then materialises a row with no token — and it is <em>impossible</em> on
    /// a partitioned table, which is why <c>notifications.notification_messages</c> deliberately maps
    /// none. A caller cannot get this wrong because a caller is never asked.
    /// </remarks>
    private static string Projection<TEntity>(DbContext context)
        where TEntity : class
    {
        var mapsToken = context.Model
            .FindEntityType(typeof(TEntity))?
            .GetProperties()
            .Any(property => property.IsConcurrencyToken
                             && string.Equals(
                                 property.GetColumnName(),
                                 ModelConventions.ConcurrencyTokenName,
                                 StringComparison.Ordinal))
            ?? false;

        return mapsToken ? "*, " + ModelConventions.ConcurrencyTokenName : "*";
    }

    private static void Validate(string value, string parameterName, Regex pattern)
    {
        if (string.IsNullOrWhiteSpace(value) || !pattern.IsMatch(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a plain SQL identifier. A claim's table and ordering are written into "
                + "the statement rather than parameterised, so they must be constants a developer wrote.",
                parameterName);
        }
    }

    /// <summary>A schema-qualified table name, optionally quoted: <c>carts.carts</c>.</summary>
    [GeneratedRegex("""^"?[A-Za-z_][A-Za-z0-9_]*"?(\."?[A-Za-z_][A-Za-z0-9_]*"?)?$""", RegexOptions.CultureInvariant)]
    private static partial Regex TableName();

    /// <summary>One or more column names, each optionally with a direction.</summary>
    [GeneratedRegex(
        """^[A-Za-z_][A-Za-z0-9_]*( (ASC|DESC))?(, ?[A-Za-z_][A-Za-z0-9_]*( (ASC|DESC))?)*$""",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex OrderByClause();
}
