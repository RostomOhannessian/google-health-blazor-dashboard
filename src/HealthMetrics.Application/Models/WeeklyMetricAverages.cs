namespace HealthMetrics.Application.Models;

public sealed record WeeklyMetricAverages(
    DateOnly WeekStart,
    decimal? RestingHeartRateBpm,
    decimal? HrvRmssdMilliseconds,
    decimal? DailyVo2MaxMlKgMin,
    decimal? RunVo2MaxMlKgMin,
    decimal? CardioLoad,
    decimal? TargetLoad,
    decimal? Acwr,
    decimal? SleepEfficiency,
    decimal? ConsumedCaloriesKcal,
    decimal? CarbohydratesGrams,
    decimal? FatGrams,
    decimal? ProteinGrams)
{
    public static WeeklyMetricAverages From(
        DateOnly weekStart,
        IEnumerable<DailyMetricSnapshot> snapshots)
    {
        var values = snapshots.ToList();
        return new WeeklyMetricAverages(
            weekStart,
            Average(values.Select(snapshot => (decimal?)snapshot.RestingHeartRateBpm)),
            Average(values.Select(snapshot => snapshot.HrvRmssdMilliseconds)),
            Average(values.Select(snapshot => snapshot.DailyVo2MaxMlKgMin)),
            Average(values.Select(snapshot => snapshot.RunVo2MaxMlKgMin)),
            Average(values.Select(snapshot => snapshot.CardioLoad)),
            Average(values.Select(snapshot => snapshot.TargetLoad)),
            Average(values.Select(snapshot => snapshot.Acwr)),
            Average(values.Select(snapshot => snapshot.SleepEfficiency)),
            Average(values.Select(snapshot => (decimal?)snapshot.ConsumedCaloriesKcal)),
            Average(values.Select(snapshot => snapshot.CarbohydratesGrams)),
            Average(values.Select(snapshot => snapshot.FatGrams)),
            Average(values.Select(snapshot => snapshot.ProteinGrams)));
    }

    private static decimal? Average(IEnumerable<decimal?> values)
    {
        var nonNullValues = values
            .Where(value => value.HasValue)
            .Select(value => value.GetValueOrDefault())
            .ToList();

        return nonNullValues.Count == 0 ? null : nonNullValues.Average();
    }
}
