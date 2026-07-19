namespace HealthMetrics.Application.Models;

public sealed record SyncResult(
    int RequestedDays,
    int PersistedDays,
    DateTimeOffset CompletedAtUtc);
