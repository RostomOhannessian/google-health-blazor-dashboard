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

        var snapshot = await dbContext.DailyMetricSnapshots.SingleOrDefaultAsync(
            item => item.UserKey == LocalUser.Key && item.MetricDate == entry.MetricDate,
            cancellationToken);

        if (snapshot is null)
        {
            snapshot = new DailyMetricSnapshot
            {
                UserKey = LocalUser.Key,
                MetricDate = entry.MetricDate
            };
            dbContext.DailyMetricSnapshots.Add(snapshot);
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
    }

    private static string? Validate(ManualLoadEntry entry)
    {
        if (entry.CardioLoad is < 0)
            return "Manual Cardio Load cannot be negative.";
        if (entry.TargetLoad is < 0)
            return "Manual target cannot be negative.";

        return null;
    }
}
