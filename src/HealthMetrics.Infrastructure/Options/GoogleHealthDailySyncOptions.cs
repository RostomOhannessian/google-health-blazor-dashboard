namespace HealthMetrics.Infrastructure.Options;

public sealed class GoogleHealthDailySyncOptions
{
    public const string SectionName = "GoogleHealthDailySync";

    /// <summary>Enable the background daily sync service.</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>UTC hour of day (0–23) at which the daily sync runs.</summary>
    public int SyncHourUtc { get; init; } = 6;

    /// <summary>Number of days to include in each automatic sync (1–90).</summary>
    public int DaysToSync { get; init; } = 7;
}
