using HealthMetrics.Application.Interfaces;
using HealthMetrics.Application.Models;
using HealthMetrics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HealthMetrics.Infrastructure.Services;

internal sealed class DemoSeedService(HealthMetricsDbContext dbContext) : IDemoSeedService
{
    public async Task<int> SeedAsync(int dayCount, CancellationToken cancellationToken = default)
    {
        if (dayCount <= 0 || dayCount > 90)
            throw new ArgumentOutOfRangeException(nameof(dayCount), "Day count must be between 1 and 90.");

        return await SnapshotMutationCoordinator.RunAsync(async () =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var inserted = 0;

            for (var i = 0; i < dayCount; i++)
            {
                var date = today.AddDays(-i);

                var exists = await dbContext.DailyMetricSnapshots
                    .AnyAsync(s => s.UserKey == LocalUser.Key && s.MetricDate == date, cancellationToken);

                if (exists) continue;

                // Deterministic seed per calendar date — same run always produces the same values.
                var rng = new Random(date.DayNumber);
                var weekStart = WeeklyLoadCalculator.GetWeekStart(date);
                var weeklyTarget = Math.Round((decimal)(new Random(weekStart.DayNumber).NextDouble() * 250 + 350), 1);
                var cardioLoad = Math.Round((decimal)(rng.NextDouble() * 55 + 45), 1);

                var snapshot = new DailyMetricSnapshot
                {
                    UserKey = LocalUser.Key,
                    MetricDate = date,
                    RestingHeartRateBpm = rng.Next(52, 68),
                    HrvRmssdMilliseconds = Math.Round((decimal)(rng.NextDouble() * 35 + 30), 1),
                    DailyVo2MaxMlKgMin = rng.Next(0, 8) == 0 ? null : Math.Round((decimal)(rng.NextDouble() * 14 + 42), 1),
                    RunVo2MaxMlKgMin = rng.Next(0, 8) == 0 ? null : Math.Round((decimal)(rng.NextDouble() * 14 + 42), 1),
                    CardioLoad = cardioLoad,
                    TargetLoad = weeklyTarget,
                    SleepEfficiency = Math.Round((decimal)(rng.NextDouble() * 15 + 82), 2),
                    DeepSleepMinutes = rng.Next(45, 111),
                    RemSleepMinutes = rng.Next(70, 151),
                    ConsumedCaloriesKcal = rng.Next(1700, 2600),
                    CarbohydratesGrams = Math.Round((decimal)(rng.NextDouble() * 100 + 200), 1),
                    FatGrams = Math.Round((decimal)(rng.NextDouble() * 40 + 55), 1),
                    ProteinGrams = Math.Round((decimal)(rng.NextDouble() * 50 + 80), 1),
                    CapturedAtUtc = DateTimeOffset.UtcNow
                };
                NutritionEnergyEstimator.UpdateEstimatedAlcoholGrams(snapshot);
                dbContext.DailyMetricSnapshots.Add(snapshot);

                inserted++;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            var snapshots = await dbContext.DailyMetricSnapshots
                .Where(snapshot => snapshot.UserKey == LocalUser.Key)
                .OrderBy(snapshot => snapshot.MetricDate)
                .ToListAsync(cancellationToken);
            AcwrCalculator.RecalculateManualCardioLoad(snapshots);
            await dbContext.SaveChangesAsync(cancellationToken);
            return inserted;
        }, cancellationToken);
    }
}
