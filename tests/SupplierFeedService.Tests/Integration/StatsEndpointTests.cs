using System.Net;
using System.Net.Http.Json;
using SupplierFeedService.Api.Contracts;
using static SupplierFeedService.Tests.Integration.ReservationPayloads;

namespace SupplierFeedService.Tests.Integration;

public sealed class StatsEndpointTests : IClassFixture<SupplierFeedWebApplicationFactory>
{
    private readonly HttpClient _client;

    public StatsEndpointTests(SupplierFeedWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<SupplierStatsResponse> GetStatsAsync(string supplierId)
    {
        var response = await _client.GetAsync($"/api/reservations/stats/{supplierId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SupplierStatsResponse>(JsonOptions))!;
    }

    [Fact]
    public async Task Unseen_supplier_returns_all_zero_counters()
    {
        var stats = await GetStatsAsync(NewSupplierId());

        Assert.Equal(0, stats.IngestedCount);
        Assert.Equal(0, stats.ThrottledCount);
        Assert.Equal(0, stats.CreatedCount);
        Assert.Equal(0, stats.UpdatedCount);
        Assert.Equal(0, stats.UnchangedDuplicateCount);
        Assert.Equal(0, stats.IgnoredStaleCount);
    }

    [Fact]
    public async Task Ingested_count_covers_created_updated_unchanged_and_ignored_stale()
    {
        var supplierId = NewSupplierId();

        // Created
        await _client.PostAsJsonAsync(
            "/api/reservations/ingest",
            Build(supplierId, "res-1", price: 450.00m, updatedAtUtc: "2026-07-01T10:15:00Z"));

        // Updated
        await _client.PostAsJsonAsync(
            "/api/reservations/ingest",
            Build(supplierId, "res-1", price: 500.00m, updatedAtUtc: "2026-07-01T11:15:00Z"));

        // UnchangedDuplicate (resend of the now-current state)
        await _client.PostAsJsonAsync(
            "/api/reservations/ingest",
            Build(supplierId, "res-1", price: 500.00m, updatedAtUtc: "2026-07-01T11:15:00Z"));

        // IgnoredStale
        await _client.PostAsJsonAsync(
            "/api/reservations/ingest",
            Build(supplierId, "res-1", price: 999.00m, updatedAtUtc: "2026-07-01T09:00:00Z"));

        var stats = await GetStatsAsync(supplierId);

        Assert.Equal(4, stats.IngestedCount);
        Assert.Equal(1, stats.CreatedCount);
        Assert.Equal(1, stats.UpdatedCount);
        Assert.Equal(1, stats.UnchangedDuplicateCount);
        Assert.Equal(1, stats.IgnoredStaleCount);
        Assert.Equal(0, stats.ThrottledCount);
    }

    [Fact]
    public async Task Throttled_requests_are_reflected_in_stats()
    {
        var supplierId = NewSupplierId();

        for (var i = 0; i < 100; i++)
        {
            await _client.PostAsJsonAsync("/api/reservations/ingest", Build(supplierId, $"res-{i}"));
        }

        await _client.PostAsJsonAsync("/api/reservations/ingest", Build(supplierId, "res-over-1"));
        await _client.PostAsJsonAsync("/api/reservations/ingest", Build(supplierId, "res-over-2"));

        var stats = await GetStatsAsync(supplierId);

        Assert.Equal(100, stats.IngestedCount);
        Assert.Equal(2, stats.ThrottledCount);
    }

    [Fact]
    public async Task Malformed_request_does_not_touch_stats_counters()
    {
        var supplierId = NewSupplierId();
        var payload = new
        {
            supplierId,
            reservationId = string.Empty,
            roomId = "room-1",
            checkIn = "2026-08-01T00:00:00Z",
            checkOut = "2026-08-03T00:00:00Z",
            price = 100.00m,
            updatedAtUtc = "2026-07-01T10:15:00Z",
        };

        var response = await _client.PostAsJsonAsync("/api/reservations/ingest", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var stats = await GetStatsAsync(supplierId);

        Assert.Equal(0, stats.IngestedCount);
        Assert.Equal(0, stats.ThrottledCount);
    }
}
