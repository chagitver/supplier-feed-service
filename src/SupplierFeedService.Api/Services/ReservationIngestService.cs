using SupplierFeedService.Api.Contracts;
using SupplierFeedService.Api.Data;

namespace SupplierFeedService.Api.Services;

public sealed class ReservationIngestService : IReservationIngestService
{
    private readonly ISlidingWindowRateLimiter _rateLimiter;
    private readonly IReservationRepository _reservationRepository;
    private readonly TimeProvider _timeProvider;

    public ReservationIngestService(
        ISlidingWindowRateLimiter rateLimiter,
        IReservationRepository reservationRepository,
        TimeProvider timeProvider)
    {
        _rateLimiter = rateLimiter;
        _reservationRepository = reservationRepository;
        _timeProvider = timeProvider;
    }

    public async Task<ReservationIngestResult> IngestAsync(IngestReservationRequest request)
    {
        var allowed = await _rateLimiter.TryAcquireAsync(request.SupplierId);
        if (!allowed)
        {
            return new ReservationIngestResult.Throttled();
        }

        var nowUtcMs = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var upsertResult = await _reservationRepository.UpsertAsync(request, nowUtcMs);
        return new ReservationIngestResult.Processed(upsertResult.Outcome, upsertResult.StoredUpdatedAtUtcMs);
    }
}
