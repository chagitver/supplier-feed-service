using Microsoft.Extensions.Options;
using SupplierFeedService.Api.Data;
using SupplierFeedService.Api.Options;

namespace SupplierFeedService.Api.Services;

public sealed class SlidingWindowRateLimiter : ISlidingWindowRateLimiter
{
    private readonly IRateLimitLogRepository _repository;
    private readonly SupplierFeedOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SlidingWindowRateLimiter> _logger;

    public SlidingWindowRateLimiter(
        IRateLimitLogRepository repository,
        IOptions<SupplierFeedOptions> options,
        TimeProvider timeProvider,
        ILogger<SlidingWindowRateLimiter> logger)
    {
        _repository = repository;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<bool> TryAcquireAsync(string supplierId)
    {
        var nowUtcMs = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var allowed = await _repository.TryRecordAttemptAsync(
            supplierId, nowUtcMs, _options.MaxRequestsPerWindow, _options.WindowSeconds);

        if (!allowed)
        {
            _logger.LogWarning(
                "Supplier {SupplierId} throttled: exceeded {MaxRequestsPerWindow} requests in the last {WindowSeconds}s.",
                supplierId, _options.MaxRequestsPerWindow, _options.WindowSeconds);
        }

        return allowed;
    }
}
