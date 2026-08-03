namespace HealthMetrics.Application.Models;

public enum TrendDirection { Up, Down, Stable, Insufficient }

public sealed record MetricSummary(
    int? LatestRestingHeartRateBpm,
    double? AvgRestingHeartRateBpm7d,
    TrendDirection HeartRateTrend,
    decimal? LatestHrvRmssd,
    double? AvgHrvRmssd7d,
    TrendDirection HrvTrend,
    decimal? LatestCardioLoad,
    decimal? LatestTargetLoadMin,
    decimal? LatestTargetLoadMax,
    decimal? LatestAcwr,
    AcwrStatus LatestAcwrStatus,
    decimal? LatestActiveZoneMinutes,
    decimal? LatestActiveZoneMinutesAcwr,
    AcwrStatus LatestActiveZoneMinutesAcwrStatus)
{
    public static MetricSummary From(IReadOnlyList<DailyMetricSnapshot> snapshots)
    {
        var ordered = snapshots.OrderByDescending(s => s.MetricDate).ToList();
        var latest = ordered.FirstOrDefault();

        return new MetricSummary(
            LatestRestingHeartRateBpm: latest?.RestingHeartRateBpm,
            AvgRestingHeartRateBpm7d: Avg(ordered.Take(7).Select(s => (double?)s.RestingHeartRateBpm)),
            HeartRateTrend: Trend(
                ordered.Take(7).Select(s => (double?)s.RestingHeartRateBpm),
                ordered.Skip(7).Take(7).Select(s => (double?)s.RestingHeartRateBpm)),
            LatestHrvRmssd: latest?.HrvRmssdMilliseconds,
            AvgHrvRmssd7d: Avg(ordered.Take(7).Select(s => (double?)s.HrvRmssdMilliseconds)),
            HrvTrend: Trend(
                ordered.Take(7).Select(s => (double?)s.HrvRmssdMilliseconds),
                ordered.Skip(7).Take(7).Select(s => (double?)s.HrvRmssdMilliseconds)),
            LatestCardioLoad: latest?.CardioLoad,
            LatestTargetLoadMin: latest?.TargetLoadMin,
            LatestTargetLoadMax: latest?.TargetLoadMax,
            LatestAcwr: latest?.Acwr,
            LatestAcwrStatus: AcwrCalculator.GetStatus(latest?.Acwr),
            LatestActiveZoneMinutes: latest?.ActiveZoneMinutes,
            LatestActiveZoneMinutesAcwr: latest?.ActiveZoneMinutesAcwr,
            LatestActiveZoneMinutesAcwrStatus: AcwrCalculator.GetStatus(latest?.ActiveZoneMinutesAcwr));
    }

    private static double? Avg(IEnumerable<double?> values)
    {
        var nonNull = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return nonNull.Count == 0 ? null : nonNull.Average();
    }

    private static TrendDirection Trend(IEnumerable<double?> recent, IEnumerable<double?> previous)
    {
        var recentAvg = Avg(recent);
        var previousAvg = Avg(previous);

        if (recentAvg is null || previousAvg is null || previousAvg == 0)
            return TrendDirection.Insufficient;

        var changePct = (recentAvg.Value - previousAvg.Value) / previousAvg.Value;
        return changePct switch
        {
            > 0.03 => TrendDirection.Up,
            < -0.03 => TrendDirection.Down,
            _ => TrendDirection.Stable
        };
    }
}
