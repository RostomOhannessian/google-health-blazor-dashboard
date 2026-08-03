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
