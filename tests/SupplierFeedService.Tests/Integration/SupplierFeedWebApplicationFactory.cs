using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace SupplierFeedService.Tests.Integration;

/// <summary>
/// By default, one fresh temp SQLite file per class-fixture instance (xUnit creates one
/// instance per test class via <see cref="IClassFixture{TFixture}"/>, requiring a public
/// parameterless constructor), isolating parallel test classes from each other. Pass an
/// explicit <paramref name="sharedDbPath"/> to point two independently-constructed factories
/// at the same file, simulating two real server instances sharing one disk - in that case the
/// caller owns the file's lifecycle, not this class.
/// </summary>
public sealed class SupplierFeedWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath;
    private readonly bool _ownsDbFile;

    public SupplierFeedWebApplicationFactory()
        : this(null)
    {
    }

    internal SupplierFeedWebApplicationFactory(string? sharedDbPath)
    {
        _ownsDbFile = sharedDbPath is null;
        _dbPath = sharedDbPath ?? Path.Combine(Path.GetTempPath(), $"supplier-feed-tests-{Guid.NewGuid():N}.db");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SupplierFeedDb"] = $"Data Source={_dbPath}",
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing || !_ownsDbFile)
        {
            return;
        }

        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = _dbPath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
