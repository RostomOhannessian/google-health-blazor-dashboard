using HealthMetrics.Application.Interfaces;
using HealthMetrics.Application.Models;
using HealthMetrics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HealthMetrics.Infrastructure.Services;

internal sealed class MetricQueryService(HealthMetricsDbContext dbContext) : IMetricQueryService
{
    public async Task<IReadOnlyList<DailyMetricSnapshot>> GetRecentMetricsAsync(
        int dayCount,
        CancellationToken cancellationToken = default)
    {
        if (dayCount <= 0 || dayCount > 365)
        {
            throw new ArgumentOutOfRangeException(nameof(dayCount), "Day count must be between 1 and 365.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cutoff = today.AddDays(1 - dayCount);

        return await dbContext.DailyMetricSnapshots
            .AsNoTracking()
            .Where(item => item.UserKey == LocalUser.Key && item.MetricDate >= cutoff)
            .OrderByDescending(item => item.MetricDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SyncHistoryEntry>> GetRecentSyncHistoryAsync(
        int count = 10,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.SyncHistory
            .AsNoTracking()
            .Where(entry => entry.UserKey == LocalUser.Key)
            // SQLite cannot translate ordering by DateTimeOffset reliably. Sync history
            // receives its identity when a sync starts, so descending identity is the
            // database-safe equivalent of newest-first ordering.
            .OrderByDescending(entry => entry.Id)
            .Take(count)
            .ToListAsync(cancellationToken);
    }
}
