using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using static SupplierFeedService.Tests.Integration.ReservationPayloads;

namespace SupplierFeedService.Tests.Integration;

/// <summary>
/// Reproduces the real incident that motivated GlobalExceptionHandler: the app starts up fine
/// (schema init succeeds), but the underlying file disappears later - each repository call
/// opens a fresh connection, so the next request after the file vanishes hits a genuine
/// unhandled SqliteException ("no such table"), not a startup failure.
/// </summary>
public sealed class ExceptionHandlingTests : IAsyncLifetime
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"supplier-feed-exception-tests-{Guid.NewGuid():N}.db");

    private SupplierFeedWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new SupplierFeedWebApplicationFactory(_dbPath);
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
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
    public async Task Unhandled_exception_returns_clean_500_problem_details_without_leaking_internals()
    {
        // Sanity check: the app is healthy before we pull the rug out.
        var healthy = await _client.PostAsJsonAsync("/api/reservations/ingest", Build(NewSupplierId(), "before"));
        Assert.NotEqual(HttpStatusCode.InternalServerError, healthy.StatusCode);

        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);

        var response = await _client.PostAsJsonAsync("/api/reservations/ingest", Build(NewSupplierId(), "after"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SqliteException", body);
        Assert.DoesNotContain("StackTrace", body);
        Assert.DoesNotContain("at Microsoft.Data.Sqlite", body);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(ReservationPayloads.JsonOptions);
        Assert.Equal(500, problem!.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
    }
}
