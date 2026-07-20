using System.Diagnostics;
using HealthMetrics.Application.Interfaces;
using HealthMetrics.Application.Models;
using HealthMetrics.Infrastructure.Clients;
using HealthMetrics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthMetrics.Infrastructure.Services;

internal sealed class GoogleHealthSyncService(
    HealthMetricsDbContext dbContext,
    IHealthAuthorizationService healthAuthorizationService,
    GoogleHealthApiClient googleHealthApiClient,
    ILogger<GoogleHealthSyncService> logger) : IHealthSyncService
{
    public async Task<SyncResult> SyncRecentDaysAsync(int dayCount, CancellationToken cancellationToken = default)
    {
        if (dayCount <= 0 || dayCount > 90)
            throw new ArgumentOutOfRangeException(nameof(dayCount), "Day count must be between 1 and 90.");

        var stopwatch = Stopwatch.StartNew();
        var historyEntry = new SyncHistoryEntry
        {
            RequestedDays = dayCount,
            StartedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.SyncHistory.Add(historyEntry);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Google Health sync started. SyncHistoryEntryId: {SyncHistoryEntryId}; RequestedDays: {RequestedDays}.",
            historyEntry.Id,
            dayCount);

        try
        {
            var accessToken = await healthAuthorizationService.GetValidAccessTokenAsync(cancellationToken);
            var endDate = DateOnly.FromDateTime(DateTime.UtcNow);
            var startDate = endDate.AddDays(-(dayCount - 1));
            logger.LogInformation(
                "Google Health sync date range calculated. SyncHistoryEntryId: {SyncHistoryEntryId}; StartDate: {StartDate}; EndDate: {EndDate}.",
                historyEntry.Id,
                startDate,
                endDate);

            var fetchedSnapshots = await googleHealthApiClient.FetchDailyMetricsAsync(accessToken, startDate, endDate, cancellationToken);
            var persistedDays = 0;
            var insertedDays = 0;
            var updatedDays = 0;
            var daysWithMetricValues = 0;

            foreach (var fetchedSnapshot in fetchedSnapshots)
            {
                if (HasAnyMetricValue(fetchedSnapshot))
                    daysWithMetricValues++;

                var existingSnapshot = await dbContext.DailyMetricSnapshots
                    .SingleOrDefaultAsync(
                        item => item.UserKey == LocalUser.Key && item.MetricDate == fetchedSnapshot.MetricDate,
                        cancellationToken);

                if (existingSnapshot is null)
                {
                    dbContext.DailyMetricSnapshots.Add(fetchedSnapshot);
                    insertedDays++;
                }
                else
                {
                    Merge(existingSnapshot, fetchedSnapshot);
                    updatedDays++;
                }

                persistedDays++;
            }

            if (daysWithMetricValues == 0)
            {
                logger.LogWarning(
                    "Google Health sync completed without metric values. SyncHistoryEntryId: {SyncHistoryEntryId}; RequestedDays: {RequestedDays}; StartDate: {StartDate}; EndDate: {EndDate}.",
                    historyEntry.Id,
                    dayCount,
                    startDate,
                    endDate);
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
            stopwatch.Stop();

            logger.LogInformation(
                "Google Health sync completed. SyncHistoryEntryId: {SyncHistoryEntryId}; RequestedDays: {RequestedDays}; PersistedDays: {PersistedDays}; InsertedDays: {InsertedDays}; UpdatedDays: {UpdatedDays}; DaysWithMetricValues: {DaysWithMetricValues}; ElapsedMs: {ElapsedMs}.",
                historyEntry.Id,
                dayCount,
                persistedDays,
                insertedDays,
                updatedDays,
                daysWithMetricValues,
                stopwatch.ElapsedMilliseconds);

            return new SyncResult(dayCount, persistedDays, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            historyEntry.CompletedAtUtc = DateTimeOffset.UtcNow;
            historyEntry.Outcome = SyncOutcome.Failed;
            historyEntry.ErrorMessage = ex.Message;
            await dbContext.SaveChangesAsync(CancellationToken.None);

            logger.LogError(
                ex,
                "Google Health sync failed. SyncHistoryEntryId: {SyncHistoryEntryId}; RequestedDays: {RequestedDays}; ElapsedMs: {ElapsedMs}.",
                historyEntry.Id,
                dayCount,
                stopwatch.ElapsedMilliseconds);

            throw;
        }
    }

    private static void Merge(DailyMetricSnapshot target, DailyMetricSnapshot source)
    {
        target.RestingHeartRateBpm = source.RestingHeartRateBpm;
        target.HrvRmssdMilliseconds = source.HrvRmssdMilliseconds;
        target.DailyVo2MaxMlKgMin = source.DailyVo2MaxMlKgMin;
        target.RunVo2MaxMlKgMin = source.RunVo2MaxMlKgMin;
        target.ConsumedCaloriesKcal = source.ConsumedCaloriesKcal;
        target.CarbohydratesGrams = source.CarbohydratesGrams;
        target.FatGrams = source.FatGrams;
        target.ProteinGrams = source.ProteinGrams;
        target.CapturedAtUtc = source.CapturedAtUtc;
    }

    private static bool HasAnyMetricValue(DailyMetricSnapshot snapshot) =>
        snapshot.RestingHeartRateBpm is not null
        || snapshot.HrvRmssdMilliseconds is not null
        || snapshot.DailyVo2MaxMlKgMin is not null
        || snapshot.RunVo2MaxMlKgMin is not null
        || snapshot.ConsumedCaloriesKcal is not null
        || snapshot.CarbohydratesGrams is not null
        || snapshot.FatGrams is not null
        || snapshot.ProteinGrams is not null;
}
