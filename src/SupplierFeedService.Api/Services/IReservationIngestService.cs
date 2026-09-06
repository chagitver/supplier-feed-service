using SupplierFeedService.Api.Contracts;

namespace SupplierFeedService.Api.Services;

public abstract record ReservationIngestResult
{
    private ReservationIngestResult()
    {
    }

    public sealed record Processed(IngestOutcome Outcome, long StoredUpdatedAtUtcMs) : ReservationIngestResult;

    public sealed record Throttled : ReservationIngestResult;
}

public interface IReservationIngestService
{
    Task<ReservationIngestResult> IngestAsync(IngestReservationRequest request);
}
