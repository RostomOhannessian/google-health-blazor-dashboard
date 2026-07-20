namespace HealthMetrics.Application.Models;

public sealed class DailyMetricSnapshot
{
    public int Id { get; set; }

    public string UserKey { get; set; } = LocalUser.Key;

    public DateOnly MetricDate { get; set; }

    public int? RestingHeartRateBpm { get; set; }

    public decimal? HrvRmssdMilliseconds { get; set; }

    public decimal? DailyVo2MaxMlKgMin { get; set; }

    public decimal? RunVo2MaxMlKgMin { get; set; }

    public int? ConsumedCaloriesKcal { get; set; }

    public decimal? CarbohydratesGrams { get; set; }

    public decimal? FatGrams { get; set; }

    public decimal? ProteinGrams { get; set; }

    public DateTimeOffset CapturedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
