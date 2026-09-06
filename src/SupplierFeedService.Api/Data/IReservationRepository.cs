using SupplierFeedService.Api.Contracts;

namespace SupplierFeedService.Api.Data;

public sealed record ReservationUpsertResult(IngestOutcome Outcome, long StoredUpdatedAtUtcMs);

public interface IReservationRepository
{
    /// <summary>
    /// Atomically classifies and applies (or skips) the write for a reservation upsert, and
    /// increments the matching SupplierStats counters in the same transaction.
    /// </summary>
    Task<ReservationUpsertResult> UpsertAsync(IngestReservationRequest request, long nowUtcMs);
}
