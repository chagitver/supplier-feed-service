using Dapper;

namespace SupplierFeedService.Api.Data;

public sealed class SupplierStatsRepository : ISupplierStatsRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SupplierStatsRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<SupplierStatsSnapshot> GetAsync(string supplierId)
    {
        using var connection = _connectionFactory.CreateOpen();
        var row = await connection.QuerySingleOrDefaultAsync<SupplierStatsSnapshot>(
            """
            SELECT IngestedCount, ThrottledCount, CreatedCount, UpdatedCount,
                   UnchangedDuplicateCount, IgnoredStaleCount
            FROM SupplierStats
            WHERE SupplierId = @SupplierId;
            """,
            new { SupplierId = supplierId });

        return row ?? SupplierStatsSnapshot.Empty;
    }
}
