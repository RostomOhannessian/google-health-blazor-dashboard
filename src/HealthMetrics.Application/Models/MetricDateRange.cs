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

    public static MetricDateRange ForYearToDate(DateOnly today)
    {
        var startDate = new DateOnly(today.Year, 1, 1);
        var endDate = GetLastCompletedWeekEnd(today);

        // There is no completed Sunday in the current year during its first days.
        if (endDate < startDate)
            endDate = today;

        return new MetricDateRange(startDate, endDate);
    }

    private static DateOnly GetLastCompletedWeekEnd(DateOnly today) =>
        today.AddDays(-(int)today.DayOfWeek);
}
