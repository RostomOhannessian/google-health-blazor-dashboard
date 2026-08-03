using HealthMetrics.Application.Models;

namespace HealthMetrics.Tests.Models;

public sealed class WeeklyLoadCalculatorTests
{
    [Theory]
    [InlineData(2026, 8, 3, 2026, 8, 3)]
    [InlineData(2026, 8, 4, 2026, 8, 3)]
    [InlineData(2026, 8, 9, 2026, 8, 3)]
    [InlineData(2026, 8, 10, 2026, 8, 10)]
    public void GetWeekStart_UsesMondayAsTheFirstDay(
        int year,
        int month,
        int day,
        int expectedYear,
        int expectedMonth,
        int expectedDay)
    {
        var result = WeeklyLoadCalculator.GetWeekStart(new DateOnly(year, month, day));

        Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay), result);
    }

    [Fact]
    public void BuildDailySeries_AccumulatesWithinEachWeekAndResetsOnMonday()
    {
        var monday = new DateOnly(2026, 8, 3);
        var series = WeeklyLoadCalculator.BuildDailySeries(
        [
            new DailyMetricSnapshot
            {
                MetricDate = monday,
                CardioLoad = 100m
            },
            new DailyMetricSnapshot
            {
                MetricDate = monday.AddDays(2),
                CardioLoad = 50m
            },
            new DailyMetricSnapshot
            {
                MetricDate = monday.AddDays(6)
            },
            new DailyMetricSnapshot
            {
                MetricDate = monday.AddDays(7),
                CardioLoad = 20m
            },
            new DailyMetricSnapshot
            {
                MetricDate = monday.AddDays(8),
                CardioLoad = 5m
            }
        ]);

        Assert.Equal(5, series.Count);
        Assert.Equal(
            new decimal?[] { 100m, 150m, 150m, 20m, 25m },
            series.Select(point => point.CumulativeCardioLoad).ToArray());
        Assert.Equal(
            new decimal?[] { 100m, 50m, null, 20m, 5m },
            series.Select(point => point.CardioLoad).ToArray());
        Assert.Equal(
            [monday, monday, monday, monday.AddDays(7), monday.AddDays(7)],
            series.Select(point => point.WeekStart).ToArray());
    }

    [Fact]
    public void BuildDailySeries_PreservesWeekToDateLoadForAPartialWeekDisplay()
    {
        var monday = new DateOnly(2026, 8, 3);
        var series = WeeklyLoadCalculator.BuildDailySeries(
        [
            new DailyMetricSnapshot { MetricDate = monday, CardioLoad = 40m },
            new DailyMetricSnapshot { MetricDate = monday.AddDays(1), CardioLoad = 30m },
            new DailyMetricSnapshot { MetricDate = monday.AddDays(2), CardioLoad = 20m },
            new DailyMetricSnapshot { MetricDate = monday.AddDays(3), CardioLoad = 10m }
        ]);

        var visiblePoints = series
            .Where(point => point.MetricDate >= monday.AddDays(2))
            .ToList();

        Assert.Equal(
            new decimal?[] { 90m, 100m },
            visiblePoints.Select(point => point.CumulativeCardioLoad).ToArray());
    }

    [Fact]
    public void Summarize_SumsDailyLoadAndUsesTheLastWeeklyValues()
    {
        var monday = new DateOnly(2026, 8, 3);
        var summaries = WeeklyLoadCalculator.Summarize(
        [
            new DailyMetricSnapshot
            {
                MetricDate = monday,
                CardioLoad = 100m,
                TargetLoad = 500m,
                Acwr = 1.1m
            },
            new DailyMetricSnapshot
            {
                MetricDate = monday.AddDays(2),
                CardioLoad = 50m,
                TargetLoad = 600m,
                Acwr = 1.2m
            },
            new DailyMetricSnapshot
            {
                MetricDate = monday.AddDays(6)
            },
            new DailyMetricSnapshot
            {
                MetricDate = monday.AddDays(7),
                CardioLoad = 20m,
                TargetLoad = 700m,
                Acwr = 1.3m
            }
        ]);

        Assert.Equal(2, summaries.Count);
        Assert.Equal(monday, summaries[0].WeekStart);
        Assert.Equal(150m, summaries[0].CardioLoad);
        Assert.Equal(600m, summaries[0].TargetLoad);
        Assert.Equal(1.2m, summaries[0].Acwr);
        Assert.Equal(monday.AddDays(7), summaries[1].WeekStart);
        Assert.Equal(20m, summaries[1].CardioLoad);
        Assert.Equal(700m, summaries[1].TargetLoad);
        Assert.Equal(1.3m, summaries[1].Acwr);
    }
}
