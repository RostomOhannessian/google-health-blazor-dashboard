using HealthMetrics.Application.Models;

namespace HealthMetrics.Tests.Models;

public sealed class AcwrCalculatorTests
{
    [Fact]
    public void Calculate_UsesCompleteCalendarWindowsAndRoundsToTwoDecimals()
    {
        var endDate = new DateOnly(2026, 1, 28);
        var loads = Enumerable.Range(0, 28)
            .ToDictionary(
                offset => endDate.AddDays(-offset),
                offset => (decimal?)(offset < 7 ? 101 : 100));

        var acwr = AcwrCalculator.Calculate(loads, endDate);

        Assert.Equal(1.01m, acwr);
    }

    [Fact]
    public void Calculate_ReturnsNullWhenAnAcuteOrChronicDayIsMissing()
    {
        var endDate = new DateOnly(2026, 1, 28);
        var loads = Enumerable.Range(0, 28)
            .Where(offset => offset != 10)
            .ToDictionary(
                offset => endDate.AddDays(-offset),
                offset => (decimal?)100);

        Assert.Null(AcwrCalculator.Calculate(loads, endDate));
    }

    [Fact]
    public void Calculate_ReturnsNullWhenChronicLoadIsZero()
    {
        var endDate = new DateOnly(2026, 1, 28);
        var loads = Enumerable.Range(0, 28)
            .ToDictionary(
                offset => endDate.AddDays(-offset),
                offset => (decimal?)0);

        Assert.Null(AcwrCalculator.Calculate(loads, endDate));
    }

    [Theory]
    [InlineData(null, AcwrStatus.Insufficient)]
    [InlineData(0.79, AcwrStatus.Undertraining)]
    [InlineData(0.8, AcwrStatus.OptimalZone)]
    [InlineData(1.3, AcwrStatus.OptimalZone)]
    [InlineData(1.31, AcwrStatus.Overreaching)]
    [InlineData(1.5, AcwrStatus.Overreaching)]
    [InlineData(1.51, AcwrStatus.HighDangerZone)]
    public void GetStatus_UsesDocumentedThresholds(double? acwr, AcwrStatus expected)
    {
        Assert.Equal(expected, AcwrCalculator.GetStatus((decimal?)acwr));
    }

    [Fact]
    public void Recalculate_ClearsRatiosForIncompleteRows()
    {
        var endDate = new DateOnly(2026, 1, 28);
        var snapshots = Enumerable.Range(0, 28)
            .Select(offset => new DailyMetricSnapshot
            {
                MetricDate = endDate.AddDays(-offset),
                CardioLoad = 100,
                Acwr = 1.2m
            })
            .ToList();

        snapshots.RemoveAt(10);
        AcwrCalculator.Recalculate(snapshots);

        Assert.All(snapshots, snapshot => Assert.Null(snapshot.Acwr));
    }
}
