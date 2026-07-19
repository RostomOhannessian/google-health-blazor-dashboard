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
    }

    [Fact]
    public async Task Connect_RedirectsToGoogleAuthorizationUri()
    {
        await using var factory = new HealthMetricsWebApplicationFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/health/connect");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth", response.Headers.Location!.ToString());
        Assert.Contains("state=", response.Headers.Location!.Query);
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
        Assert.StartsWith("Date,RestingHR_bpm,HRV_RMSSD_ms,RunVO2Max_ml_kg_min,Calories_kcal,Carbs_g,Fat_g,Protein_g", csv);
        Assert.DoesNotContain("Sodium", csv);
        Assert.DoesNotContain("Fiber", csv);
    }

    private sealed class HealthMetricsWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
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

                services.RemoveAll<IHealthAuthorizationService>();
                services.RemoveAll<IHealthSyncService>();
                services.RemoveAll<IMetricQueryService>();
                services.RemoveAll<IDemoSeedService>();

                services.AddSingleton<IHealthAuthorizationService, FakeHealthAuthorizationService>();
                services.AddSingleton<IHealthSyncService, FakeHealthSyncService>();
                services.AddSingleton<IMetricQueryService, FakeMetricQueryService>();
                services.AddSingleton<IDemoSeedService, FakeDemoSeedService>();
            });
        }
    }

    private sealed class FakeHealthAuthorizationService : IHealthAuthorizationService
    {
        public Task<Uri> BuildAuthorizationUriAsync(string state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Uri($"https://accounts.google.com/o/oauth2/v2/auth?state={state}&access_type=offline"));

        public Task HandleAuthorizationCodeAsync(string code, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult("access-token");

        public Task<HealthConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new HealthConnectionStatus(
                true,
                "google-user-1",
                DateTimeOffset.UtcNow.AddHours(1),
                null,
                DateTimeOffset.UtcNow.AddMinutes(-10)));

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
                    RunVo2MaxMlKgMin = 47.2m,
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
