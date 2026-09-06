namespace SupplierFeedService.Api.Data;

public interface IRateLimitLogRepository
{
    /// <summary>
    /// Atomically prunes stale log rows, records this attempt, and counts attempts for the
    /// supplier within the trailing window (every attempt counts, allowed or not, so the
    /// window clears naturally as old attempts age out). If the count exceeds the limit,
    /// increments SupplierStats.ThrottledCount in the same transaction and returns false.
    /// </summary>
    Task<bool> TryRecordAttemptAsync(string supplierId, long nowUtcMs, int maxRequestsPerWindow, int windowSeconds);
}
