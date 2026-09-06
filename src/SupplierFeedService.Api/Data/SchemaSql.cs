namespace SupplierFeedService.Api.Data;

internal static class SchemaSql
{
    // Timestamps are stored as INTEGER Unix epoch milliseconds - SQLite has no native datetime
    // type, and this makes range comparisons plain integer math instead of string comparison.
    public const string CreateAll = """
        CREATE TABLE IF NOT EXISTS Reservations (
            SupplierId      TEXT    NOT NULL,
            ReservationId   TEXT    NOT NULL,
            RoomId          TEXT    NOT NULL,
            CheckInUtcMs    INTEGER NOT NULL,
            CheckOutUtcMs   INTEGER NOT NULL,
            Price           TEXT    NOT NULL,
            UpdatedAtUtcMs  INTEGER NOT NULL,
            CreatedAtUtcMs  INTEGER NOT NULL,
            LastWriteUtcMs  INTEGER NOT NULL,
            PRIMARY KEY (SupplierId, ReservationId)
        );

        CREATE TABLE IF NOT EXISTS SupplierRequestLog (
            Id             INTEGER PRIMARY KEY AUTOINCREMENT,
            SupplierId     TEXT    NOT NULL,
            RequestAtUtcMs INTEGER NOT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_SupplierRequestLog_Supplier_Time
            ON SupplierRequestLog (SupplierId, RequestAtUtcMs);
        CREATE INDEX IF NOT EXISTS IX_SupplierRequestLog_Time
            ON SupplierRequestLog (RequestAtUtcMs);

        CREATE TABLE IF NOT EXISTS SupplierStats (
            SupplierId              TEXT PRIMARY KEY,
            IngestedCount           INTEGER NOT NULL DEFAULT 0,
            ThrottledCount          INTEGER NOT NULL DEFAULT 0,
            CreatedCount            INTEGER NOT NULL DEFAULT 0,
            UpdatedCount            INTEGER NOT NULL DEFAULT 0,
            UnchangedDuplicateCount INTEGER NOT NULL DEFAULT 0,
            IgnoredStaleCount       INTEGER NOT NULL DEFAULT 0
        );
        """;
}
