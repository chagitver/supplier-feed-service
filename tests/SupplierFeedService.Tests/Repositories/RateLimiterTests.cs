using Dapper;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SupplierFeedService.Api.Data;
using SupplierFeedService.Api.Options;
using SupplierFeedService.Api.Services;
using SupplierFeedService.Tests.Fixtures;

namespace SupplierFeedService.Tests.Repositories;

public sealed class RateLimiterTests : IAsyncLifetime
{
    private readonly TempSqliteFixture _fixture = new();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    private ISlidingWindowRateLimiter _rateLimiter = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        var repository = new RateLimitLogRepository(_fixture.ConnectionFactory);
        var options = Options.Create(new SupplierFeedOptions { MaxRequestsPerWindow = 100, WindowSeconds = 60 });
        _rateLimiter = new SlidingWindowRateLimiter(repository, options, _timeProvider);
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Exactly_100_requests_are_all_allowed()
    {
        for (var i = 0; i < 100; i++)
        {
            Assert.True(await _rateLimiter.TryAcquireAsync("supplier-1"));
        }
    }

    [Fact]
    public async Task The_101st_request_is_throttled()
    {
        for (var i = 0; i < 100; i++)
        {
            await _rateLimiter.TryAcquireAsync("supplier-1");
        }

        Assert.False(await _rateLimiter.TryAcquireAsync("supplier-1"));
    }

    [Fact]
    public async Task Concurrent_burst_past_the_limit_allows_exactly_100()
    {
        // Genuinely concurrent (not sequential) requests racing to acquire the same
        // BEGIN IMMEDIATE write lock - exercises the TOCTOU race the design is meant to
        // close, unlike the sequential-loop tests above.
        var tasks = Enumerable.Range(0, 110)
            .Select(_ => _rateLimiter.TryAcquireAsync("supplier-1"))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.Equal(100, results.Count(allowed => allowed));
        Assert.Equal(10, results.Count(allowed => !allowed));
    }

    [Fact]
    public async Task Throttled_requests_are_recorded_in_stats()
    {
        for (var i = 0; i < 100; i++)
        {
            await _rateLimiter.TryAcquireAsync("supplier-1");
        }

        await _rateLimiter.TryAcquireAsync("supplier-1");
        await _rateLimiter.TryAcquireAsync("supplier-1");

        var statsRepository = new SupplierStatsRepository(_fixture.ConnectionFactory);
        var stats = await statsRepository.GetAsync("supplier-1");

        Assert.Equal(2, stats.ThrottledCount);
    }

    [Fact]
    public async Task Window_clears_after_60_seconds()
    {
        for (var i = 0; i < 100; i++)
        {
            await _rateLimiter.TryAcquireAsync("supplier-1");
        }

        Assert.False(await _rateLimiter.TryAcquireAsync("supplier-1"));

        _timeProvider.Advance(TimeSpan.FromSeconds(61));

        Assert.True(await _rateLimiter.TryAcquireAsync("supplier-1"));
    }

    [Fact]
    public async Task Different_suppliers_do_not_interfere_with_each_other()
    {
        for (var i = 0; i < 100; i++)
        {
            Assert.True(await _rateLimiter.TryAcquireAsync("supplier-A"));
            Assert.True(await _rateLimiter.TryAcquireAsync("supplier-B"));
        }

        Assert.False(await _rateLimiter.TryAcquireAsync("supplier-A"));
        Assert.False(await _rateLimiter.TryAcquireAsync("supplier-B"));
    }

    [Fact]
    public async Task Pruning_removes_only_rows_older_than_retention_cutoff()
    {
        var nowMs = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var staleMs = nowMs - 130_000; // older than the 120s retention cutoff -> pruned
        var freshMs = nowMs - 30_000; // within retention -> kept

        using (var connection = _fixture.ConnectionFactory.CreateOpen())
        {
            await connection.ExecuteAsync(
                "INSERT INTO SupplierRequestLog (SupplierId, RequestAtUtcMs) VALUES (@SupplierId, @Ms);",
                new[]
                {
                    new { SupplierId = "supplier-1", Ms = staleMs },
                    new { SupplierId = "supplier-1", Ms = freshMs },
                });
        }

        await _rateLimiter.TryAcquireAsync("supplier-1"); // triggers pruning as part of its own transaction

        using var checkConnection = _fixture.ConnectionFactory.CreateOpen();
        var remaining = (await checkConnection.QueryAsync<long>(
            "SELECT RequestAtUtcMs FROM SupplierRequestLog WHERE SupplierId = @SupplierId;",
            new { SupplierId = "supplier-1" })).ToList();

        Assert.DoesNotContain(staleMs, remaining);
        Assert.Contains(freshMs, remaining);
    }
}
