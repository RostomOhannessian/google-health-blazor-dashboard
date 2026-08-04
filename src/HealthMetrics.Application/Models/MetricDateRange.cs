namespace HealthMetrics.Application.Models;

public sealed record MetricDateRange(DateOnly StartDate, DateOnly EndDate)
{
    public int DayCount => EndDate.DayNumber - StartDate.DayNumber + 1;

    public static MetricDateRange ForRecentDays(int requestedDays, DateOnly endDate)
    {
        if (requestedDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedDays), "Requested days must be greater than zero.");

        if (endDate.DayNumber < requestedDays - 1)
            throw new ArgumentOutOfRangeException(nameof(endDate), "The requested range cannot begin before January 1, 0001.");

        return new MetricDateRange(endDate.AddDays(1 - requestedDays), endDate);
    }

    public static MetricDateRange ForYearToDate(DateOnly endDate)
    {
        return new MetricDateRange(new DateOnly(endDate.Year, 1, 1), endDate);
    }
}
