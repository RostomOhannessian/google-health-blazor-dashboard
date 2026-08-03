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
            RestingHeartRateBpm = 58
        });
        await dbContext.SaveChangesAsync();

        var result = await new ManualLoadEntryService(dbContext).SaveAsync(
            new ManualLoadEntry(date, 78m, 75m));

        var stored = await dbContext.DailyMetricSnapshots.SingleAsync();
        Assert.True(result.Succeeded);
        Assert.Equal(78m, stored.CardioLoad);
        Assert.Equal(75m, stored.TargetLoad);
        Assert.Equal(58, stored.RestingHeartRateBpm);
    }

    [Fact]
    public async Task SaveAsync_StoresOnlyOneTargetWithinTheMondayWeek()
    {
        var monday = new DateOnly(2026, 8, 3);
        var friday = monday.AddDays(4);
        var selectedDate = monday.AddDays(2);
        dbContext.DailyMetricSnapshots.AddRange(
            new DailyMetricSnapshot { MetricDate = monday, TargetLoad = 70m },
            new DailyMetricSnapshot { MetricDate = friday, TargetLoad = 75m });
        await dbContext.SaveChangesAsync();

        var result = await new ManualLoadEntryService(dbContext).SaveAsync(
            new ManualLoadEntry(selectedDate, null, 80m));

        var stored = await dbContext.DailyMetricSnapshots
            .OrderBy(snapshot => snapshot.MetricDate)
            .ToListAsync();
        Assert.True(result.Succeeded);
        Assert.Equal([monday, selectedDate, friday], stored.Select(snapshot => snapshot.MetricDate));
        Assert.Null(stored[0].TargetLoad);
        Assert.Equal(80m, stored[1].TargetLoad);
        Assert.Null(stored[2].TargetLoad);
    }

    [Fact]
    public async Task SaveAsync_ClearsNullableManualFieldsWithoutDeletingProviderFields()
    {
        var date = new DateOnly(2026, 8, 2);
        dbContext.DailyMetricSnapshots.Add(new DailyMetricSnapshot
        {
            MetricDate = date,
            CardioLoad = 78m,
            TargetLoad = 75m
        });
        await dbContext.SaveChangesAsync();

        var result = await new ManualLoadEntryService(dbContext).SaveAsync(
            new ManualLoadEntry(date, null, null));

        var stored = await dbContext.DailyMetricSnapshots.SingleAsync();
        Assert.True(result.Succeeded);
        Assert.Null(stored.CardioLoad);
        Assert.Null(stored.TargetLoad);
    }

    [Fact]
    public async Task SaveAsync_ClearsOnlyBlankManualValuesAndPreservesProviderFields()
    {
        var date = new DateOnly(2026, 8, 2);
        dbContext.DailyMetricSnapshots.Add(new DailyMetricSnapshot
        {
            MetricDate = date,
            CardioLoad = 78m,
            TargetLoad = 75m
        });
        await dbContext.SaveChangesAsync();

        var result = await new ManualLoadEntryService(dbContext).SaveAsync(
            new ManualLoadEntry(date, null, 80m));

        var stored = await dbContext.DailyMetricSnapshots.SingleAsync();
        Assert.True(result.Succeeded);
        Assert.Null(stored.CardioLoad);
        Assert.Equal(80m, stored.TargetLoad);
    }

    [Theory]
    [InlineData(-1d, null, "Manual Cardio Load cannot be negative.")]
    [InlineData(null, -1d, "Weekly target cannot be negative.")]
    public async Task SaveAsync_RejectsInvalidManualValues(
        double? cardioLoad,
        double? targetLoad,
        string expectedError)
    {
        var result = await new ManualLoadEntryService(dbContext).SaveAsync(
            new ManualLoadEntry(
                new DateOnly(2026, 8, 2),
                (decimal?)cardioLoad,
                (decimal?)targetLoad));

        Assert.False(result.Succeeded);
        Assert.Equal(expectedError, result.ErrorMessage);
        Assert.Empty(await dbContext.DailyMetricSnapshots.ToListAsync());
    }
}
