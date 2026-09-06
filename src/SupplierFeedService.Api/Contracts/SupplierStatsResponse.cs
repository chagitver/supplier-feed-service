namespace SupplierFeedService.Api.Contracts;

public sealed record SupplierStatsResponse(
    string SupplierId,
    long IngestedCount,
    long ThrottledCount,
    long CreatedCount,
    long UpdatedCount,
    long UnchangedDuplicateCount,
    long IgnoredStaleCount);
