using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace SupplierFeedService.Api.Data;

public sealed class SqliteConnectionFactory : ISqliteConnectionFactory
{
    private static readonly int[] RetryBackoffMs = { 20, 80, 200 };

    private readonly string _connectionString;

    /// <summary>Takes an already-resolved connection string directly, bypassing
    /// IConfiguration/IHostEnvironment-based path resolution - used by tests that manage
    /// their own temp database file path.</summary>
    public SqliteConnectionFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public SqliteConnectionFactory(IConfiguration configuration, IHostEnvironment environment)
    {
        var configuredConnectionString = configuration.GetConnectionString("SupplierFeedDb")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:SupplierFeedDb configuration.");

        var connectionStringBuilder = new SqliteConnectionStringBuilder(configuredConnectionString);
        if (!Path.IsPathRooted(connectionStringBuilder.DataSource))
        {
            // All instances must share the literal same file, resolved relative to this
            // project's own content root - not the process's current working directory,
            // which can vary depending on how each instance was launched.
            connectionStringBuilder.DataSource = Path.Combine(environment.ContentRootPath, connectionStringBuilder.DataSource);
        }

        _connectionString = connectionStringBuilder.ConnectionString;
    }

    public SqliteConnection CreateOpen()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var pragmaCommand = connection.CreateCommand();
        pragmaCommand.CommandText =
            "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
        pragmaCommand.ExecuteNonQuery();

        return connection;
    }

    public async Task<T> RunInWriteTransactionAsync<T>(Func<SqliteConnection, Task<T>> action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var connection = CreateOpen();
                return await RunOnceAsync(connection, action);
            }
            catch (SqliteException ex) when (IsRetryable(ex) && attempt < RetryBackoffMs.Length)
            {
                var delay = RetryBackoffMs[attempt] + Random.Shared.Next(0, 20);
                await Task.Delay(delay);
            }
        }
    }

    private static async Task<T> RunOnceAsync<T>(SqliteConnection connection, Func<SqliteConnection, Task<T>> action)
    {
        // Microsoft.Data.Sqlite's BeginTransaction() issues a deferred BEGIN, which only
        // acquires the write lock lazily on first write - two connections could both pass an
        // initial SELECT believing they'll be the writer, then race on upgrade. Raw "BEGIN
        // IMMEDIATE" grabs the RESERVED lock up front, serializing the whole read-decide-write
        // sequence across every instance sharing this database file.
        await connection.ExecuteAsync("BEGIN IMMEDIATE;");
        try
        {
            var result = await action(connection);
            await connection.ExecuteAsync("COMMIT;");
            return result;
        }
        catch
        {
            try
            {
                await connection.ExecuteAsync("ROLLBACK;");
            }
            catch
            {
                // Best-effort: the connection is being torn down either way, and a secondary
                // failure here must not replace/mask the original exception below.
            }

            throw;
        }
    }

    private static bool IsRetryable(SqliteException ex) =>
        ex.SqliteErrorCode is 5 /* SQLITE_BUSY */ or 6 /* SQLITE_LOCKED */;
}
