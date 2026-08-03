namespace HealthMetrics.Application.Models;

public enum AcwrStatus
{
    Insufficient,
    Undertraining,
    OptimalZone,
    Overreaching,
    HighDangerZone
}

public static class AcwrCalculator
{
    public static void Recalculate(IEnumerable<DailyMetricSnapshot> snapshots)
    {
        var snapshotList = snapshots.ToList();
        var cardioLoads = snapshotList
            .GroupBy(snapshot => snapshot.MetricDate)
            .ToDictionary(group => group.Key, group => group.Last().CardioLoad);

        foreach (var snapshot in snapshotList)
            snapshot.Acwr = Calculate(cardioLoads, snapshot.MetricDate);
    }

    public static decimal? Calculate(
        IReadOnlyDictionary<DateOnly, decimal?> cardioLoads,
        DateOnly date)
    {
        if (!TryGetAverage(cardioLoads, date, days: 7, out var acuteLoad)
            || !TryGetAverage(cardioLoads, date, days: 28, out var chronicLoad)
            || chronicLoad <= 0)
        {
            return null;
        }

        return Math.Round(acuteLoad / chronicLoad, 2, MidpointRounding.AwayFromZero);
    }

    public static AcwrStatus GetStatus(decimal? acwr) => acwr switch
    {
        null => AcwrStatus.Insufficient,
        < 0.8m => AcwrStatus.Undertraining,
        <= 1.3m => AcwrStatus.OptimalZone,
        <= 1.5m => AcwrStatus.Overreaching,
        _ => AcwrStatus.HighDangerZone
    };

    private static bool TryGetAverage(
        IReadOnlyDictionary<DateOnly, decimal?> cardioLoads,
        DateOnly endDate,
        int days,
        out decimal average)
    {
        average = 0;
        var values = new List<decimal>(days);

        for (var offset = 0; offset < days; offset++)
        {
            if (!cardioLoads.TryGetValue(endDate.AddDays(-offset), out var value)
                || value is null)
            {
                return false;
            }

            values.Add(value.Value);
        }

        average = values.Average();
        return true;
    }
}
