using HealthMetrics.Application.Interfaces;
using HealthMetrics.Application.Models;
using HealthMetrics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HealthMetrics.Infrastructure.Services;

internal sealed class ManualLoadEntryService(HealthMetricsDbContext dbContext) : IManualLoadEntryService
{
    public async Task<ManualLoadEntryResult> SaveAsync(
        ManualLoadEntry entry,
        CancellationToken cancellationToken = default)
    {
        var validationError = Validate(entry);
        if (validationError is not null)
            return new ManualLoadEntryResult(false, validationError);

        return await SnapshotMutationCoordinator.RunAsync(async () =>
        {
            var weekStart = WeeklyLoadCalculator.GetWeekStart(entry.MetricDate);
            var weekEnd = weekStart.AddDays(6);
            var weeklySnapshots = await dbContext.DailyMetricSnapshots
                .Where(item =>
                    item.UserKey == LocalUser.Key
                    && item.MetricDate >= weekStart
                    && item.MetricDate <= weekEnd)
                .ToListAsync(cancellationToken);
            var snapshotsByDate = weeklySnapshots.ToDictionary(snapshot => snapshot.MetricDate);
            if (!snapshotsByDate.TryGetValue(entry.MetricDate, out var snapshot))
            {
                snapshot = new DailyMetricSnapshot
                {
                    UserKey = LocalUser.Key,
                    MetricDate = entry.MetricDate
                };
                dbContext.DailyMetricSnapshots.Add(snapshot);
                snapshotsByDate.Add(snapshot.MetricDate, snapshot);
            }

            if (entry.TargetLoad.HasValue)
            {
                for (var dayOffset = 0; dayOffset < 7; dayOffset++)
                {
                    var metricDate = weekStart.AddDays(dayOffset);
                    if (!snapshotsByDate.TryGetValue(metricDate, out var weeklySnapshot))
                    {
                        weeklySnapshot = new DailyMetricSnapshot
                        {
                            UserKey = LocalUser.Key,
                            MetricDate = metricDate
                        };
                        dbContext.DailyMetricSnapshots.Add(weeklySnapshot);
                        snapshotsByDate.Add(metricDate, weeklySnapshot);
                    }

                    weeklySnapshot.TargetLoad = entry.TargetLoad;
                }
            }
            else
            {
                foreach (var weeklySnapshot in snapshotsByDate.Values)
                    weeklySnapshot.TargetLoad = null;
            }

            snapshot.CardioLoad = entry.CardioLoad;
            snapshot.TargetLoad = entry.TargetLoad;

            await dbContext.SaveChangesAsync(cancellationToken);

            var snapshots = await dbContext.DailyMetricSnapshots
                .Where(item => item.UserKey == LocalUser.Key)
                .OrderBy(item => item.MetricDate)
                .ToListAsync(cancellationToken);
            AcwrCalculator.RecalculateManualCardioLoad(snapshots);
            await dbContext.SaveChangesAsync(cancellationToken);

            return ManualLoadEntryResult.Success;
        }, cancellationToken);
    }

    private static string? Validate(ManualLoadEntry entry)
    {
        if (entry.CardioLoad is < 0)
            return "Manual Cardio Load cannot be negative.";
        if (entry.TargetLoad is < 0)
            return "Weekly target cannot be negative.";

        return null;
    }
}
