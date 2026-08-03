using HealthMetrics.Application.Interfaces;
using HealthMetrics.Application.Models;
using HealthMetrics.Infrastructure.Persistence;
using HealthMetrics.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HealthMetrics.Tests.Services;

public sealed class DemoSeedServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private HealthMetricsDbContext  _dbContext  = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HealthMetricsDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new HealthMetricsDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private IDemoSeedService CreateService() => new DemoSeedService(_dbContext);

    // ── Invalid argument guard ────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(91)]
    public async Task SeedAsync_InvalidDayCount_Throws(int days)
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => svc.SeedAsync(days));
    }

    // ── Insertion count ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(30)]
    public async Task SeedAsync_InsertsExactRequestedDays(int days)
    {
        var svc      = CreateService();
        var inserted = await svc.SeedAsync(days);

        Assert.Equal(days, inserted);
        var dbCount = await _dbContext.DailyMetricSnapshots.CountAsync();
        Assert.Equal(days, dbCount);
    }

    // ── Idempotency ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SeedAsync_CalledTwice_SecondRunInsertsZero()
    {
        var svc = CreateService();
        await svc.SeedAsync(30);

        var second = await svc.SeedAsync(30);

        Assert.Equal(0, second);
        Assert.Equal(30, await _dbContext.DailyMetricSnapshots.CountAsync());
    }

    [Fact]
    public async Task SeedAsync_SecondRunWithLargerWindow_OnlyInsertsNewDays()
    {
        var svc = CreateService();
        await svc.SeedAsync(7);

        var inserted = await svc.SeedAsync(14);

        Assert.Equal(7, inserted);
        Assert.Equal(14, await _dbContext.DailyMetricSnapshots.CountAsync());
    }

    // ── Determinism ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SeedAsync_SameDateAlwaysProducesSameHrValue()
    {
        // Seed on two separate DB instances; same date must yield identical HR.
        await using var connection2 = new SqliteConnection("Data Source=:memory:");
        await connection2.OpenAsync();
        var opts2 = new DbContextOptionsBuilder<HealthMetricsDbContext>().UseSqlite(connection2).Options;
        await using var db2 = new HealthMetricsDbContext(opts2);
        await db2.Database.EnsureCreatedAsync();

        await CreateService().SeedAsync(7);
        await new DemoSeedService(db2).SeedAsync(7);

        var rows1 = await _dbContext.DailyMetricSnapshots
            .OrderBy(s => s.MetricDate).Select(s => s.RestingHeartRateBpm).ToListAsync();
        var rows2 = await db2.DailyMetricSnapshots
            .OrderBy(s => s.MetricDate).Select(s => s.RestingHeartRateBpm).ToListAsync();

        Assert.Equal(rows1, rows2);
    }

    // ── Value ranges ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SeedAsync_AllInsertedValuesAreWithinExpectedRanges()
    {
        var svc = CreateService();
        await svc.SeedAsync(30);

        var snapshots = await _dbContext.DailyMetricSnapshots.ToListAsync();
        foreach (var s in snapshots)
        {
            Assert.InRange(s.RestingHeartRateBpm!.Value, 52, 67);
            Assert.InRange(s.HrvRmssdMilliseconds!.Value, 30m, 65m);
            Assert.InRange(s.CardioLoad!.Value, 45m, 100m);
            Assert.InRange(s.SleepEfficiency!.Value, 82m, 97m);
            Assert.InRange(s.DeepSleepMinutes!.Value, 45, 110);
            Assert.InRange(s.RemSleepMinutes!.Value, 70, 150);
            Assert.InRange(s.ConsumedCaloriesKcal!.Value, 1700, 2599);
        }

        Assert.Contains(snapshots, snapshot => snapshot.Acwr is not null);
        Assert.All(snapshots, snapshot =>
        {
            Assert.Equal(snapshot.CardioLoad, snapshot.TargetLoad);
        });
    }
}
