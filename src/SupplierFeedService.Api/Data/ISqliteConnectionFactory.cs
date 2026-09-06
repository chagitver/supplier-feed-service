using Microsoft.Data.Sqlite;

namespace SupplierFeedService.Api.Data;

/// <summary>
/// The only seam in the codebase that knows about SQLite specifics (pragmas, connection
/// string, BEGIN IMMEDIATE transaction semantics, busy/locked retry). Everything above this
/// layer depends only on this interface, so swapping to a client-server DB later is a
/// contained change, not a rewrite.
/// </summary>
public interface ISqliteConnectionFactory
{
    SqliteConnection CreateOpen();

    /// <summary>
    /// Runs <paramref name="action"/> inside a BEGIN IMMEDIATE transaction on a fresh
    /// connection, retrying the whole attempt a few times on SQLITE_BUSY/SQLITE_LOCKED.
    /// </summary>
    Task<T> RunInWriteTransactionAsync<T>(Func<SqliteConnection, Task<T>> action);
}
