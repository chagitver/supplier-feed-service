using Microsoft.Extensions.Options;
using SupplierFeedService.Api.Data;
using SupplierFeedService.Api.Options;

namespace SupplierFeedService.Api.Services;

public sealed class SlidingWindowRateLimiter : ISlidingWindowRateLimiter
{
    private readonly IRateLimitLogRepository _repository;
    private readonly SupplierFeedOptions _options;
    private readonly TimeProvider _timeProvider;

    public SlidingWindowRateLimiter(
        IRateLimitLogRepository repository,
        IOptions<SupplierFeedOptions> options,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public Task<bool> TryAcquireAsync(string supplierId)
    {
        var nowUtcMs = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        return _repository.TryRecordAttemptAsync(supplierId, nowUtcMs, _options.MaxRequestsPerWindow, _options.WindowSeconds);
    }
}
