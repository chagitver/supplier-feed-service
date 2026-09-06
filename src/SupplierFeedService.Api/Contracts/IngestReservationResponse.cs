namespace SupplierFeedService.Api.Contracts;

public sealed record IngestReservationResponse(
    string SupplierId,
    string ReservationId,
    IngestOutcome Outcome,
    DateTimeOffset StoredUpdatedAtUtc,
    DateTimeOffset IncomingUpdatedAtUtc,
    string Message);
