using Dapper;

namespace SupplierFeedService.Api.Data;

public static class DatabaseInitializer
{
    /// <summary>
    /// All DDL is idempotent ("IF NOT EXISTS"), so multiple instances racing to run this at
    /// boot against the same shared file is safe - retried the same way as every other write
    /// in the codebase if startup contention exceeds busy_timeout.
    /// </summary>
    public static Task InitializeAsync(ISqliteConnectionFactory connectionFactory) =>
        connectionFactory.RunInWriteTransactionAsync(async connection =>
        {
            await connection.ExecuteAsync(SchemaSql.CreateAll);
            return true;
        });
}
