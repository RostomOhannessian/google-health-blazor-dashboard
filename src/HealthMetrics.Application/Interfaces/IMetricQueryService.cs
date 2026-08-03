using HealthMetrics.Application.Models;

namespace HealthMetrics.Application.Interfaces;

public interface IMetricQueryService
{
    Task<IReadOnlyList<DailyMetricSnapshot>> GetRecentMetricsAsync(int dayCount, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DailyMetricSnapshot>> GetAllMetricsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SyncHistoryEntry>> GetRecentSyncHistoryAsync(int count = 10, CancellationToken cancellationToken = default);
}
