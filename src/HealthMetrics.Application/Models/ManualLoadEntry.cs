namespace HealthMetrics.Application.Models;

public sealed record ManualLoadEntry(
    DateOnly MetricDate,
    decimal? CardioLoad,
    decimal? TargetLoadMin,
    decimal? TargetLoadMax);

public sealed record ManualLoadEntryResult(bool Succeeded, string? ErrorMessage = null)
{
    public static ManualLoadEntryResult Success { get; } = new(true);
}
