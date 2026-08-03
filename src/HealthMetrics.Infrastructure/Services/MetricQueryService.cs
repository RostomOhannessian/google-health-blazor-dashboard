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
        if (dayCount <= 0 || dayCount > 366)
        {
            throw new ArgumentOutOfRangeException(nameof(dayCount), "Day count must be between 1 and 366.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cutoff = today.AddDays(1 - dayCount);

        var snapshots = await dbContext.DailyMetricSnapshots
            .AsNoTracking()
            .Where(item => item.UserKey == LocalUser.Key && item.MetricDate >= cutoff)
            .OrderByDescending(item => item.MetricDate)
            .ToListAsync(cancellationToken);

        return await ProjectWeeklyTargetsAsync(snapshots, cancellationToken);
    }

    public async Task<IReadOnlyList<DailyMetricSnapshot>> GetAllMetricsAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshots = await dbContext.DailyMetricSnapshots
            .AsNoTracking()
            .Where(item => item.UserKey == LocalUser.Key)
            .OrderByDescending(item => item.MetricDate)
            .ToListAsync(cancellationToken);

        return await ProjectWeeklyTargetsAsync(snapshots, cancellationToken);
    }

    private async Task<IReadOnlyList<DailyMetricSnapshot>> ProjectWeeklyTargetsAsync(
        List<DailyMetricSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        if (snapshots.Count == 0)
            return snapshots;

        var earliestWeekStart = WeeklyLoadCalculator.GetWeekStart(snapshots.Min(item => item.MetricDate));
        var latestDate = snapshots.Max(item => item.MetricDate);
        var weeklyTargets = await dbContext.DailyMetricSnapshots
            .AsNoTracking()
            .Where(item =>
                item.UserKey == LocalUser.Key
                && item.MetricDate >= earliestWeekStart
                && item.MetricDate <= latestDate
                && item.TargetLoad.HasValue)
            .OrderBy(item => item.MetricDate)
            .Select(item => new { item.MetricDate, item.TargetLoad })
            .ToListAsync(cancellationToken);
        var targetByWeek = weeklyTargets
            .GroupBy(item => WeeklyLoadCalculator.GetWeekStart(item.MetricDate))
            .ToDictionary(
                group => group.Key,
                group => group.Last().TargetLoad);

        foreach (var snapshot in snapshots)
        {
            if (targetByWeek.TryGetValue(
                    WeeklyLoadCalculator.GetWeekStart(snapshot.MetricDate),
                    out var weeklyTarget))
            {
                snapshot.TargetLoad = weeklyTarget;
            }
        }

        return snapshots;
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
