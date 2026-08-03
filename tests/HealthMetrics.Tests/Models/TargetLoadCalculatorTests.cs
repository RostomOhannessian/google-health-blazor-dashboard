using HealthMetrics.Application.Models;

namespace HealthMetrics.Tests.Models;

public sealed class TargetLoadCalculatorTests
{
    [Fact]
    public void Calculate_UsesUpToTwentyEightConsecutiveValuesAndRoundsToTwoDecimals()
    {
        var endDate = new DateOnly(2026, 1, 28);
        var loads = Enumerable.Range(0, 28)
            .ToDictionary(
                offset => endDate.AddDays(-offset),
                offset => (decimal?)(offset < 7 ? 101 : 100));

        var target = TargetLoadCalculator.Calculate(loads, endDate);

        Assert.Equal(new TargetLoadRange(80.2m, 120.3m), target);
    }

    [Fact]
    public void Calculate_ReturnsNullWhenFewerThanSevenConsecutiveValuesExist()
    {
        var endDate = new DateOnly(2026, 1, 28);
        var loads = Enumerable.Range(0, 7)
            .Where(offset => offset != 3)
            .ToDictionary(offset => endDate.AddDays(-offset), _ => (decimal?)100);

        Assert.Null(TargetLoadCalculator.Calculate(loads, endDate));
    }

    [Fact]
    public void Recalculate_ClearsStaleTargetsWhenTheBaselineIsZero()
    {
        var endDate = new DateOnly(2026, 1, 28);
        var snapshots = Enumerable.Range(0, 7)
            .Select(offset => new DailyMetricSnapshot
            {
                MetricDate = endDate.AddDays(-offset),
                CardioLoad = 0,
                TargetLoadMin = 80,
                TargetLoadMax = 120
            })
            .ToList();

        TargetLoadCalculator.Recalculate(snapshots);

        Assert.All(snapshots, snapshot =>
        {
            Assert.Null(snapshot.TargetLoadMin);
            Assert.Null(snapshot.TargetLoadMax);
        });
    }
}
