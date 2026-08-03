using System.Net;
using HealthMetrics.Application.Interfaces;
using HealthMetrics.Application.Models;
using HealthMetrics.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;

namespace HealthMetrics.Tests.Web;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Status_ReturnsConnectionStatus()
    {
        await using var factory = new HealthMetricsWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health/status");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("google-user-1", json);
        Assert.Contains("user@example.com", json);
    }

    [Fact]
    public async Task Connect_RedirectsToGoogleAuthorizationUri()
    {
        await using var factory = new HealthMetricsWebApplicationFactory(useRealAuthorization: true);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/health/connect");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth", location);
        Assert.Contains("state=", response.Headers.Location.Query);
        Assert.Contains("redirect_uri=https%3A%2F%2Flocalhost%3A5001%2Fapi%2Fhealth%2Fcallback", location);
        // Google.Apis.Auth encodes spaces as literal spaces in the scope parameter, not %20.
        Assert.Contains("scope=openid email https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fgooglehealth.health_metrics_and_measurements.readonly", location);
        Assert.Contains("googlehealth.activity_and_fitness.readonly", location);
        Assert.Contains("googlehealth.nutrition.readonly", location);
        Assert.Contains("googlehealth.sleep.readonly", location);
    }

    [Fact]
    public async Task Callback_WithInvalidState_RedirectsWithAuthError()
    {
        await using var factory = new HealthMetricsWebApplicationFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/health/callback?code=abc&state=invalid");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/?authError=invalid_state", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task MetricsExport_ReturnsReducedCsvContract()
    {
        await using var factory = new HealthMetricsWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/metrics/export?days=7");
        var csv = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("Date,RestingHR_bpm,HRV_RMSSD_ms,DailyVO2Max_ml_kg_min,RunVO2Max_ml_kg_min,CardioLoad,TargetLoadMin,TargetLoadMax,ACWR,SleepEfficiency_pct,DeepSleep_min,RemSleep_min,Calories_kcal,Carbs_g,Fat_g,Protein_g", csv);
        Assert.DoesNotContain("Sodium", csv);
        Assert.DoesNotContain("Fiber", csv);
    }

    [Fact]
    public async Task MetricsExport_UsesInvariantCultureForDecimals()
    {
        // The fake metric query returns rows with decimal fields (HRV 42.5, Carbs 260.5 etc.).
        // Each data row must have exactly 16 comma-separated columns regardless of the host
        // locale. A comma-decimal locale would produce extra columns if formatting were
        // culture-dependent.
        await using var factory = new HealthMetricsWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/metrics/export?days=7");
        var csv = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines.Skip(1)) // skip header
            Assert.Equal(16, line.TrimEnd('\r').Split(',').Length);

        Assert.Contains("42.5", csv);
        Assert.Contains("260.5", csv);
        Assert.Contains("78", csv);
    }

    [Fact]
    public async Task RootDocument_ReferencesResolvableScopedStylesheet()
    {
        await using var factory = new HealthMetricsWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();
        var match = System.Text.RegularExpressions.Regex.Match(
            html,
            "href=\"(?<href>[^\"]*\\.styles\\.css)\"");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(match.Success, "The root document did not reference the generated scoped stylesheet.");

        var stylesheet = await client.GetAsync(match.Groups["href"].Value);

        Assert.Equal(HttpStatusCode.OK, stylesheet.StatusCode);
        Assert.Contains(".page[", await stylesheet.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RootDocument_ReferencesGoogleHealthConnectFeedbackScript()
    {
        await using var factory = new HealthMetricsWebApplicationFactory();
        var client = factory.CreateClient();

        var root = await client.GetAsync("/");
        var script = await client.GetAsync("/health-connect.js");

        Assert.Equal(HttpStatusCode.OK, root.StatusCode);
        Assert.Contains("health-connect.js", await root.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
    }

    [Fact]
    public async Task RootDocument_FormatsHrvAndNutritionPrecision()
    {
        await using var factory = new HealthMetricsWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("42.500 ms", html);
        Assert.Contains("260.50", html);
        Assert.Contains("70.00", html);
        Assert.Contains("120.00", html);
        Assert.Contains("78.0 / Target:", html);
        Assert.Contains("60.0", html);
        Assert.Contains("90.0", html);
        Assert.Contains("1.05", html);
        Assert.Contains("Optimal Zone", html);
        Assert.Contains("Cardio Load", html);
        Assert.Contains("Sleep Efficiency (%)", html);
    }

    [Fact]
    public async Task RootDocument_WhenConnectionNeedsNewScopes_ShowsReconnectGuidance()
    {
        await using var factory = new HealthMetricsWebApplicationFactory(requiresReconnect: true);
        var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Connected · reconnect required", html);
        Assert.Contains("Reconnect to grant sleep access. Other metrics can still sync.", html);
        Assert.Contains("Reconnect for sleep", html);
    }

    private sealed class HealthMetricsWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly bool useRealAuthorization;
        private readonly bool requiresReconnect;

        public HealthMetricsWebApplicationFactory(
            bool useRealAuthorization = false,
            bool requiresReconnect = false)
        {
            this.useRealAuthorization = useRealAuthorization;
            this.requiresReconnect = requiresReconnect;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>("GoogleHealthApi:ClientId", "health-metrics-test-client"),
                    new KeyValuePair<string, string?>("GoogleHealthApi:ClientSecret", "health-metrics-test-secret"),
                    new KeyValuePair<string, string?>("GoogleHealthApi:RedirectUri", "https://localhost:5001/api/health/callback"),
                    new KeyValuePair<string, string?>("GoogleHealthApi:Scopes:0", "openid"),
                    new KeyValuePair<string, string?>("GoogleHealthApi:Scopes:1", "email"),
                    new KeyValuePair<string, string?>("GoogleHealthApi:Scopes:2", "https://www.googleapis.com/auth/googlehealth.health_metrics_and_measurements.readonly"),
                    new KeyValuePair<string, string?>("GoogleHealthApi:Scopes:3", "https://www.googleapis.com/auth/googlehealth.activity_and_fitness.readonly"),
                    new KeyValuePair<string, string?>("GoogleHealthApi:Scopes:4", "https://www.googleapis.com/auth/googlehealth.nutrition.readonly"),
                    new KeyValuePair<string, string?>("GoogleHealthApi:Scopes:5", "https://www.googleapis.com/auth/googlehealth.sleep.readonly")
                ]);
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<HealthMetricsDbContext>>();
                services.AddSingleton(_ =>
                {
                    var connection = new SqliteConnection("Data Source=:memory:");
                    connection.Open();
                    return connection;
                });
                services.AddDbContext<HealthMetricsDbContext>((sp, options) =>
                    options.UseSqlite(sp.GetRequiredService<SqliteConnection>()));

                services.RemoveAll<IHealthSyncService>();
                services.RemoveAll<IMetricQueryService>();
                services.RemoveAll<IDemoSeedService>();

                if (!useRealAuthorization)
                {
                    services.RemoveAll<IHealthAuthorizationService>();
                    services.AddSingleton<IHealthAuthorizationService>(
                        new FakeHealthAuthorizationService(requiresReconnect));
                }
                services.AddSingleton<IHealthSyncService, FakeHealthSyncService>();
                services.AddSingleton<IMetricQueryService, FakeMetricQueryService>();
                services.AddSingleton<IDemoSeedService, FakeDemoSeedService>();
            });
        }
    }

    private sealed class FakeHealthAuthorizationService(bool requiresReconnect) : IHealthAuthorizationService
    {
        public Task<Uri> BuildAuthorizationUriAsync(string state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Uri(
                "https://accounts.google.com/o/oauth2/v2/auth" +
                "?state=" + state +
                "&access_type=offline" +
                "&redirect_uri=https%3A%2F%2Flocalhost%3A5001%2Fapi%2Fhealth%2Fcallback" +
                "&scope=https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fgooglehealth.health_metrics_and_measurements.readonly%20" +
                "https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fgooglehealth.activity_and_fitness.readonly%20" +
                "https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fgooglehealth.nutrition.readonly"));

        public Task HandleAuthorizationCodeAsync(string code, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult("access-token");

        public Task<HealthConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new HealthConnectionStatus(
                true,
                "google-user-1",
                "user@example.com",
                DateTimeOffset.UtcNow.AddHours(1),
                null,
                DateTimeOffset.UtcNow.AddMinutes(-10),
                requiresReconnect));

        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeHealthSyncService : IHealthSyncService
    {
        public Task<SyncResult> SyncRecentDaysAsync(int dayCount, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SyncResult(dayCount, dayCount, DateTimeOffset.UtcNow));
    }

    private sealed class FakeMetricQueryService : IMetricQueryService
    {
        public Task<IReadOnlyList<DailyMetricSnapshot>> GetRecentMetricsAsync(int dayCount, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DailyMetricSnapshot>>(
            [
                new DailyMetricSnapshot
                {
                    UserKey = LocalUser.Key,
                    MetricDate = new DateOnly(2026, 7, 18),
                    RestingHeartRateBpm = 58,
                    HrvRmssdMilliseconds = 42.5m,
                    DailyVo2MaxMlKgMin = 46.8m,
                    RunVo2MaxMlKgMin = 47.2m,
                    CardioLoad = 78m,
                    TargetLoadMin = 60m,
                    TargetLoadMax = 90m,
                    Acwr = 1.05m,
                    SleepEfficiency = 91m,
                    DeepSleepMinutes = 85,
                    RemSleepMinutes = 105,
                    ConsumedCaloriesKcal = 2200,
                    CarbohydratesGrams = 260.5m,
                    FatGrams = 70m,
                    ProteinGrams = 120m
                }
            ]);

        public Task<IReadOnlyList<SyncHistoryEntry>> GetRecentSyncHistoryAsync(int count = 10, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SyncHistoryEntry>>([]);
    }

    private sealed class FakeDemoSeedService : IDemoSeedService
    {
        public Task<int> SeedAsync(int dayCount, CancellationToken cancellationToken = default) => Task.FromResult(dayCount);
    }
}
