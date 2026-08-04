using HealthMetrics.Application.Models;

namespace HealthMetrics.Application.Interfaces;

public interface IHealthSyncService
{
    Task<SyncResult> SyncRecentDaysAsync(int dayCount, CancellationToken cancellationToken = default);
}
