namespace SupplierFeedService.Api.Services;

public interface ISlidingWindowRateLimiter
{
    /// <summary>Returns false if this supplier has exceeded its rolling-window request budget.</summary>
    Task<bool> TryAcquireAsync(string supplierId);
}
