namespace HealthMetrics.Infrastructure.Options;

public sealed class GoogleHealthHttpLoggingOptions
{
    public const string SectionName = "GoogleHealthHttpLogging";

    public bool LogRequestBodies { get; init; } = true;

    public bool LogResponseBodies { get; init; }

    public int MaxBodyCharacters { get; init; } = 4096;
}
