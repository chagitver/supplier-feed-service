using System.Net;
using System.Net.Http.Json;
using SupplierFeedService.Api.Contracts;
using static SupplierFeedService.Tests.Integration.ReservationPayloads;

namespace SupplierFeedService.Tests.Integration;

public sealed class IngestEndpointTests : IClassFixture<SupplierFeedWebApplicationFactory>
{
    private readonly HttpClient _client;

    public IngestEndpointTests(SupplierFeedWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task New_reservation_returns_201_created()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/reservations/ingest", Build(NewSupplierId(), "res-1"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestReservationResponse>(JsonOptions);
        Assert.Equal(IngestOutcome.Created, body!.Outcome);
    }

    [Fact]
    public async Task Identical_resend_returns_200_unchanged_duplicate()
    {
        var supplierId = NewSupplierId();
        var payload = Build(supplierId, "res-1");
        await _client.PostAsJsonAsync("/api/reservations/ingest", payload);

        var response = await _client.PostAsJsonAsync("/api/reservations/ingest", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestReservationResponse>(JsonOptions);
        Assert.Equal(IngestOutcome.UnchangedDuplicate, body!.Outcome);
    }

    [Fact]
    public async Task Changed_field_with_newer_timestamp_returns_200_updated()
    {
        var supplierId = NewSupplierId();
        await _client.PostAsJsonAsync(
            "/api/reservations/ingest",
            Build(supplierId, "res-1", price: 450.00m, updatedAtUtc: "2026-07-01T10:15:00Z"));

        var response = await _client.PostAsJsonAsync(
            "/api/reservations/ingest",
            Build(supplierId, "res-1", price: 500.00m, updatedAtUtc: "2026-07-01T11:15:00Z"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestReservationResponse>(JsonOptions);
        Assert.Equal(IngestOutcome.Updated, body!.Outcome);
    }

    [Fact]
    public async Task Changed_field_with_older_timestamp_returns_409_ignored_stale()
    {
        var supplierId = NewSupplierId();
        await _client.PostAsJsonAsync(
            "/api/reservations/ingest",
            Build(supplierId, "res-1", price: 450.00m, updatedAtUtc: "2026-07-01T10:15:00Z"));

        var response = await _client.PostAsJsonAsync(
            "/api/reservations/ingest",
            Build(supplierId, "res-1", price: 999.00m, updatedAtUtc: "2026-07-01T09:00:00Z"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestReservationResponse>(JsonOptions);
        Assert.Equal(IngestOutcome.IgnoredStale, body!.Outcome);
    }

    [Fact]
    public async Task Same_reservation_id_from_different_suppliers_are_independent_rows()
    {
        var supplierA = NewSupplierId();
        var supplierB = NewSupplierId();

        var responseA = await _client.PostAsJsonAsync(
            "/api/reservations/ingest", Build(supplierA, "res-1", price: 100.00m));
        var responseB = await _client.PostAsJsonAsync(
            "/api/reservations/ingest", Build(supplierB, "res-1", price: 200.00m));

        // Both are 201 Created, not 200 Updated/UnchangedDuplicate against each other's row -
        // proves the natural key is (SupplierId, ReservationId), not ReservationId alone.
        Assert.Equal(HttpStatusCode.Created, responseA.StatusCode);
        Assert.Equal(HttpStatusCode.Created, responseB.StatusCode);

        var followUpA = await _client.PostAsJsonAsync(
            "/api/reservations/ingest", Build(supplierA, "res-1", price: 100.00m));
        Assert.Equal(HttpStatusCode.OK, followUpA.StatusCode);
        var followUpBody = await followUpA.Content.ReadFromJsonAsync<IngestReservationResponse>(JsonOptions);
        Assert.Equal(IngestOutcome.UnchangedDuplicate, followUpBody!.Outcome);
    }

    [Fact]
    public async Task The_101st_request_in_60_seconds_returns_429_with_retry_after()
    {
        var supplierId = NewSupplierId();

        for (var i = 0; i < 100; i++)
        {
            var ok = await _client.PostAsJsonAsync("/api/reservations/ingest", Build(supplierId, $"res-{i}"));
            Assert.NotEqual(HttpStatusCode.TooManyRequests, ok.StatusCode);
        }

        var response = await _client.PostAsJsonAsync("/api/reservations/ingest", Build(supplierId, "res-101"));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Retry-After", out _));
    }

    [Fact]
    public async Task Malformed_request_returns_400()
    {
        var payload = new
        {
            supplierId = string.Empty,
            reservationId = "res-1",
            roomId = "room-1",
            checkIn = "2026-08-01T00:00:00Z",
            checkOut = "2026-08-03T00:00:00Z",
            price = 100.00m,
            updatedAtUtc = "2026-07-01T10:15:00Z",
        };

        var response = await _client.PostAsJsonAsync("/api/reservations/ingest", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
