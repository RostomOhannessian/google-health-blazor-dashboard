using FitbitMetrics.Application.Models;
using FitbitMetrics.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FitbitMetrics.Tests.Persistence;

public sealed class FitbitDbContextTests
{
    [Fact]
    public async Task Should_EnforceUniqueSnapshotPerUserAndDate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<FitbitDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setupContext = new FitbitDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
        }

        await using (var firstContext = new FitbitDbContext(options))
        {
            firstContext.DailyMetricSnapshots.Add(new DailyMetricSnapshot
            {
                UserKey = DemoUser.Key,
                MetricDate = new DateOnly(2026, 7, 18),
                RestingHeartRateBpm = 58
            });

            await firstContext.SaveChangesAsync();
        }

        await using var secondContext = new FitbitDbContext(options);
        secondContext.DailyMetricSnapshots.Add(new DailyMetricSnapshot
        {
            UserKey = DemoUser.Key,
            MetricDate = new DateOnly(2026, 7, 18),
            RestingHeartRateBpm = 61
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => secondContext.SaveChangesAsync());
    }
}
