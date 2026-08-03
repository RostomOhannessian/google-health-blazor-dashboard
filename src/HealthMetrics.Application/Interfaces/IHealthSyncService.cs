using HealthMetrics.Application.Models;

namespace HealthMetrics.Application.Interfaces;

public interface IHealthSyncService
{
    Task<SyncResult> SyncRecentDaysAsync(int dayCount, CancellationToken cancellationToken = default);

    Task<SyncResult> SyncDateRangeAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);
}
