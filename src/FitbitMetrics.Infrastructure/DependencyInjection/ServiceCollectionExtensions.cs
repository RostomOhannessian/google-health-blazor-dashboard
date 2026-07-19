using FitbitMetrics.Application.Interfaces;
using FitbitMetrics.Infrastructure.Clients;
using FitbitMetrics.Infrastructure.Options;
using FitbitMetrics.Infrastructure.Persistence;
using FitbitMetrics.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FitbitMetrics.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFitbitMetricsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<FitbitApiOptions>()
            .Bind(configuration.GetSection(FitbitApiOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ClientId)
                    && !string.IsNullOrWhiteSpace(options.ClientSecret)
                    && !string.IsNullOrWhiteSpace(options.RedirectUri)
                    && options.Scopes.Length > 0,
                "FitbitApi options must contain ClientId, ClientSecret, RedirectUri, and at least one scope.")
            .ValidateOnStart();

        services.AddOptions<FitbitDailySyncOptions>()
            .Bind(configuration.GetSection(FitbitDailySyncOptions.SectionName));

        var connectionString = configuration.GetConnectionString("FitbitMetricsDb")
            ?? "Data Source=fitbit-metrics.db";

        services.AddDbContext<FitbitDbContext>(options =>
        {
            options.UseSqlite(connectionString);
        });

        services.AddHttpClient<FitbitApiClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.fitbit.com");
            client.Timeout     = TimeSpan.FromSeconds(30);
        })
        .AddStandardResilienceHandler(resilience =>
        {
            resilience.Retry.MaxRetryAttempts    = 3;
            resilience.Retry.UseJitter           = true;
            resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(90);
        });

        services.AddScoped<IFitbitOAuthService, FitbitOAuthService>();
        services.AddScoped<IFitbitSyncService, FitbitSyncService>();
        services.AddScoped<IMetricQueryService, MetricQueryService>();

        services.AddHostedService<FitbitDailySyncBackgroundService>();

        return services;
    }
}
