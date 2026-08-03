using HealthMetrics.Application.Models;

namespace HealthMetrics.Tests.Models;

public sealed class WeeklyMetricAveragesTests
{
    [Fact]
    public void From_AveragesRecordedValuesAndIgnoresMissingValues()
    {
        var weekStart = new DateOnly(2026, 8, 3);
        var averages = WeeklyMetricAverages.From(
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
                    CarbohydratesGrams = 250m
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
                    CarbohydratesGrams = null
                },
                new DailyMetricSnapshot
                {
                    MetricDate = weekStart.AddDays(2)
                }
            ]);

        Assert.Equal(weekStart, averages.WeekStart);
        Assert.Equal(62m, averages.RestingHeartRateBpm);
        Assert.Equal(40m, averages.HrvRmssdMilliseconds);
        Assert.Equal(46m, averages.DailyVo2MaxMlKgMin);
        Assert.Equal(75m, averages.CardioLoad);
        Assert.Equal(500m, averages.TargetLoad);
        Assert.Equal(85m, averages.SleepEfficiency);
        Assert.Equal(2100m, averages.ConsumedCaloriesKcal);
        Assert.Equal(250m, averages.CarbohydratesGrams);
    }

    [Fact]
    public void From_ReturnsNullForMetricsWithoutRecordedValues()
    {
        var averages = WeeklyMetricAverages.From(
            new DateOnly(2026, 8, 3),
            [new DailyMetricSnapshot()]);

        Assert.Null(averages.RestingHeartRateBpm);
        Assert.Null(averages.HrvRmssdMilliseconds);
        Assert.Null(averages.CardioLoad);
        Assert.Null(averages.ProteinGrams);
    }
}
