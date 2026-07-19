using FitbitMetrics.Application.Interfaces;
using FitbitMetrics.Application.Models;
using FitbitMetrics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FitbitMetrics.Infrastructure.Services;

internal sealed class DemoSeedService(FitbitDbContext dbContext) : IDemoSeedService
{
    public async Task<int> SeedAsync(int dayCount, CancellationToken cancellationToken = default)
    {
        if (dayCount <= 0 || dayCount > 90)
            throw new ArgumentOutOfRangeException(nameof(dayCount), "Day count must be between 1 and 90.");

        var today    = DateOnly.FromDateTime(DateTime.UtcNow);
        var inserted = 0;

        for (var i = 0; i < dayCount; i++)
        {
            var date = today.AddDays(-i);

            var exists = await dbContext.DailyMetricSnapshots
                .AnyAsync(s => s.UserKey == DemoUser.Key && s.MetricDate == date, cancellationToken);

            if (exists) continue;

            // Deterministic seed per calendar date — same run always produces the same values.
            var rng = new Random(date.DayNumber);

            dbContext.DailyMetricSnapshots.Add(new DailyMetricSnapshot
            {
                UserKey              = DemoUser.Key,
                MetricDate           = date,
                RestingHeartRateBpm  = rng.Next(52, 68),
                HrvRmssdMilliseconds = Math.Round((decimal)(rng.NextDouble() * 35 + 30), 1),
                Vo2MaxMlKgMin        = rng.Next(0, 8) == 0 ? null : Math.Round((decimal)(rng.NextDouble() * 14 + 42), 1),
                ConsumedCaloriesKcal = rng.Next(1700, 2600),
                CarbohydratesGrams   = Math.Round((decimal)(rng.NextDouble() * 100 + 200), 1),
                FatGrams             = Math.Round((decimal)(rng.NextDouble() * 40 + 55), 1),
                ProteinGrams         = Math.Round((decimal)(rng.NextDouble() * 50 + 80), 1),
                FiberGrams           = Math.Round((decimal)(rng.NextDouble() * 15 + 15), 1),
                SodiumMilligrams     = Math.Round((decimal)(rng.NextDouble() * 1000 + 1500), 1),
                CapturedAtUtc        = DateTimeOffset.UtcNow
            });

            inserted++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return inserted;
    }
}
