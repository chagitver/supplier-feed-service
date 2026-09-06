using System.Net;
using System.Net.Http.Json;
using static SupplierFeedService.Tests.Integration.ReservationPayloads;

namespace SupplierFeedService.Tests.Integration;

/// <summary>
/// Regression coverage for IngestReservationRequest's IValidatableObject checks - these guard
/// against a real bug that shipped once: [Required] does not catch a missing value for
/// non-nullable value types (DateTimeOffset/decimal), so the model binder silently fills in
/// default(DateTimeOffset)/0 instead of failing validation unless IValidatableObject
/// explicitly checks for it.
/// </summary>
public sealed class ValidationTests : IClassFixture<SupplierFeedWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ValidationTests(SupplierFeedWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Missing_checkIn_returns_400()
    {
        var payload = new
        {
            supplierId = NewSupplierId(),
            reservationId = "r1",
            roomId = "room1",
            checkOut = "2026-08-03T00:00:00Z",
            price = 100.00m,
            updatedAtUtc = "2026-07-01T10:15:00Z",
        };

        var response = await _client.PostAsJsonAsync("/api/reservations/ingest", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Missing_checkOut_returns_400()
    {
        var payload = new
        {
            supplierId = NewSupplierId(),
            reservationId = "r1",
            roomId = "room1",
            checkIn = "2026-08-01T00:00:00Z",
            price = 100.00m,
            updatedAtUtc = "2026-07-01T10:15:00Z",
        };

        var response = await _client.PostAsJsonAsync("/api/reservations/ingest", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Missing_updatedAtUtc_returns_400()
    {
        var payload = new
        {
            supplierId = NewSupplierId(),
            reservationId = "r1",
            roomId = "room1",
            checkIn = "2026-08-01T00:00:00Z",
            checkOut = "2026-08-03T00:00:00Z",
            price = 100.00m,
        };

        var response = await _client.PostAsJsonAsync("/api/reservations/ingest", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Zero_price_returns_400()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/reservations/ingest", Build(NewSupplierId(), "r1", price: 0m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Negative_price_returns_400()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/reservations/ingest", Build(NewSupplierId(), "r1", price: -50.00m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SupplierId_containing_slash_returns_400()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/reservations/ingest", Build("acme/east", "r1"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Valid_request_still_succeeds()
    {
        // Guards against an overly-aggressive validation fix silently rejecting good input.
        var response = await _client.PostAsJsonAsync(
            "/api/reservations/ingest", Build(NewSupplierId(), "r1"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
