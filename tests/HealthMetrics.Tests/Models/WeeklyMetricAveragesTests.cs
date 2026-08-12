using HealthMetrics.Application.Models;

namespace HealthMetrics.Tests.Models;

public sealed class WeeklyMetricAveragesTests
{
    [Fact]
    public void From_AveragesRecordedValuesAndIgnoresMissingValues()
    {
        var weekStart = new DateOnly(2026, 8, 3);
        var summary = WeeklyMetricAverages.From(
            weekStart,
            [
                new DailyMetricSnapshot
                {
                    MetricDate = weekStart,
                    RestingHeartRateBpm = 60,
                    HrvRmssdMilliseconds = 40m,
                    DailyVo2MaxMlKgMin = 45m,
                    CardioLoad = 100m,
                    TargetLoad = 500m,
                    SleepEfficiency = 80m,
                    ConsumedCaloriesKcal = 2000,
                    CarbohydratesGrams = 250m,
                    EstimatedAlcoholGrams = 20m
                },
                new DailyMetricSnapshot
                {
                    MetricDate = weekStart.AddDays(1),
                    RestingHeartRateBpm = 64,
                    HrvRmssdMilliseconds = null,
                    DailyVo2MaxMlKgMin = 47m,
                    CardioLoad = 50m,
                    TargetLoad = 500m,
                    SleepEfficiency = 90m,
                    ConsumedCaloriesKcal = 2200,
                    CarbohydratesGrams = null,
                    EstimatedAlcoholGrams = 0m
                },
                new DailyMetricSnapshot
                {
                    MetricDate = weekStart.AddDays(2)
                }
            ]);

        Assert.Equal(weekStart, summary.WeekStart);
        Assert.Equal(62m, summary.RestingHeartRateBpm);
        Assert.Equal(40m, summary.HrvRmssdMilliseconds);
        Assert.Equal(46m, summary.DailyVo2MaxMlKgMin);
        Assert.Equal(150m, summary.CardioLoadTotal);
        Assert.Equal(500m, summary.TargetLoad);
        Assert.Equal(85m, summary.SleepEfficiency);
        Assert.Equal(2100m, summary.ConsumedCaloriesKcal);
        Assert.Equal(250m, summary.CarbohydratesGrams);
        Assert.Equal(10m, summary.EstimatedAlcoholGrams);
    }

    [Fact]
    public void From_ReturnsNullForMetricsWithoutRecordedValues()
    {
        var summary = WeeklyMetricAverages.From(
            new DateOnly(2026, 8, 3),
            [new DailyMetricSnapshot()]);

        Assert.Null(summary.RestingHeartRateBpm);
        Assert.Null(summary.HrvRmssdMilliseconds);
        Assert.Null(summary.CardioLoadTotal);
        Assert.Null(summary.ProteinGrams);
        Assert.Null(summary.EstimatedAlcoholGrams);
    }
}
