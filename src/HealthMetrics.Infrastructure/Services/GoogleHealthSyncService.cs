using HealthMetrics.Application.Interfaces;
using HealthMetrics.Application.Models;
using HealthMetrics.Infrastructure.Clients;
using HealthMetrics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HealthMetrics.Infrastructure.Services;

internal sealed class GoogleHealthSyncService(
    HealthMetricsDbContext dbContext,
    IHealthAuthorizationService healthAuthorizationService,
    GoogleHealthApiClient googleHealthApiClient) : IHealthSyncService
{
    public async Task<SyncResult> SyncRecentDaysAsync(int dayCount, CancellationToken cancellationToken = default)
    {
        if (dayCount <= 0 || dayCount > 90)
            throw new ArgumentOutOfRangeException(nameof(dayCount), "Day count must be between 1 and 90.");

        var historyEntry = new SyncHistoryEntry
        {
            RequestedDays = dayCount,
            StartedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.SyncHistory.Add(historyEntry);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var accessToken = await healthAuthorizationService.GetValidAccessTokenAsync(cancellationToken);
            var endDate = DateOnly.FromDateTime(DateTime.UtcNow);
            var startDate = endDate.AddDays(-(dayCount - 1));
            var fetchedSnapshots = await googleHealthApiClient.FetchDailyMetricsAsync(accessToken, startDate, endDate, cancellationToken);
            var persistedDays = 0;

            foreach (var fetchedSnapshot in fetchedSnapshots)
            {
                var existingSnapshot = await dbContext.DailyMetricSnapshots
                    .SingleOrDefaultAsync(
                        item => item.UserKey == LocalUser.Key && item.MetricDate == fetchedSnapshot.MetricDate,
                        cancellationToken);

                if (existingSnapshot is null)
                    dbContext.DailyMetricSnapshots.Add(fetchedSnapshot);
                else
                    Merge(existingSnapshot, fetchedSnapshot);

                persistedDays++;
            }

            var connection = await dbContext.HealthConnections
                .SingleOrDefaultAsync(item => item.UserKey == LocalUser.Key, cancellationToken);

            if (connection is not null)
            {
                connection.LastSuccessfulSyncAtUtc = DateTimeOffset.UtcNow;
                connection.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }

            historyEntry.CompletedAtUtc = DateTimeOffset.UtcNow;
            historyEntry.PersistedDays = persistedDays;
            historyEntry.Outcome = SyncOutcome.Success;

            await dbContext.SaveChangesAsync(cancellationToken);

            return new SyncResult(dayCount, persistedDays, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            historyEntry.CompletedAtUtc = DateTimeOffset.UtcNow;
            historyEntry.Outcome = SyncOutcome.Failed;
            historyEntry.ErrorMessage = ex.Message;
            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private static void Merge(DailyMetricSnapshot target, DailyMetricSnapshot source)
    {
        target.RestingHeartRateBpm = source.RestingHeartRateBpm;
        target.HrvRmssdMilliseconds = source.HrvRmssdMilliseconds;
        target.RunVo2MaxMlKgMin = source.RunVo2MaxMlKgMin;
        target.ConsumedCaloriesKcal = source.ConsumedCaloriesKcal;
        target.CarbohydratesGrams = source.CarbohydratesGrams;
        target.FatGrams = source.FatGrams;
        target.ProteinGrams = source.ProteinGrams;
        target.CapturedAtUtc = source.CapturedAtUtc;
    }
}
