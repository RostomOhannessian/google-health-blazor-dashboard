using HealthMetrics.Application.Models;
using HealthMetrics.Infrastructure.Persistence;

namespace HealthMetrics.Tests.Models;

public sealed class MetricSummaryTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static DailyMetricSnapshot Snapshot(DateOnly date, int hr = 60, decimal hrv = 50m) =>
        new()
        {
            UserKey              = LocalUser.Key,
            MetricDate           = date,
            RestingHeartRateBpm  = hr,
            HrvRmssdMilliseconds = hrv,
        };

    // Build a list of N snapshots with uniformly fixed HR and HRV values.
    private static List<DailyMetricSnapshot> Build(int count, int startDayNumber, int hr = 60, decimal hrv = 50m) =>
        Enumerable.Range(0, count)
                  .Select(i => Snapshot(DateOnly.FromDayNumber(startDayNumber + i), hr, hrv))
                  .ToList();

    // ── Empty / insufficient data ─────────────────────────────────────────────

    [Fact]
    public void From_EmptyList_ReturnsAllNullsAndInsufficientTrends()
    {
        var summary = MetricSummary.From([]);

        Assert.Null(summary.LatestRestingHeartRateBpm);
        Assert.Null(summary.AvgRestingHeartRateBpm7d);
        Assert.Equal(TrendDirection.Insufficient, summary.HeartRateTrend);
        Assert.Null(summary.LatestHrvRmssd);
        Assert.Null(summary.AvgHrvRmssd7d);
        Assert.Equal(TrendDirection.Insufficient, summary.HrvTrend);
    }

    [Fact]
    public void From_SevenOrFewerSnapshots_NoComparisonWindowReturnsInsufficientTrend()
    {
        // Only 7 snapshots → Skip(7) is empty → prior-window avg is null → Insufficient
        var snapshots = Build(7, startDayNumber: 1000);
        var summary   = MetricSummary.From(snapshots);

        Assert.Equal(TrendDirection.Insufficient, summary.HeartRateTrend);
        Assert.Equal(TrendDirection.Insufficient, summary.HrvTrend);
    }

    [Fact]
    public void From_EightToThirteenSnapshots_PartialComparisonWindowYieldsTrend()
    {
        // 13 snapshots all at hr=60 → recent 7 avg = 60, prior 6 avg = 60 → Stable (< 3% change)
        var snapshots = Build(13, startDayNumber: 1000, hr: 60);
        var summary   = MetricSummary.From(snapshots);

        Assert.Equal(TrendDirection.Stable, summary.HeartRateTrend);
    }

    [Fact]
    public void From_ExactlyOneSnapshot_ReturnsThatValueAsLatestAndInsufficientTrend()
    {
        var snapshot = Snapshot(new DateOnly(2025, 1, 1), hr: 62, hrv: 45m);
        var summary  = MetricSummary.From([snapshot]);

        Assert.Equal(62, summary.LatestRestingHeartRateBpm);
        Assert.Equal(62.0, summary.AvgRestingHeartRateBpm7d);
        Assert.Equal(45m, summary.LatestHrvRmssd);
        Assert.Equal(TrendDirection.Insufficient, summary.HeartRateTrend);
    }

    // ── Latest value and 7-day average ────────────────────────────────────────

    [Fact]
    public void From_MultipleSnapshots_LatestIsTheMostRecent()
    {
        var older   = Snapshot(new DateOnly(2025, 1, 1), hr: 55, hrv: 40m);
        var recent  = Snapshot(new DateOnly(2025, 1, 10), hr: 70, hrv: 60m);
        var summary = MetricSummary.From([older, recent]);

        Assert.Equal(70, summary.LatestRestingHeartRateBpm);
        Assert.Equal(60m, summary.LatestHrvRmssd);
    }

    [Fact]
    public void From_SevenSnapshots_AverageEqualsMeanOfAllSeven()
    {
        // HR values 61–67 (mean = 64), all fall within the 7-day window
        var snapshots = Enumerable.Range(0, 7)
                                  .Select(i => Snapshot(new DateOnly(2025, 1, 1).AddDays(i), hr: 61 + i))
                                  .ToList();
        var summary = MetricSummary.From(snapshots);

        Assert.Equal(64.0, summary.AvgRestingHeartRateBpm7d);
    }

    // ── Trend direction ───────────────────────────────────────────────────────

    [Fact]
    public void From_HrIncreaseMoreThan3Pct_ReturnsTrendUp()
    {
        // Recent 7 days: hr=65; previous 7 days: hr=60 → +8.3%
        var previous = Build(7, startDayNumber: 1000, hr: 60);
        var recent   = Build(7, startDayNumber: 1010, hr: 65);
        var summary  = MetricSummary.From([.. previous, .. recent]);

        Assert.Equal(TrendDirection.Up, summary.HeartRateTrend);
    }

    [Fact]
    public void From_HrDecreaseMoreThan3Pct_ReturnsTrendDown()
    {
        // Recent 7 days: hr=60; previous 7 days: hr=65 → -7.7%
        var previous = Build(7, startDayNumber: 1000, hr: 65);
        var recent   = Build(7, startDayNumber: 1010, hr: 60);
        var summary  = MetricSummary.From([.. previous, .. recent]);

        Assert.Equal(TrendDirection.Down, summary.HeartRateTrend);
    }

    [Fact]
    public void From_HrChangeWithin3Pct_ReturnsTrendStable()
    {
        // Recent 7 days: hr=61; previous 7 days: hr=60 → +1.67%
        var previous = Build(7, startDayNumber: 1000, hr: 60);
        var recent   = Build(7, startDayNumber: 1010, hr: 61);
        var summary  = MetricSummary.From([.. previous, .. recent]);

        Assert.Equal(TrendDirection.Stable, summary.HeartRateTrend);
    }

    [Fact]
    public void From_HrvIncreaseMoreThan3Pct_ReturnsTrendUp()
    {
        // Recent 7 days: hrv=55; previous 7 days: hrv=50 → +10%
        var previous = Build(7, startDayNumber: 1000, hrv: 50m);
        var recent   = Build(7, startDayNumber: 1010, hrv: 55m);
        var summary  = MetricSummary.From([.. previous, .. recent]);

        Assert.Equal(TrendDirection.Up, summary.HrvTrend);
    }

    // ── Null HRV values ────────────────────────────────────────────────────────

    [Fact]
    public void From_AllNullHrv_ReturnsNullAverageAndInsufficientTrend()
    {
        var snapshots = Enumerable.Range(0, 14)
                                  .Select(i =>
                                  {
                                      var s = Snapshot(new DateOnly(2025, 1, 1).AddDays(i));
                                      s.HrvRmssdMilliseconds = null;
                                      return s;
                                  })
                                  .ToList();
        var summary = MetricSummary.From(snapshots);

        Assert.Null(summary.AvgHrvRmssd7d);
        Assert.Equal(TrendDirection.Insufficient, summary.HrvTrend);
    }

    [Fact]
    public void From_ExposesLatestLoadTargetAndAcwrStatus()
    {
        var snapshot = Snapshot(new DateOnly(2025, 1, 1));
        snapshot.CardioLoad = 78m;
        snapshot.TargetLoad = 75m;
        snapshot.Acwr = 1.05m;

        var summary = MetricSummary.From([snapshot]);

        Assert.Equal(78m, summary.LatestCardioLoad);
        Assert.Equal(75m, summary.LatestTargetLoad);
        Assert.Equal(1.05m, summary.LatestAcwr);
        Assert.Equal(AcwrStatus.OptimalZone, summary.LatestAcwrStatus);
    }
}
