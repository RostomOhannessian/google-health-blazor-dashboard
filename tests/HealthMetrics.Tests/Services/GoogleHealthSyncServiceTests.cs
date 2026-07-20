using System.Net;
using HealthMetrics.Application.Interfaces;
using HealthMetrics.Application.Models;
using HealthMetrics.Infrastructure.Clients;
using HealthMetrics.Infrastructure.Options;
using HealthMetrics.Infrastructure.Persistence;
using HealthMetrics.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HealthMetrics.Tests.Services;

public sealed class GoogleHealthSyncServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private HealthMetricsDbContext _dbContext = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        _dbContext = new HealthMetricsDbContext(
            new DbContextOptionsBuilder<HealthMetricsDbContext>().UseSqlite(_connection).Options);
        await _dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ── Argument guards ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(91)]
    public async Task SyncRecentDaysAsync_InvalidDayCount_Throws(int days)
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => svc.SyncRecentDaysAsync(days));
    }

    // ── Merge: null fields from a re-sync must not overwrite stored values ───

    [Fact]
    public async Task SyncRecentDaysAsync_Merge_PreservesExistingValuesWhenSourceIsNull()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Pre-populate a row with all metric values set.
        _dbContext.DailyMetricSnapshots.Add(new DailyMetricSnapshot
        {
            MetricDate = today,
            RestingHeartRateBpm = 58,
            HrvRmssdMilliseconds = 42.5m,
            DailyVo2MaxMlKgMin = 46.0m,
            RunVo2MaxMlKgMin = 47.0m,
            ConsumedCaloriesKcal = 2000,
            CarbohydratesGrams = 200m,
            FatGrams = 60m,
            ProteinGrams = 100m,
        });
        await _dbContext.SaveChangesAsync();

        // The API returns the same date but with every metric field null
        // (simulates a partial response or a day where the API returned nothing).
        var allNullHandler = new StubHandler(req =>
        {
            var path = req.RequestUri!.ToString();
            if (path.Contains(":dailyRollUp"))
                return Json($$$"""
                    {
                      "rollupDataPoints": [
                        { "civilStartTime": { "date": {"year":{{{today.Year}}},"month":{{{today.Month}}},"day":{{{today.Day}}}} } }
                      ]
                    }
                    """);
            return Json($$$"""
                {
                  "dataPoints": [
                    { "date": {"year":{{{today.Year}}},"month":{{{today.Month}}},"day":{{{today.Day}}}} }
                  ]
                }
                """);
        });

        var svc = CreateService(allNullHandler);
        await svc.SyncRecentDaysAsync(1);

        var stored = await _dbContext.DailyMetricSnapshots.SingleAsync();
        Assert.Equal(58, stored.RestingHeartRateBpm);
        Assert.Equal(42.5m, stored.HrvRmssdMilliseconds);
        Assert.Equal(46.0m, stored.DailyVo2MaxMlKgMin);
        Assert.Equal(47.0m, stored.RunVo2MaxMlKgMin);
        Assert.Equal(2000, stored.ConsumedCaloriesKcal);
        Assert.Equal(200m, stored.CarbohydratesGrams);
        Assert.Equal(60m, stored.FatGrams);
        Assert.Equal(100m, stored.ProteinGrams);
    }

    [Fact]
    public async Task SyncRecentDaysAsync_Merge_UpdatesValuesWhenSourceIsNonNull()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        _dbContext.DailyMetricSnapshots.Add(new DailyMetricSnapshot
        {
            MetricDate = today,
            RestingHeartRateBpm = 58,
        });
        await _dbContext.SaveChangesAsync();

        var updatedHrHandler = new StubHandler(req =>
        {
            var path = req.RequestUri!.ToString();
            if (path.Contains("daily-resting-heart-rate"))
                return Json($$$"""
                    {
                      "dataPoints": [
                        {
                          "date": {"year":{{{today.Year}}},"month":{{{today.Month}}},"day":{{{today.Day}}}},
                          "value": {"dailyRestingHeartRate": {"beatsPerMinute": 62}}
                        }
                      ]
                    }
                    """);
            return Json("""{}""");
        });

        var svc = CreateService(updatedHrHandler);
        await svc.SyncRecentDaysAsync(1);

        var stored = await _dbContext.DailyMetricSnapshots.SingleAsync();
        Assert.Equal(62, stored.RestingHeartRateBpm);
    }

    // ── PartialSuccess outcome ───────────────────────────────────────────────

    [Fact]
    public async Task SyncRecentDaysAsync_WhenNoMetricValues_RecordsPartialSuccess()
    {
        var emptyHandler = new StubHandler(_ => Json("""{}"""));
        var svc = CreateService(emptyHandler);

        await svc.SyncRecentDaysAsync(1);

        var entry = await _dbContext.SyncHistory.SingleAsync();
        Assert.Equal(SyncOutcome.PartialSuccess, entry.Outcome);
    }

    [Fact]
    public async Task SyncRecentDaysAsync_WhenMetricValuesPresent_RecordsSuccess()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var handler = new StubHandler(req =>
        {
            if (req.RequestUri!.ToString().Contains("daily-resting-heart-rate"))
                return Json($$$"""
                    {
                      "dataPoints": [
                        {
                          "date": {"year":{{{today.Year}}},"month":{{{today.Month}}},"day":{{{today.Day}}}},
                          "value": {"dailyRestingHeartRate": {"beatsPerMinute": 58}}
                        }
                      ]
                    }
                    """);
            return Json("""{}""");
        });
        var svc = CreateService(handler);

        await svc.SyncRecentDaysAsync(1);

        var entry = await _dbContext.SyncHistory.SingleAsync();
        Assert.Equal(SyncOutcome.Success, entry.Outcome);
    }

    // ── Error handling ───────────────────────────────────────────────────────

    [Fact]
    public async Task SyncRecentDaysAsync_WhenApiFails_RecordsFailedAndRethrows()
    {
        var failingHandler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"error":"unauthorized"}""")
        });
        var svc = CreateService(failingHandler);

        await Assert.ThrowsAsync<GoogleHealthApiException>(() => svc.SyncRecentDaysAsync(1));

        var entry = await _dbContext.SyncHistory.SingleAsync();
        Assert.Equal(SyncOutcome.Failed, entry.Outcome);
        Assert.NotNull(entry.ErrorMessage);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private GoogleHealthSyncService CreateService(HttpMessageHandler? handler = null)
    {
        handler ??= new StubHandler(_ => Json("""{}"""));
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://health.googleapis.com/v4/")
        };
        var loggingOptions = Options.Create(new GoogleHealthHttpLoggingOptions());
        var apiClient = new GoogleHealthApiClient(httpClient, loggingOptions, NullLogger<GoogleHealthApiClient>.Instance);

        var authService = new FakeAuthorizationService();

        return new GoogleHealthSyncService(_dbContext, authService, apiClient, NullLogger<GoogleHealthSyncService>.Instance);
    }

    private sealed class FakeAuthorizationService : IHealthAuthorizationService
    {
        public Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult("stub-token");

        public Task<Uri> BuildAuthorizationUriAsync(string state, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task HandleAuthorizationCodeAsync(string code, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HealthConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new HealthConnectionStatus(false, null, null, null, null, null));

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
}
