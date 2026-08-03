using HealthMetrics.Application.Models;
using HealthMetrics.Infrastructure.Persistence;
using HealthMetrics.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HealthMetrics.Tests.Services;

public sealed class ManualLoadEntryServiceTests : IAsyncLifetime
{
    private SqliteConnection connection = null!;
    private HealthMetricsDbContext dbContext = null!;

    public async Task InitializeAsync()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        dbContext = new HealthMetricsDbContext(
            new DbContextOptionsBuilder<HealthMetricsDbContext>().UseSqlite(connection).Options);
        await dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task SaveAsync_PersistsManualFieldsAndPreservesSyncedFields()
    {
        var date = new DateOnly(2026, 8, 2);
        dbContext.DailyMetricSnapshots.Add(new DailyMetricSnapshot
        {
            MetricDate = date,
            RestingHeartRateBpm = 58,
            ActiveZoneMinutes = 82m,
            ActiveZoneMinutesAcwr = 1.08m
        });
        await dbContext.SaveChangesAsync();

        var result = await new ManualLoadEntryService(dbContext).SaveAsync(
            new ManualLoadEntry(date, 78m, 60m, 90m));

        var stored = await dbContext.DailyMetricSnapshots.SingleAsync();
        Assert.True(result.Succeeded);
        Assert.Equal(78m, stored.CardioLoad);
        Assert.Equal(60m, stored.TargetLoadMin);
        Assert.Equal(90m, stored.TargetLoadMax);
        Assert.Equal(58, stored.RestingHeartRateBpm);
        Assert.Equal(82m, stored.ActiveZoneMinutes);
        Assert.Equal(1.08m, stored.ActiveZoneMinutesAcwr);
    }

    [Fact]
    public async Task SaveAsync_ClearsNullableManualFieldsWithoutDeletingProviderFields()
    {
        var date = new DateOnly(2026, 8, 2);
        dbContext.DailyMetricSnapshots.Add(new DailyMetricSnapshot
        {
            MetricDate = date,
            CardioLoad = 78m,
            TargetLoadMin = 60m,
            TargetLoadMax = 90m,
            ActiveZoneMinutes = 82m
        });
        await dbContext.SaveChangesAsync();

        var result = await new ManualLoadEntryService(dbContext).SaveAsync(
            new ManualLoadEntry(date, null, null, null));

        var stored = await dbContext.DailyMetricSnapshots.SingleAsync();
        Assert.True(result.Succeeded);
        Assert.Null(stored.CardioLoad);
        Assert.Null(stored.TargetLoadMin);
        Assert.Null(stored.TargetLoadMax);
        Assert.Equal(82m, stored.ActiveZoneMinutes);
    }

    [Fact]
    public async Task SaveAsync_ClearsOnlyBlankManualValuesAndPreservesProviderFields()
    {
        var date = new DateOnly(2026, 8, 2);
        dbContext.DailyMetricSnapshots.Add(new DailyMetricSnapshot
        {
            MetricDate = date,
            CardioLoad = 78m,
            TargetLoadMin = 60m,
            TargetLoadMax = 90m,
            ActiveZoneMinutes = 82m,
            ActiveZoneMinutesAcwr = 1.08m
        });
        await dbContext.SaveChangesAsync();

        var result = await new ManualLoadEntryService(dbContext).SaveAsync(
            new ManualLoadEntry(date, null, 65m, 95m));

        var stored = await dbContext.DailyMetricSnapshots.SingleAsync();
        Assert.True(result.Succeeded);
        Assert.Null(stored.CardioLoad);
        Assert.Equal(65m, stored.TargetLoadMin);
        Assert.Equal(95m, stored.TargetLoadMax);
        Assert.Equal(82m, stored.ActiveZoneMinutes);
        Assert.Equal(1.08m, stored.ActiveZoneMinutesAcwr);
    }

    [Theory]
    [InlineData(-1d, null, null, "Manual Cardio Load cannot be negative.")]
    [InlineData(null, -1d, null, "Manual target minimum cannot be negative.")]
    [InlineData(null, null, -1d, "Manual target maximum cannot be negative.")]
    [InlineData(null, 91d, 90d, "Manual target minimum cannot exceed the maximum.")]
    public async Task SaveAsync_RejectsInvalidManualValues(
        double? cardioLoad,
        double? targetMin,
        double? targetMax,
        string expectedError)
    {
        var result = await new ManualLoadEntryService(dbContext).SaveAsync(
            new ManualLoadEntry(
                new DateOnly(2026, 8, 2),
                (decimal?)cardioLoad,
                (decimal?)targetMin,
                (decimal?)targetMax));

        Assert.False(result.Succeeded);
        Assert.Equal(expectedError, result.ErrorMessage);
        Assert.Empty(await dbContext.DailyMetricSnapshots.ToListAsync());
    }
}
