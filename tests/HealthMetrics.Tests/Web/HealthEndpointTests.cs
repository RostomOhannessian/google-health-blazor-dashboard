using System.Net;
using HealthMetrics.Application.Interfaces;
using HealthMetrics.Application.Models;
using HealthMetrics.Infrastructure.Persistence;
using HealthMetrics.Web.Security;
using Microsoft.AspNetCore.Http;
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
        Assert.StartsWith("Date,RestingHR_bpm,HRV_RMSSD_ms,DailyVO2Max_ml_kg_min,RunVO2Max_ml_kg_min,ManualCardioLoad,ManualTargetLoad,ManualACWR,SleepEfficiency_pct,DeepSleep_min,RemSleep_min,Calories_kcal,Carbs_g,Fat_g,Protein_g,AlcoholEstimate_g", csv);
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

    [Theory]
    [InlineData("/api/metrics?days=30&endDate=0001-01-01")]
    [InlineData("/api/metrics?days=1&endDate=9999-12-31")]
    [InlineData("/api/metrics/export?days=366&endDate=0001-01-01")]
    [InlineData("/api/metrics/export?days=1&endDate=9999-12-31")]
    public async Task MetricsEndpoints_RejectInvalidDateRanges(string path)
    {
        await using var factory = new HealthMetricsWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Metrics_UsesRequestedDateRange()
    {
        var endDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7);
        await using var factory = new HealthMetricsWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/metrics?days=7&endDate={endDate:yyyy-MM-dd}");
        var queryService = Assert.IsType<FakeMetricQueryService>(
            factory.Services.GetRequiredService<IMetricQueryService>());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(endDate.AddDays(-6), queryService.LastStartDate);
        Assert.Equal(endDate, queryService.LastEndDate);
    }

    [Fact]
    public async Task RootDocument_UsesExactCurrentDayRange()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var expectedRange = MetricDateRange.ForRecentDays(30, today);
        await using var factory = new HealthMetricsWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();
        var queryService = Assert.IsType<FakeMetricQueryService>(
            factory.Services.GetRequiredService<IMetricQueryService>());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedRange.StartDate, queryService.LastStartDate);
        Assert.Equal(expectedRange.EndDate, queryService.LastEndDate);
        Assert.Contains("Sync last 30 days", html);
        Assert.Contains("weekly-summaries-toggle", html);
        Assert.Contains("daily-snapshots-panel", html);
        Assert.Contains("dailySnapshotsScroll", html);
        Assert.Contains("Scroll chart history horizontally", html);
        Assert.Contains("Scroll daily snapshot history", html);
        Assert.Contains("checked", html);
        Assert.Contains($"api/metrics/export?days={expectedRange.DayCount}", html);
        Assert.Contains($"endDate={expectedRange.EndDate:yyyy-MM-dd}", html);
        Assert.DoesNotContain("historical partial weeks are hidden", html);
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
        var stylesheetContent = await stylesheet.Content.ReadAsStringAsync();
        Assert.Contains(".page[", stylesheetContent);
        Assert.Contains(".chart-plot", stylesheetContent);
        Assert.Contains("height: 520px", stylesheetContent);
        Assert.Contains(".daily-snapshots-panel", stylesheetContent);
        Assert.Contains(".daily-snapshots-scroll", stylesheetContent);
        Assert.Contains("height: 31rem", stylesheetContent);
        Assert.Contains("border: var(--bs-border-width) solid var(--bs-border-color)", stylesheetContent);
        Assert.Contains("border-radius: var(--bs-border-radius)", stylesheetContent);
        Assert.Contains("position: sticky", stylesheetContent);
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
        Assert.Contains("78.0 / Weekly target: 75.0", html);
        Assert.Contains("1.05", html);
        Assert.Contains("Optimal Zone", html);
        Assert.Contains("Sat, Jul 18", html);
        Assert.Contains("border-start", html);
        Assert.Contains("Cardio Load (Manual)", html);
        Assert.Contains("Target Load (Manual)", html);
        Assert.Contains(">ACWR", html);
        Assert.Contains("Manual Cardio Load", html);
        Assert.Contains("Double-click to edit manual load", html);
        Assert.Contains("Sleep Efficiency (%)", html);
        Assert.Contains("Alcohol est. (g)", html);
        Assert.Contains("0.00", html);
    }

    [Fact]
    public async Task RootDocument_DefaultsDailySnapshotsToNewestDateFirst()
    {
        await using var factory = new HealthMetricsWebApplicationFactory(includeOlderMetric: true);
        var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        var newestDateIndex = html.IndexOf("Jul 18", StringComparison.Ordinal);
        var olderDateIndex = html.IndexOf("Jul 17", StringComparison.Ordinal);
        Assert.True(newestDateIndex >= 0);
        Assert.True(olderDateIndex >= 0);
        Assert.True(newestDateIndex < olderDateIndex);
    }

    [Fact]
    public async Task RootDocument_MergesConsecutiveWeeklyTargetCells()
    {
        await using var factory = new HealthMetricsWebApplicationFactory(includeOlderMetric: true);
        var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Matches("<td[^>]*rowspan=\"2\"[^>]*>75\\.0</td>", html);
    }

    [Fact]
    public async Task HomePage_ManualLoadEntry_UsesAccessibleModalTrigger()
    {
        await using var factory = new HealthMetricsWebApplicationFactory();
        var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("manual-load-entry-trigger", html);
        Assert.Contains("aria-haspopup=\"dialog\"", html);
        Assert.Contains("aria-controls=\"manual-load-modal\"", html);
        Assert.Contains("Enter or edit manual load", html);
        Assert.Contains("Enable optional autosave in the manual load popup.", html);
        Assert.Contains(">YTD<", html);
        Assert.Contains("Year to date (since January 1)", html);
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

    [Fact]
    public async Task Charts_ExposeDailyAndWeeklyLoadSeries()
    {
        await using var factory = new HealthMetricsWebApplicationFactory();
        var client = factory.CreateClient();

        var script = await client.GetStringAsync("/charts.js");

        Assert.Contains("Daily Cardio Load", script);
        Assert.Contains("Weekly cumulative load", script);
        Assert.Contains("Weekly target", script);
        Assert.Contains("Manual ACWR", script);
        Assert.Contains("yAcwr", script);
        Assert.Contains("Daily values (Monday-starting weeks)", script);
        Assert.Contains("type: \"bar\"", script);
        Assert.Contains("loadWeekBands", script);
        Assert.Contains("isWeekBoundary", script);
        Assert.Contains("bindHistoryScroll", script);
        Assert.Contains("latestWindowStart", script);
        Assert.Contains("maintainAspectRatio: false", script);
        Assert.Contains("animation: false", script);
        Assert.Contains("rgb(255, 193, 7)", script);
        Assert.Contains("formatCalendarDate", script);
        Assert.Contains("chartTooltipTitle", script);
        Assert.Contains("renderConsumption", script);
        Assert.Contains("Calories (kcal)", script);
        Assert.Contains("Alcohol estimate (g)", script);
        Assert.Contains("yCalories", script);
        Assert.Contains("yGrams", script);
    }

    [Fact]
    public async Task RootDocument_RendersPersistedHistoryOutsideTheSelectedRange()
    {
        var outsideSelectedRange = MetricDateRange
            .ForRecentDays(30, DateOnly.FromDateTime(DateTime.UtcNow))
            .StartDate
            .AddDays(-1);
        await using var factory = new HealthMetricsWebApplicationFactory(includeOutsideSelectedRangeMetric: true);
        var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains(
            outsideSelectedRange.ToString("ddd, MMM d", System.Globalization.CultureInfo.CurrentCulture),
            html);
        Assert.Contains("2 persisted history days", html);
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("::ffff:127.0.0.1", true)]
    [InlineData("192.168.1.10", false)]
    public void LocalRequestPolicy_RecognizesLoopbackAddresses(string remoteIpAddress, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIpAddress);

        Assert.Equal(expected, LocalRequestPolicy.IsLocal(context));
    }

    [Fact]
    public void LocalRequestPolicy_RejectsRequestsWithoutRemoteIp()
    {
        var context = new DefaultHttpContext();

        Assert.False(LocalRequestPolicy.IsLocal(context));
    }

    [Theory]
    [InlineData("/api/health/disconnect")]
    [InlineData("/api/health/sync?days=7")]
    [InlineData("/api/demo/seed?days=30")]
    public async Task LegacyMutationEndpoints_DoNotAcceptBrowserPostsWithoutAntiforgery(string path)
    {
        await using var factory = new HealthMetricsWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync(path, content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class HealthMetricsWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly bool useRealAuthorization;
        private readonly bool requiresReconnect;
        private readonly bool includeOlderMetric;
        private readonly bool includeOutsideSelectedRangeMetric;

        public HealthMetricsWebApplicationFactory(
            bool useRealAuthorization = false,
            bool requiresReconnect = false,
            bool includeOlderMetric = false,
            bool includeOutsideSelectedRangeMetric = false)
        {
            this.useRealAuthorization = useRealAuthorization;
            this.requiresReconnect = requiresReconnect;
            this.includeOlderMetric = includeOlderMetric;
            this.includeOutsideSelectedRangeMetric = includeOutsideSelectedRangeMetric;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                [
                   new KeyValuePair<string, string?>("LocalRequestPolicy:AllowMissingRemoteIp", "true"),
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
                services.AddSingleton<IMetricQueryService>(
                    new FakeMetricQueryService(includeOlderMetric, includeOutsideSelectedRangeMetric));
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

    private sealed class FakeMetricQueryService(
        bool includeOlderMetric,
        bool includeOutsideSelectedRangeMetric) : IMetricQueryService
    {
        public DateOnly? LastStartDate { get; private set; }
        public DateOnly? LastEndDate { get; private set; }

        public Task<IReadOnlyList<DailyMetricSnapshot>> GetRecentMetricsAsync(int dayCount, CancellationToken cancellationToken = default) =>
            GetFakeMetrics();

        public Task<IReadOnlyList<DailyMetricSnapshot>> GetMetricsAsync(
            DateOnly startDate,
            DateOnly endDate,
            CancellationToken cancellationToken = default)
        {
            LastStartDate = startDate;
            LastEndDate = endDate;
            return GetFakeMetrics();
        }

        private Task<IReadOnlyList<DailyMetricSnapshot>> GetFakeMetrics()
        {
            var metrics = new List<DailyMetricSnapshot>
            {
                CreateSnapshot(new DateOnly(2026, 7, 18))
            };
            if (includeOlderMetric)
                metrics.Add(CreateSnapshot(new DateOnly(2026, 7, 17)));
            if (includeOutsideSelectedRangeMetric)
            {
                var outsideSelectedRange = MetricDateRange
                    .ForRecentDays(30, DateOnly.FromDateTime(DateTime.UtcNow))
                    .StartDate
                    .AddDays(-1);
                metrics.Add(CreateSnapshot(outsideSelectedRange));
            }

            return Task.FromResult<IReadOnlyList<DailyMetricSnapshot>>(metrics);
        }

        public Task<IReadOnlyList<DailyMetricSnapshot>> GetAllMetricsAsync(CancellationToken cancellationToken = default)
        {
            var range = MetricDateRange.ForRecentDays(30, DateOnly.FromDateTime(DateTime.UtcNow));
            LastStartDate = range.StartDate;
            LastEndDate = range.EndDate;
            return GetFakeMetrics();
        }

        public Task<IReadOnlyList<SyncHistoryEntry>> GetRecentSyncHistoryAsync(int count = 10, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SyncHistoryEntry>>([]);

        private static DailyMetricSnapshot CreateSnapshot(DateOnly metricDate) => new()
        {
            UserKey = LocalUser.Key,
            MetricDate = metricDate,
            RestingHeartRateBpm = 58,
            HrvRmssdMilliseconds = 42.5m,
            DailyVo2MaxMlKgMin = 46.8m,
            RunVo2MaxMlKgMin = 47.2m,
            CardioLoad = 78m,
            TargetLoad = 75m,
            Acwr = 1.05m,
            SleepEfficiency = 91m,
            DeepSleepMinutes = 85,
            RemSleepMinutes = 105,
            ConsumedCaloriesKcal = 2200,
            CarbohydratesGrams = 260.5m,
            FatGrams = 70m,
            ProteinGrams = 120m,
            EstimatedAlcoholGrams = 0m
        };
    }

    private sealed class FakeDemoSeedService : IDemoSeedService
    {
        public Task<int> SeedAsync(int dayCount, CancellationToken cancellationToken = default) => Task.FromResult(dayCount);
    }
}
