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

    public decimal? CardioLoad { get; set; }

    public decimal? TargetLoadMin { get; set; }

    public decimal? TargetLoadMax { get; set; }

    public decimal? Acwr { get; set; }

    public decimal? ActiveZoneMinutes { get; set; }

    public decimal? ActiveZoneMinutesAcwr { get; set; }

    public decimal? SleepEfficiency { get; set; }

    public int? DeepSleepMinutes { get; set; }

    public int? RemSleepMinutes { get; set; }

    public int? ConsumedCaloriesKcal { get; set; }

    public decimal? CarbohydratesGrams { get; set; }

    public decimal? FatGrams { get; set; }

    public decimal? ProteinGrams { get; set; }

    public DateTimeOffset CapturedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
