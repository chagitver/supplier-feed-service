using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupplierFeedService.Tests.Integration;

internal static class ReservationPayloads
{
    public static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static object Build(
        string supplierId,
        string reservationId,
        string roomId = "room-1",
        decimal price = 450.00m,
        string updatedAtUtc = "2026-07-01T10:15:00Z") => new
        {
            supplierId,
            reservationId,
            roomId,
            checkIn = "2026-08-01T00:00:00Z",
            checkOut = "2026-08-03T00:00:00Z",
            price,
            updatedAtUtc,
        };

    public static string NewSupplierId() => $"supplier-{Guid.NewGuid():N}";

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
