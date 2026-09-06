using System.Globalization;
using Dapper;
using SupplierFeedService.Api.Contracts;

namespace SupplierFeedService.Api.Data;

internal sealed record ReservationRow(
    string RoomId,
    long CheckInUtcMs,
    long CheckOutUtcMs,
    string Price,
    long UpdatedAtUtcMs);

public sealed class ReservationRepository : IReservationRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public ReservationRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<ReservationUpsertResult> UpsertAsync(IngestReservationRequest request, long nowUtcMs)
    {
        var checkInMs = request.CheckIn.ToUnixTimeMilliseconds();
        var checkOutMs = request.CheckOut.ToUnixTimeMilliseconds();
        var updatedAtMs = request.UpdatedAtUtc.ToUnixTimeMilliseconds();
        var priceText = request.Price.ToString(CultureInfo.InvariantCulture);

        return _connectionFactory.RunInWriteTransactionAsync(async conn =>
        {
            var existing = await conn.QuerySingleOrDefaultAsync<ReservationRow>(
                """
                SELECT RoomId, CheckInUtcMs, CheckOutUtcMs, Price, UpdatedAtUtcMs
                FROM Reservations
                WHERE SupplierId = @SupplierId AND ReservationId = @ReservationId;
                """,
                new { request.SupplierId, request.ReservationId });

            IngestOutcome outcome;
            long storedUpdatedAtUtcMs;

            if (existing is null)
            {
                await conn.ExecuteAsync(
                    """
                    INSERT INTO Reservations
                        (SupplierId, ReservationId, RoomId, CheckInUtcMs, CheckOutUtcMs, Price,
                         UpdatedAtUtcMs, CreatedAtUtcMs, LastWriteUtcMs)
                    VALUES (@SupplierId, @ReservationId, @RoomId, @CheckInMs, @CheckOutMs, @PriceText,
                            @UpdatedAtMs, @NowUtcMs, @NowUtcMs);
                    """,
                    new
                    {
                        request.SupplierId,
                        request.ReservationId,
                        request.RoomId,
                        CheckInMs = checkInMs,
                        CheckOutMs = checkOutMs,
                        PriceText = priceText,
                        UpdatedAtMs = updatedAtMs,
                        NowUtcMs = nowUtcMs,
                    });
                outcome = IngestOutcome.Created;
                storedUpdatedAtUtcMs = updatedAtMs;
            }
            else if (existing.RoomId == request.RoomId
                     && existing.CheckInUtcMs == checkInMs
                     && existing.CheckOutUtcMs == checkOutMs
                     && decimal.Parse(existing.Price, CultureInfo.InvariantCulture) == request.Price)
            {
                // Business fields identical - no write, regardless of updatedAtUtc.
                outcome = IngestOutcome.UnchangedDuplicate;
                storedUpdatedAtUtcMs = existing.UpdatedAtUtcMs;
            }
            else if (updatedAtMs >= existing.UpdatedAtUtcMs)
            {
                await conn.ExecuteAsync(
                    """
                    UPDATE Reservations
                    SET RoomId = @RoomId, CheckInUtcMs = @CheckInMs, CheckOutUtcMs = @CheckOutMs,
                        Price = @PriceText, UpdatedAtUtcMs = @UpdatedAtMs, LastWriteUtcMs = @NowUtcMs
                    WHERE SupplierId = @SupplierId AND ReservationId = @ReservationId;
                    """,
                    new
                    {
                        request.SupplierId,
                        request.ReservationId,
                        request.RoomId,
                        CheckInMs = checkInMs,
                        CheckOutMs = checkOutMs,
                        PriceText = priceText,
                        UpdatedAtMs = updatedAtMs,
                        NowUtcMs = nowUtcMs,
                    });
                outcome = IngestOutcome.Updated;
                storedUpdatedAtUtcMs = updatedAtMs;
            }
            else
            {
                // Incoming updatedAtUtc is older than what's stored - would regress the
                // record, so skip the write. Kept distinct from UnchangedDuplicate so it
                // stays observable (see SupplierStats.IgnoredStaleCount) rather than silently
                // dropped.
                outcome = IngestOutcome.IgnoredStale;
                storedUpdatedAtUtcMs = existing.UpdatedAtUtcMs;
            }

            await SupplierStatsSql.IncrementIngestedAsync(conn, request.SupplierId, outcome);

            return new ReservationUpsertResult(outcome, storedUpdatedAtUtcMs);
        });
    }
}
