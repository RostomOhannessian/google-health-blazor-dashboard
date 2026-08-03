namespace HealthMetrics.Application.Models;

public static class TargetLoadCalculator
{
    private const int MinimumHistoryDays = 7;
    private const int MaximumHistoryDays = 28;

    public static void Recalculate(IEnumerable<DailyMetricSnapshot> snapshots)
    {
        var snapshotList = snapshots.ToList();
        var cardioLoads = snapshotList
            .GroupBy(snapshot => snapshot.MetricDate)
            .ToDictionary(group => group.Key, group => group.Last().CardioLoad);

        foreach (var snapshot in snapshotList)
        {
            var target = Calculate(cardioLoads, snapshot.MetricDate);
            snapshot.TargetLoadMin = target?.Min;
            snapshot.TargetLoadMax = target?.Max;
        }
    }

    public static TargetLoadRange? Calculate(
        IReadOnlyDictionary<DateOnly, decimal?> cardioLoads,
        DateOnly date)
    {
        var values = new List<decimal>(MaximumHistoryDays);
        for (var offset = 0; offset < MaximumHistoryDays; offset++)
        {
            if (!cardioLoads.TryGetValue(date.AddDays(-offset), out var value) || value is null)
                break;

            values.Add(value.Value);
        }

        if (values.Count < MinimumHistoryDays)
            return null;

        var baseline = values.Average();
        if (baseline <= 0)
            return null;

        return new TargetLoadRange(
            Math.Round(baseline * 0.8m, 2, MidpointRounding.AwayFromZero),
            Math.Round(baseline * 1.2m, 2, MidpointRounding.AwayFromZero));
    }
}

public readonly record struct TargetLoadRange(decimal Min, decimal Max);
