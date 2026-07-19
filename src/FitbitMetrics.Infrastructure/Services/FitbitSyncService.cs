using FitbitMetrics.Application.Interfaces;
using FitbitMetrics.Application.Models;
using FitbitMetrics.Infrastructure.Clients;
using FitbitMetrics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FitbitMetrics.Infrastructure.Services;

internal sealed class FitbitSyncService(
    FitbitDbContext dbContext,
    IFitbitOAuthService fitbitOAuthService,
    FitbitApiClient fitbitApiClient) : IFitbitSyncService
{
    public async Task<SyncResult> SyncRecentDaysAsync(int dayCount, CancellationToken cancellationToken = default)
    {
        if (dayCount <= 0 || dayCount > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(dayCount), "Day count must be between 1 and 90.");
        }

        var historyEntry = new SyncHistoryEntry
        {
            RequestedDays = dayCount,
            StartedAtUtc  = DateTimeOffset.UtcNow
        };
        dbContext.SyncHistory.Add(historyEntry);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var accessToken = await fitbitOAuthService.GetValidAccessTokenAsync(cancellationToken);

            var startDate    = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-(dayCount - 1));
            var persistedDays = 0;

            for (var dayIndex = 0; dayIndex < dayCount; dayIndex++)
            {
                var date            = startDate.AddDays(dayIndex);
                var fetchedSnapshot = await fitbitApiClient.FetchMetricsForDateAsync(accessToken, date, cancellationToken);

                var existingSnapshot = await dbContext.DailyMetricSnapshots
                    .SingleOrDefaultAsync(
                        item => item.UserKey == DemoUser.Key && item.MetricDate == date,
                        cancellationToken);

                if (existingSnapshot is null)
                    dbContext.DailyMetricSnapshots.Add(fetchedSnapshot);
                else
                    Merge(existingSnapshot, fetchedSnapshot);

                persistedDays++;
            }

            var connection = await dbContext.FitbitConnections
                .SingleOrDefaultAsync(item => item.UserKey == DemoUser.Key, cancellationToken);

            if (connection is not null)
            {
                connection.LastSuccessfulSyncAtUtc = DateTimeOffset.UtcNow;
                connection.UpdatedAtUtc            = DateTimeOffset.UtcNow;
            }

            historyEntry.CompletedAtUtc = DateTimeOffset.UtcNow;
            historyEntry.PersistedDays  = persistedDays;
            historyEntry.Outcome        = SyncOutcome.Success;

            await dbContext.SaveChangesAsync(cancellationToken);

            return new SyncResult(dayCount, persistedDays, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            historyEntry.CompletedAtUtc = DateTimeOffset.UtcNow;
            historyEntry.Outcome        = SyncOutcome.Failed;
            historyEntry.ErrorMessage   = ex.Message;
            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private static void Merge(DailyMetricSnapshot target, DailyMetricSnapshot source)
    {
        target.RestingHeartRateBpm    = source.RestingHeartRateBpm;
        target.HrvRmssdMilliseconds   = source.HrvRmssdMilliseconds;
        target.Vo2MaxMlKgMin          = source.Vo2MaxMlKgMin;
        target.ConsumedCaloriesKcal   = source.ConsumedCaloriesKcal;
        target.CarbohydratesGrams     = source.CarbohydratesGrams;
        target.FatGrams               = source.FatGrams;
        target.ProteinGrams           = source.ProteinGrams;
        target.FiberGrams             = source.FiberGrams;
        target.SodiumMilligrams       = source.SodiumMilligrams;
        target.PotassiumMilligrams    = source.PotassiumMilligrams;
        target.CalciumMilligrams      = source.CalciumMilligrams;
        target.IronMilligrams         = source.IronMilligrams;
        target.CapturedAtUtc          = source.CapturedAtUtc;
    }
}
