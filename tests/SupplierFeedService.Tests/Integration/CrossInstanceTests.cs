using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using SupplierFeedService.Api.Contracts;
using static SupplierFeedService.Tests.Integration.ReservationPayloads;

namespace SupplierFeedService.Tests.Integration;

/// <summary>
/// Two independently-constructed <see cref="SupplierFeedWebApplicationFactory"/> instances
/// (separate DI containers, separate connection pools) sharing one SQLite file - codifying
/// the cross-instance correctness that was previously only verified manually by running two
/// real `dotnet run` processes against the same file and hitting them with curl.
/// </summary>
public sealed class CrossInstanceTests : IAsyncLifetime
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"supplier-feed-cross-instance-{Guid.NewGuid():N}.db");

    private SupplierFeedWebApplicationFactory _instance1 = null!;
    private SupplierFeedWebApplicationFactory _instance2 = null!;
    private HttpClient _client1 = null!;
    private HttpClient _client2 = null!;

    public Task InitializeAsync()
    {
        _instance1 = new SupplierFeedWebApplicationFactory(_dbPath);
        _instance2 = new SupplierFeedWebApplicationFactory(_dbPath);
        _client1 = _instance1.CreateClient();
        _client2 = _instance2.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _instance1.Dispose();
        _instance2.Dispose();

        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = _dbPath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Rate_limit_is_global_across_both_instances_not_100_per_instance()
    {
        var supplierId = NewSupplierId();

        var tasks = Enumerable.Range(0, 110)
            .Select(i =>
            {
                var client = i % 2 == 0 ? _client1 : _client2;
                return client.PostAsJsonAsync("/api/reservations/ingest", Build(supplierId, $"res-{i}"));
            })
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(100, responses.Count(r => r.StatusCode != HttpStatusCode.TooManyRequests));
        Assert.Equal(10, responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));

        var statsFrom1 = await GetStatsAsync(_client1, supplierId);
        var statsFrom2 = await GetStatsAsync(_client2, supplierId);

        Assert.Equal(statsFrom1, statsFrom2);
        Assert.Equal(100, statsFrom1.IngestedCount);
        Assert.Equal(10, statsFrom1.ThrottledCount);
    }

    [Fact]
    public async Task Concurrent_conflicting_writes_from_both_instances_converge_to_the_newer_timestamp()
    {
        var supplierId = NewSupplierId();

        var created = await _client1.PostAsJsonAsync(
            "/api/reservations/ingest",
            Build(supplierId, "shared-1", roomId: "roomA", price: 111.00m, updatedAtUtc: "2026-07-01T12:00:00Z"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var updateFromInstance1 = _client1.PostAsJsonAsync(
            "/api/reservations/ingest",
            Build(supplierId, "shared-1", roomId: "roomB", price: 222.00m, updatedAtUtc: "2026-07-01T13:00:00Z"));
        var updateFromInstance2 = _client2.PostAsJsonAsync(
            "/api/reservations/ingest",
            Build(supplierId, "shared-1", roomId: "roomC", price: 333.00m, updatedAtUtc: "2026-07-01T14:00:00Z"));

        var responses = await Task.WhenAll(updateFromInstance1, updateFromInstance2);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        // Whichever instance's write landed first, the durable state must reflect the newer
        // timestamp (14:00, roomC) - resubmitting it should read back as unchanged.
        var followUp = await _client1.PostAsJsonAsync(
            "/api/reservations/ingest",
            Build(supplierId, "shared-1", roomId: "roomC", price: 333.00m, updatedAtUtc: "2026-07-01T14:00:00Z"));

        var body = await followUp.Content.ReadFromJsonAsync<IngestReservationResponse>(JsonOptions);
        Assert.Equal(IngestOutcome.UnchangedDuplicate, body!.Outcome);
    }

    private static async Task<SupplierStatsResponse> GetStatsAsync(HttpClient client, string supplierId)
    {
        var response = await client.GetAsync($"/api/reservations/stats/{supplierId}");
        return (await response.Content.ReadFromJsonAsync<SupplierStatsResponse>(JsonOptions))!;
    }
}
