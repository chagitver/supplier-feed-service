using Dapper;

namespace SupplierFeedService.Api.Data;

public sealed class RateLimitLogRepository : IRateLimitLogRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public RateLimitLogRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<bool> TryRecordAttemptAsync(string supplierId, long nowUtcMs, int maxRequestsPerWindow, int windowSeconds)
    {
        var windowMs = windowSeconds * 1000L;
        var pruneRetentionMs = windowMs * 2; // clock-skew safety margin

        return _connectionFactory.RunInWriteTransactionAsync(async conn =>
        {
            // Opportunistic, unscoped pruning riding along on the write lock this
            // transaction already holds - an internal implementation detail of this
            // repository, not a separate service. Relies on IX_SupplierRequestLog_Time
            // (RequestAtUtcMs alone) so this doesn't degrade into a full table scan.
            await conn.ExecuteAsync(
                "DELETE FROM SupplierRequestLog WHERE RequestAtUtcMs < @CutoffMs;",
                new { CutoffMs = nowUtcMs - pruneRetentionMs });

            await conn.ExecuteAsync(
                "INSERT INTO SupplierRequestLog (SupplierId, RequestAtUtcMs) VALUES (@SupplierId, @NowUtcMs);",
                new { SupplierId = supplierId, NowUtcMs = nowUtcMs });

            var count = await conn.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM SupplierRequestLog
                WHERE SupplierId = @SupplierId AND RequestAtUtcMs > @WindowStartMs;
                """,
                new { SupplierId = supplierId, WindowStartMs = nowUtcMs - windowMs });

            var allowed = count <= maxRequestsPerWindow;
            if (!allowed)
            {
                await SupplierStatsSql.IncrementThrottledAsync(conn, supplierId);
            }

            return allowed;
        });
    }
}
