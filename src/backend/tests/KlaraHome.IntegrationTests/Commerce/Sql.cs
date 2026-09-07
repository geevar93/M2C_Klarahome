using System.Data;
using System.Globalization;
using Npgsql;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Reads the database directly, for the assertions the API cannot make.
/// </summary>
/// <remarks>
/// <para>
/// Used sparingly and deliberately. Most of what these tests assert should be asserted through the
/// API, because that is what a client can see and what a refactor must not break. But a handful of
/// the acceptance criteria are explicitly about what is <em>in</em> the database and could not be
/// about anything else: that an integration event landed in <c>platform.outbox_messages</c> in the
/// same transaction as the status change that caused it; that a bank account number is ciphertext
/// at rest rather than a string; that a partial unique index refused a second primary account. An
/// endpoint that never returns those values cannot be asked about them.
/// </para>
/// <para>
/// Raw SQL rather than a module's <c>DbContext</c>, on purpose: a query filter, a value converter
/// or a shadow property would be exactly the thing under test, and running the assertion through
/// the same machinery would let it agree with itself.
/// </para>
/// </remarks>
/// <param name="connectionString">The migrated test database.</param>
internal sealed class Sql(string connectionString)
{
    /// <summary>The migrated test database, for the rare test that opens its own connection.</summary>
    /// <remarks>
    /// One test needs two connections held open at the same time — proving that
    /// <c>FOR UPDATE SKIP LOCKED</c> lets a second worker step over a claim the first is still
    /// holding, which is only observable while both transactions are alive. Every other test uses
    /// the methods below, which open and close a connection per call.
    /// </remarks>
    public string ConnectionString => connectionString;

    /// <summary>Reads a single scalar, or null when the query selected no row.</summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="commandText">The query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="parameters">Positional parameters, referenced as <c>$1</c>, <c>$2</c>…</param>
    public async Task<T?> ScalarAsync<T>(
        string commandText,
        CancellationToken cancellationToken,
        params object?[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = Build(connection, commandText, parameters);

        var value = await command.ExecuteScalarAsync(cancellationToken);

        return value is null or DBNull ? default : (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
    }

    /// <summary>Counts the rows a query matches.</summary>
    /// <param name="commandText">A query that selects a count.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="parameters">Positional parameters, referenced as <c>$1</c>, <c>$2</c>…</param>
    public async Task<long> CountAsync(
        string commandText,
        CancellationToken cancellationToken,
        params object?[] parameters)
        => await ScalarAsync<long>(commandText, cancellationToken, parameters);

    /// <summary>Reads every row a query matches, as dictionaries keyed by column name.</summary>
    /// <param name="commandText">The query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="parameters">Positional parameters, referenced as <c>$1</c>, <c>$2</c>…</param>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RowsAsync(
        string commandText,
        CancellationToken cancellationToken,
        params object?[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = Build(connection, commandText, parameters);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.Default, cancellationToken);

        var rows = new List<IReadOnlyDictionary<string, object?>>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);

            for (var column = 0; column < reader.FieldCount; column++)
            {
                row[reader.GetName(column)] = await reader.IsDBNullAsync(column, cancellationToken)
                    ? null
                    : reader.GetValue(column);
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>Executes a statement, and answers how many rows it touched.</summary>
    /// <param name="commandText">The statement.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="parameters">Positional parameters, referenced as <c>$1</c>, <c>$2</c>…</param>
    public async Task<int> ExecuteAsync(
        string commandText,
        CancellationToken cancellationToken,
        params object?[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = Build(connection, commandText, parameters);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Runs a statement and answers the <c>SQLSTATE</c> it was refused with, or null if it succeeded.
    /// </summary>
    /// <remarks>
    /// The shape the constraint tests want. <c>23514</c> is a <c>CHECK</c>, <c>23505</c> a unique
    /// violation, <c>23503</c> a foreign key — asserting on the code rather than the message means
    /// the test still passes when somebody improves the wording of a constraint name.
    /// </remarks>
    /// <param name="commandText">The statement.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="parameters">Positional parameters, referenced as <c>$1</c>, <c>$2</c>…</param>
    public async Task<string?> RefusalAsync(
        string commandText,
        CancellationToken cancellationToken,
        params object?[] parameters)
    {
        try
        {
            await ExecuteAsync(commandText, cancellationToken, parameters);
            return null;
        }
        catch (PostgresException exception)
        {
            return exception.SqlState;
        }
    }

    /// <summary>
    /// Runs a setup statement and then a query on the <em>same</em> connection, and answers the
    /// query's rows.
    /// </summary>
    /// <remarks>
    /// For the one thing a fresh connection per call cannot do: a planner setting is per session,
    /// so proving that an index <em>can</em> serve a query means turning sequential scans off and
    /// running <c>EXPLAIN</c> without the connection changing underneath. On a table with a handful
    /// of rows Postgres will always prefer a sequential scan, and a test that asserted otherwise
    /// would be asserting the size of the fixture rather than the existence of the index.
    /// </remarks>
    /// <param name="setup">The session statement — <c>SET enable_seqscan = off</c>.</param>
    /// <param name="query">The query to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="parameters">Positional parameters for the query, referenced as <c>$1</c>, <c>$2</c>…</param>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RowsAfterAsync(
        string setup,
        string query,
        CancellationToken cancellationToken,
        params object?[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var prepare = new NpgsqlCommand(setup, connection))
        {
            await prepare.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = Build(connection, query, parameters);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.Default, cancellationToken);

        var rows = new List<IReadOnlyDictionary<string, object?>>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);

            for (var column = 0; column < reader.FieldCount; column++)
            {
                row[reader.GetName(column)] = await reader.IsDBNullAsync(column, cancellationToken)
                    ? null
                    : reader.GetValue(column);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static NpgsqlCommand Build(NpgsqlConnection connection, string commandText, object?[] parameters)
    {
        var command = new NpgsqlCommand(commandText, connection);

        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = parameter ?? DBNull.Value });
        }

        return command;
    }
}
