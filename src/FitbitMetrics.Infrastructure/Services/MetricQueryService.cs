using FitbitMetrics.Application.Interfaces;
using FitbitMetrics.Application.Models;
using FitbitMetrics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FitbitMetrics.Infrastructure.Services;

internal sealed class MetricQueryService(FitbitDbContext dbContext) : IMetricQueryService
{
    public async Task<IReadOnlyList<DailyMetricSnapshot>> GetRecentMetricsAsync(
        int dayCount,
        CancellationToken cancellationToken = default)
    {
        if (dayCount <= 0 || dayCount > 365)
        {
            throw new ArgumentOutOfRangeException(nameof(dayCount), "Day count must be between 1 and 365.");
        }

        return await dbContext.DailyMetricSnapshots
            .AsNoTracking()
            .Where(item => item.UserKey == DemoUser.Key)
            .OrderByDescending(item => item.MetricDate)
            .Take(dayCount)
            .ToListAsync(cancellationToken);
    }
}
