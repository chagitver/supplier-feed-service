using Dapper;
using Microsoft.Data.Sqlite;
using SupplierFeedService.Api.Contracts;

namespace SupplierFeedService.Api.Data;

/// <summary>
/// Shared SupplierStats increment SQL, always executed on the caller's already-open
/// transaction so it stays atomic with the write it accompanies (see ReservationRepository
/// and RateLimitLogRepository) - deliberately not exposed via ISupplierStatsRepository, which
/// would need its own connection/transaction and break that atomicity.
/// </summary>
internal static class SupplierStatsSql
{
    public static Task IncrementIngestedAsync(SqliteConnection connection, string supplierId, IngestOutcome outcome) =>
        connection.ExecuteAsync(
            """
            INSERT INTO SupplierStats
                (SupplierId, IngestedCount, CreatedCount, UpdatedCount, UnchangedDuplicateCount, IgnoredStaleCount)
            VALUES (@SupplierId, 1, @IsCreated, @IsUpdated, @IsUnchanged, @IsStale)
            ON CONFLICT(SupplierId) DO UPDATE SET
                IngestedCount = IngestedCount + 1,
                CreatedCount = CreatedCount + @IsCreated,
                UpdatedCount = UpdatedCount + @IsUpdated,
                UnchangedDuplicateCount = UnchangedDuplicateCount + @IsUnchanged,
                IgnoredStaleCount = IgnoredStaleCount + @IsStale;
            """,
            new
            {
                SupplierId = supplierId,
                IsCreated = outcome == IngestOutcome.Created ? 1 : 0,
                IsUpdated = outcome == IngestOutcome.Updated ? 1 : 0,
                IsUnchanged = outcome == IngestOutcome.UnchangedDuplicate ? 1 : 0,
                IsStale = outcome == IngestOutcome.IgnoredStale ? 1 : 0,
            });

    public static Task IncrementThrottledAsync(SqliteConnection connection, string supplierId) =>
        connection.ExecuteAsync(
            """
            INSERT INTO SupplierStats (SupplierId, ThrottledCount) VALUES (@SupplierId, 1)
            ON CONFLICT(SupplierId) DO UPDATE SET ThrottledCount = ThrottledCount + 1;
            """,
            new { SupplierId = supplierId });
}
