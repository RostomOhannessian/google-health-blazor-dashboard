using FitbitMetrics.Application.Interfaces;
using FitbitMetrics.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FitbitMetrics.Infrastructure.Services;

/// <summary>
/// Background service that runs a Fitbit sync once per day at the configured UTC hour.
/// Only active when <see cref="FitbitDailySyncOptions.Enabled"/> is true.
/// </summary>
internal sealed class FitbitDailySyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<FitbitDailySyncOptions> options,
    ILogger<FitbitDailySyncBackgroundService> logger) : BackgroundService
{
    private readonly FitbitDailySyncOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Fitbit daily sync background service is disabled.");
            return;
        }

        logger.LogInformation(
            "Fitbit daily sync background service started. Will sync {Days} days at {Hour:D2}:00 UTC daily.",
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
                break;
            }

            await RunSyncAsync(stoppingToken);
        }
    }

    private async Task RunSyncAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Fitbit daily sync starting (last {Days} days).", _options.DaysToSync);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var syncService = scope.ServiceProvider.GetRequiredService<IFitbitSyncService>();
            var result = await syncService.SyncRecentDaysAsync(_options.DaysToSync, stoppingToken);
            logger.LogInformation(
                "Fitbit daily sync completed. Persisted {Persisted}/{Requested} day(s).",
                result.PersistedDays, result.RequestedDays);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Fitbit daily sync failed.");
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
