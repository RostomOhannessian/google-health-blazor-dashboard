namespace FitbitMetrics.Application.Interfaces;

public interface IDemoSeedService
{
    /// <summary>
    /// Seeds <paramref name="dayCount"/> days of synthetic Fitbit-like snapshots,
    /// skipping any date that already has data. Returns the number of newly inserted records.
    /// </summary>
    Task<int> SeedAsync(int dayCount, CancellationToken cancellationToken = default);
}
