namespace HealthMetrics.Application.Models;

public sealed record WeeklyLoadSummary(
    DateOnly WeekStart,
    decimal? CardioLoad,
    decimal? TargetLoad,
    decimal? Acwr);

public sealed record DailyLoadPoint(
    DateOnly MetricDate,
    DateOnly WeekStart,
    decimal? CardioLoad,
    decimal? CumulativeCardioLoad,
    decimal? TargetLoad,
    decimal? Acwr);

public static class WeeklyLoadCalculator
{
    public static DateOnly GetWeekStart(DateOnly date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-daysSinceMonday);
    }

    public static IReadOnlyList<DailyLoadPoint> BuildDailySeries(
        IEnumerable<DailyMetricSnapshot> snapshots)
    {
        var ordered = snapshots.OrderBy(snapshot => snapshot.MetricDate).ToList();
        var points = new List<DailyLoadPoint>(ordered.Count);
        DateOnly? currentWeekStart = null;
        var cumulativeCardioLoad = 0m;
        var hasCardioLoad = false;

        foreach (var snapshot in ordered)
        {
            var weekStart = GetWeekStart(snapshot.MetricDate);
            if (currentWeekStart != weekStart)
            {
                currentWeekStart = weekStart;
                cumulativeCardioLoad = 0m;
                hasCardioLoad = false;
            }

            if (snapshot.CardioLoad.HasValue)
            {
                cumulativeCardioLoad += snapshot.CardioLoad.Value;
                hasCardioLoad = true;
            }

            points.Add(new DailyLoadPoint(
                snapshot.MetricDate,
                weekStart,
                snapshot.CardioLoad,
                hasCardioLoad ? cumulativeCardioLoad : null,
                snapshot.TargetLoad,
                snapshot.Acwr));
        }

        return points;
    }

    public static IReadOnlyList<WeeklyLoadSummary> Summarize(
        IEnumerable<DailyMetricSnapshot> snapshots) =>
        snapshots
            .GroupBy(snapshot => GetWeekStart(snapshot.MetricDate))
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var ordered = group.OrderBy(snapshot => snapshot.MetricDate).ToList();
                return new WeeklyLoadSummary(
                    group.Key,
                    Sum(ordered.Select(snapshot => snapshot.CardioLoad)),
                    LastValue(ordered.Select(snapshot => snapshot.TargetLoad)),
                    LastValue(ordered.Select(snapshot => snapshot.Acwr)));
            })
            .ToList();

    private static decimal? Sum(IEnumerable<decimal?> values)
    {
        var nonNullValues = values.Where(value => value.HasValue).Select(value => value.GetValueOrDefault()).ToList();
        return nonNullValues.Count == 0 ? null : nonNullValues.Sum();
    }

    private static decimal? LastValue(IEnumerable<decimal?> values) =>
        values.LastOrDefault(value => value.HasValue);
}
