using Microsoft.Data.Sqlite;
using SupplierFeedService.Api.Data;

namespace SupplierFeedService.Tests.Fixtures;

/// <summary>Fresh temp SQLite file per test instance, schema-initialized, cleaned up after.</summary>
public sealed class TempSqliteFixture : IAsyncLifetime
{
    private string? _dbPath;

    public ISqliteConnectionFactory ConnectionFactory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"supplier-feed-tests-{Guid.NewGuid():N}.db");
        ConnectionFactory = new SqliteConnectionFactory($"Data Source={_dbPath}");
        await DatabaseInitializer.InitializeAsync(ConnectionFactory);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (_dbPath is not null)
        {
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                var path = _dbPath + suffix;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        return Task.CompletedTask;
    }
}
