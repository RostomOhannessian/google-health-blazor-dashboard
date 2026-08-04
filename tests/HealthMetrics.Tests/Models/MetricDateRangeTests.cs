using HealthMetrics.Application.Models;

namespace HealthMetrics.Tests.Models;

public sealed class MetricDateRangeTests
{
    [Theory]
    [InlineData(7, 2026, 8, 3, 2026, 7, 27, 2026, 8, 2)]
    [InlineData(30, 2026, 8, 3, 2026, 6, 29, 2026, 8, 2)]
    [InlineData(90, 2026, 8, 3, 2026, 5, 4, 2026, 8, 2)]
    [InlineData(30, 2026, 8, 9, 2026, 7, 6, 2026, 8, 9)]
    public void ForRecentFullWeeks_RoundsUpAndEndsOnSunday(
        int requestedDays,
        int todayYear,
        int todayMonth,
        int todayDay,
        int expectedStartYear,
        int expectedStartMonth,
        int expectedStartDay,
        int expectedEndYear,
        int expectedEndMonth,
        int expectedEndDay)
    {
        var range = MetricDateRange.ForRecentFullWeeks(
            requestedDays,
            new DateOnly(todayYear, todayMonth, todayDay));

        Assert.Equal(new DateOnly(expectedStartYear, expectedStartMonth, expectedStartDay), range.StartDate);
        Assert.Equal(new DateOnly(expectedEndYear, expectedEndMonth, expectedEndDay), range.EndDate);
        Assert.Equal(((requestedDays + 6) / 7) * 7, range.DayCount);
    }

    [Theory]
    [InlineData(7, 2026, 8, 3, 2026, 7, 27, 2026, 8, 2, 7)]
    [InlineData(30, 2026, 8, 4, 2026, 6, 29, 2026, 8, 3, 36)]
    [InlineData(90, 2026, 8, 4, 2026, 5, 4, 2026, 8, 3, 92)]
    [InlineData(30, 2026, 8, 10, 2026, 7, 6, 2026, 8, 9, 35)]
    public void ForRecentFullWeeksThroughLastCompletedDay_IncludesCompletedCurrentWeekDays(
        int requestedDays,
        int todayYear,
        int todayMonth,
        int todayDay,
        int expectedStartYear,
        int expectedStartMonth,
        int expectedStartDay,
        int expectedEndYear,
        int expectedEndMonth,
        int expectedEndDay,
        int expectedDayCount)
    {
        var range = MetricDateRange.ForRecentFullWeeksThroughLastCompletedDay(
            requestedDays,
            new DateOnly(todayYear, todayMonth, todayDay));

        Assert.Equal(new DateOnly(expectedStartYear, expectedStartMonth, expectedStartDay), range.StartDate);
        Assert.Equal(new DateOnly(expectedEndYear, expectedEndMonth, expectedEndDay), range.EndDate);
        Assert.Equal(expectedDayCount, range.DayCount);
    }

    [Fact]
    public void ForYearToDate_EndsOnLastCompletedDay()
    {
        var range = MetricDateRange.ForYearToDate(new DateOnly(2026, 8, 4));

        Assert.Equal(new DateOnly(2026, 1, 1), range.StartDate);
        Assert.Equal(new DateOnly(2026, 8, 3), range.EndDate);
        Assert.Equal(215, range.DayCount);
    }
}
