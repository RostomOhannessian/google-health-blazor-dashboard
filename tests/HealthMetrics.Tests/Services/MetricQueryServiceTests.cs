using HealthMetrics.Application.Models;
using HealthMetrics.Infrastructure.Persistence;
using HealthMetrics.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HealthMetrics.Tests.Services;

public sealed class MetricQueryServiceTests
{
    [Fact]
    public async Task RecentSyncHistory_UsesSqliteTranslationAndReturnsNewestLocalRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDbContext(connection);
        await db.Database.EnsureCreatedAsync();

        db.SyncHistory.AddRange(
            new SyncHistoryEntry { UserKey = LocalUser.Key, RequestedDays = 7 },
            new SyncHistoryEntry { UserKey = "another-user", RequestedDays = 14 },
            new SyncHistoryEntry { UserKey = LocalUser.Key, RequestedDays = 30 },
            new SyncHistoryEntry { UserKey = LocalUser.Key, RequestedDays = 90 });
        await db.SaveChangesAsync();

        var results = await new MetricQueryService(db).GetRecentSyncHistoryAsync(2);

        Assert.Equal([90, 30], results.Select(entry => entry.RequestedDays));
        Assert.All(results, entry => Assert.Equal(LocalUser.Key, entry.UserKey));
    }

    [Fact]
    public async Task RecentSyncHistory_LimitIsAppliedBySqlite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDbContext(connection);
        await db.Database.EnsureCreatedAsync();

        for (var i = 0; i < 4; i++)
            db.SyncHistory.Add(new SyncHistoryEntry { UserKey = LocalUser.Key, RequestedDays = i + 1 });

        await db.SaveChangesAsync();

        var results = await new MetricQueryService(db).GetRecentSyncHistoryAsync(3);

        Assert.Equal([4, 3, 2], results.Select(entry => entry.RequestedDays));
    }

    private static HealthMetricsDbContext CreateDbContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<HealthMetricsDbContext>()
            .UseSqlite(connection)
            .Options);
}
