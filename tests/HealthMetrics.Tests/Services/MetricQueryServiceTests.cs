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

    [Fact]
    public async Task RecentMetrics_FiltersByCalendarRangeInsteadOfLastRowCount()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDbContext(connection);
        await db.Database.EnsureCreatedAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.DailyMetricSnapshots.AddRange(
            new DailyMetricSnapshot { UserKey = LocalUser.Key, MetricDate = today, RestingHeartRateBpm = 50 },
            new DailyMetricSnapshot { UserKey = LocalUser.Key, MetricDate = today.AddDays(-2), RestingHeartRateBpm = 52 },
            new DailyMetricSnapshot { UserKey = LocalUser.Key, MetricDate = today.AddDays(-10), RestingHeartRateBpm = 57 },
            new DailyMetricSnapshot { UserKey = LocalUser.Key, MetricDate = today.AddDays(1), RestingHeartRateBpm = 49 },
            new DailyMetricSnapshot { UserKey = "another-user", MetricDate = today.AddDays(-1), RestingHeartRateBpm = 61 });
        await db.SaveChangesAsync();

        var results = await new MetricQueryService(db).GetRecentMetricsAsync(7);

        Assert.Equal([today, today.AddDays(-2)], results.Select(metric => metric.MetricDate));
        Assert.All(results, metric => Assert.Equal(LocalUser.Key, metric.UserKey));
    }

    [Fact]
    public async Task RecentMetrics_ProjectsWeeklyTargetAcrossMondayWeek()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDbContext(connection);
        await db.Database.EnsureCreatedAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var weekStart = WeeklyLoadCalculator.GetWeekStart(today);
        db.DailyMetricSnapshots.Add(new DailyMetricSnapshot
        {
            UserKey = LocalUser.Key,
            MetricDate = weekStart,
            TargetLoad = 90m
        });
        if (weekStart != today)
        {
            db.DailyMetricSnapshots.Add(new DailyMetricSnapshot
            {
                UserKey = LocalUser.Key,
                MetricDate = today,
                CardioLoad = 75m
            });
        }

        await db.SaveChangesAsync();

        var results = await new MetricQueryService(db).GetRecentMetricsAsync(1);

        var snapshot = Assert.Single(results);
        Assert.Equal(today, snapshot.MetricDate);
        Assert.Equal(90m, snapshot.TargetLoad);
    }

    [Fact]
    public async Task AllMetrics_ReturnsHistoryBeyondRecentRange()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDbContext(connection);
        await db.Database.EnsureCreatedAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var olderDate = today.AddDays(-400);
        db.DailyMetricSnapshots.AddRange(
            new DailyMetricSnapshot
            {
                UserKey = LocalUser.Key,
                MetricDate = olderDate,
                CardioLoad = 44m,
                TargetLoad = 120m
            },
            new DailyMetricSnapshot
            {
                UserKey = LocalUser.Key,
                MetricDate = today,
                CardioLoad = 75m,
                TargetLoad = 180m
            },
            new DailyMetricSnapshot
            {
                UserKey = LocalUser.Key,
                MetricDate = today.AddDays(1),
                CardioLoad = 77m,
                TargetLoad = 180m
            },
            new DailyMetricSnapshot
            {
                UserKey = "another-user",
                MetricDate = olderDate,
                CardioLoad = 99m
            });
        await db.SaveChangesAsync();

        var results = await new MetricQueryService(db).GetAllMetricsAsync();

        Assert.Equal([today, olderDate], results.Select(metric => metric.MetricDate));
        Assert.Equal(44m, results[1].CardioLoad);
        Assert.Equal(120m, results[1].TargetLoad);
        Assert.All(results, metric => Assert.Equal(LocalUser.Key, metric.UserKey));
    }

    private static HealthMetricsDbContext CreateDbContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<HealthMetricsDbContext>()
            .UseSqlite(connection)
            .Options);
}
