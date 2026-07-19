using HealthMetrics.Application.Interfaces;
using HealthMetrics.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HealthMetrics.Infrastructure.Services;

/// <summary>
/// Background service that runs a Google Health sync once per day at the configured UTC hour.
/// Only active when <see cref="GoogleHealthDailySyncOptions.Enabled"/> is true.
/// </summary>
internal sealed class GoogleHealthDailySyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<GoogleHealthDailySyncOptions> options,
    ILogger<GoogleHealthDailySyncBackgroundService> logger) : BackgroundService
{
    private readonly GoogleHealthDailySyncOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Google Health daily sync background service is disabled.");
            return;
        }

        logger.LogInformation(
            "Google Health daily sync background service started. Will sync {Days} days at {Hour:D2}:00 UTC daily.",
            _options.DaysToSync, _options.SyncHourUtc);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextRun();
            logger.LogInformation("Next automatic sync scheduled in {Minutes} minutes.", (int)delay.TotalMinutes);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Google Health daily sync background service is stopping.");
                break;
            }

            await RunSyncAsync(stoppingToken);
        }

        logger.LogInformation("Google Health daily sync background service stopped.");
    }

    private async Task RunSyncAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Google Health daily sync starting (last {Days} days).", _options.DaysToSync);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var syncService = scope.ServiceProvider.GetRequiredService<IHealthSyncService>();
            var result = await syncService.SyncRecentDaysAsync(_options.DaysToSync, stoppingToken);
            logger.LogInformation(
                "Google Health daily sync completed. Persisted {Persisted}/{Requested} day(s).",
                result.PersistedDays, result.RequestedDays);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Google Health daily sync failed.");
        }
    }

    private TimeSpan TimeUntilNextRun()
    {
        var now    = DateTimeOffset.UtcNow;
        var target = new DateTimeOffset(now.Date.AddHours(_options.SyncHourUtc), TimeSpan.Zero);
        if (now >= target)
            target = target.AddDays(1);
        return target - now;
    }
}
