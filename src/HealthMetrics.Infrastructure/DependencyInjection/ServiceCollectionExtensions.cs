using HealthMetrics.Application.Interfaces;
using HealthMetrics.Infrastructure.Clients;
using HealthMetrics.Infrastructure.Options;
using HealthMetrics.Infrastructure.Persistence;
using HealthMetrics.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HealthMetrics.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHealthMetricsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<GoogleHealthApiOptions>()
            .Bind(configuration.GetSection(GoogleHealthApiOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ClientId)
                    && !string.IsNullOrWhiteSpace(options.ClientSecret)
                    && !string.IsNullOrWhiteSpace(options.RedirectUri)
                    && options.Scopes.Length > 0,
                "GoogleHealthApi options must contain ClientId, ClientSecret, RedirectUri, and at least one scope.")
            .ValidateOnStart();

        services.AddOptions<GoogleHealthDailySyncOptions>()
            .Bind(configuration.GetSection(GoogleHealthDailySyncOptions.SectionName))
            .Validate(options => options.SyncHourUtc is >= 0 and <= 23, "SyncHourUtc must be between 0 and 23.")
            .Validate(options => options.DaysToSync is >= 1 and <= 90, "DaysToSync must be between 1 and 90.");

        services.AddOptions<GoogleHealthHttpLoggingOptions>()
            .Bind(configuration.GetSection(GoogleHealthHttpLoggingOptions.SectionName))
            .Validate(
                options => options.MaxBodyCharacters is >= 0 and <= 32768,
                "GoogleHealthHttpLogging MaxBodyCharacters must be between 0 and 32768.");

        var connectionString = configuration.GetConnectionString("HealthMetricsDb")
            ?? "Data Source=health-metrics.db";

        services.AddDbContext<HealthMetricsDbContext>(options =>
        {
            options.UseSqlite(connectionString);
        });

        services.AddHttpClient<GoogleHealthApiClient>(client =>
        {
            client.BaseAddress = new Uri("https://health.googleapis.com/v4/");
            client.Timeout     = TimeSpan.FromSeconds(30);
        })
        .AddStandardResilienceHandler(resilience =>
        {
            resilience.Retry.MaxRetryAttempts    = 3;
            resilience.Retry.UseJitter           = true;
            resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(90);
        });

        services.AddScoped<IHealthAuthorizationService, GoogleHealthAuthorizationService>();
        services.AddScoped<IHealthSyncService, GoogleHealthSyncService>();
        services.AddScoped<IMetricQueryService, MetricQueryService>();
        services.AddScoped<IDemoSeedService, DemoSeedService>();

        services.AddHostedService<GoogleHealthDailySyncBackgroundService>();

        return services;
    }
}
