using HealthMetrics.Application.Models;

namespace HealthMetrics.Tests.Models;

public sealed class MetricDateRangeTests
{
    [Theory]
    [InlineData(7, 2026, 8, 4, 2026, 7, 29)]
    [InlineData(30, 2026, 8, 4, 2026, 7, 6)]
    [InlineData(90, 2026, 8, 4, 2026, 5, 7)]
    [InlineData(30, 2026, 8, 9, 2026, 7, 11)]
    public void ForRecentDays_UsesExactInclusiveCalendarRange(
        int requestedDays,
        int endYear,
        int endMonth,
        int endDay,
        int expectedStartYear,
        int expectedStartMonth,
        int expectedStartDay)
    {
        var range = MetricDateRange.ForRecentDays(
            requestedDays,
            new DateOnly(endYear, endMonth, endDay));

        Assert.Equal(new DateOnly(expectedStartYear, expectedStartMonth, expectedStartDay), range.StartDate);
        Assert.Equal(new DateOnly(endYear, endMonth, endDay), range.EndDate);
        Assert.Equal(requestedDays, range.DayCount);
    }

    [Fact]
    public void ForRecentDays_CanEndOnJanuaryFirst()
    {
        var range = MetricDateRange.ForRecentDays(1, new DateOnly(2026, 1, 1));

        Assert.Equal(new DateOnly(2026, 1, 1), range.StartDate);
        Assert.Equal(new DateOnly(2026, 1, 1), range.EndDate);
        Assert.Equal(1, range.DayCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ForRecentDays_RejectsNonPositiveDayCounts(int requestedDays)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MetricDateRange.ForRecentDays(requestedDays, new DateOnly(2026, 8, 4)));
    }

    [Fact]
    public void ForRecentDays_RejectsRangesBeforeDateOnlyMinimum()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MetricDateRange.ForRecentDays(2, DateOnly.MinValue));
    }

    [Fact]
    public void ForYearToDate_IncludesCurrentDate()
    {
        var range = MetricDateRange.ForYearToDate(new DateOnly(2026, 8, 4));

        Assert.Equal(new DateOnly(2026, 1, 1), range.StartDate);
        Assert.Equal(new DateOnly(2026, 8, 4), range.EndDate);
        Assert.Equal(216, range.DayCount);
    }

    [Fact]
    public void ForYearToDate_OnJanuaryFirstContainsOneDay()
    {
        var range = MetricDateRange.ForYearToDate(new DateOnly(2026, 1, 1));

        Assert.Equal(new DateOnly(2026, 1, 1), range.StartDate);
        Assert.Equal(new DateOnly(2026, 1, 1), range.EndDate);
        Assert.Equal(1, range.DayCount);
    }
}
