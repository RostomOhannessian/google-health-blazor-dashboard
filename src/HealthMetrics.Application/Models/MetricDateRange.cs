namespace HealthMetrics.Application.Models;

public sealed record MetricDateRange(DateOnly StartDate, DateOnly EndDate)
{
    public int DayCount => EndDate.DayNumber - StartDate.DayNumber + 1;

    public static MetricDateRange ForRecentFullWeeks(int requestedDays, DateOnly today)
    {
        if (requestedDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedDays), "Requested days must be greater than zero.");

        var weekCount = (requestedDays + 6) / 7;
        var endDate = GetLastCompletedWeekEnd(today);
        return new MetricDateRange(endDate.AddDays(-(weekCount * 7 - 1)), endDate);
    }

    public static MetricDateRange ForRecentFullWeeksThroughLastCompletedDay(int requestedDays, DateOnly today)
    {
        if (requestedDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedDays), "Requested days must be greater than zero.");

        if (today == DateOnly.MinValue)
            throw new ArgumentOutOfRangeException(nameof(today), "Today must have a preceding completed day.");

        var lastCompletedDay = today.AddDays(-1);
        var completeWeeks = ForRecentFullWeeks(requestedDays, lastCompletedDay);
        return completeWeeks with { EndDate = lastCompletedDay };
    }

    public static MetricDateRange ForYearToDate(DateOnly today)
    {
        var startDate = new DateOnly(today.Year, 1, 1);
        // Keep the first day of a year queryable even though it has not completed yet.
        var endDate = today == startDate ? startDate : today.AddDays(-1);

        return new MetricDateRange(startDate, endDate);
    }

    private static DateOnly GetLastCompletedWeekEnd(DateOnly today) =>
        today.AddDays(-(int)today.DayOfWeek);
}
