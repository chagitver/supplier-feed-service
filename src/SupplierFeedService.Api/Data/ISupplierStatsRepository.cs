namespace SupplierFeedService.Api.Data;

public sealed record SupplierStatsSnapshot(
    long IngestedCount,
    long ThrottledCount,
    long CreatedCount,
    long UpdatedCount,
    long UnchangedDuplicateCount,
    long IgnoredStaleCount)
{
    public static SupplierStatsSnapshot Empty { get; } = new(0, 0, 0, 0, 0, 0);
}

public interface ISupplierStatsRepository
{
    /// <summary>Returns the lifetime counters for a supplier, or all-zeros if never seen.</summary>
    Task<SupplierStatsSnapshot> GetAsync(string supplierId);
}
