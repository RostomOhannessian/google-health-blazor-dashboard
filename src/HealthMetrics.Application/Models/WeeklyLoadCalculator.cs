namespace HealthMetrics.Application.Models;

public sealed record WeeklyLoadSummary(
    DateOnly WeekStart,
    decimal? CardioLoad,
    decimal? TargetLoad,
    decimal? Acwr);

public static class WeeklyLoadCalculator
{
    public static DateOnly GetWeekStart(DateOnly date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-daysSinceMonday);
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
