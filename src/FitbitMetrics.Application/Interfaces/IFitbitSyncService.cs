using FitbitMetrics.Application.Models;

namespace FitbitMetrics.Application.Interfaces;

public interface IFitbitSyncService
{
    Task<SyncResult> SyncRecentDaysAsync(int dayCount, CancellationToken cancellationToken = default);
}
