namespace HealthMetrics.Application.Models;

public sealed class SyncHistoryEntry
{
    public int Id { get; set; }

    public string UserKey { get; set; } = LocalUser.Key;

    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public SyncOutcome Outcome { get; set; }

    public int RequestedDays { get; set; }

    public int PersistedDays { get; set; }

    public string? ErrorMessage { get; set; }
}

public enum SyncOutcome
{
    Success,
    Failed,
    PartialSuccess
}
