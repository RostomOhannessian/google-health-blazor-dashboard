namespace FitbitMetrics.Application.Models;

public sealed class DailyMetricSnapshot
{
    public int Id { get; set; }

    public string UserKey { get; set; } = DemoUser.Key;

    public DateOnly MetricDate { get; set; }

    public int? RestingHeartRateBpm { get; set; }

    public decimal? HrvRmssdMilliseconds { get; set; }

    public decimal? Vo2MaxMlKgMin { get; set; }

    public int? ConsumedCaloriesKcal { get; set; }

    public decimal? CarbohydratesGrams { get; set; }

    public decimal? FatGrams { get; set; }

    public decimal? ProteinGrams { get; set; }

    public decimal? FiberGrams { get; set; }

    public decimal? SodiumMilligrams { get; set; }

    public decimal? PotassiumMilligrams { get; set; }

    public decimal? CalciumMilligrams { get; set; }

    public decimal? IronMilligrams { get; set; }

    public DateTimeOffset CapturedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
