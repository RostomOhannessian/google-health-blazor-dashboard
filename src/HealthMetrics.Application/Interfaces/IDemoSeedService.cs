namespace HealthMetrics.Application.Interfaces;

public interface IDemoSeedService
{
    /// <summary>
    /// Seeds <paramref name="dayCount"/> days of synthetic Google Health-like snapshots,
    /// skipping any date that already has data. Returns the number of newly inserted records.
    /// </summary>
    Task<int> SeedAsync(int dayCount, CancellationToken cancellationToken = default);
}
