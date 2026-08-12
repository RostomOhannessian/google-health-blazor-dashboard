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
    [InlineData(367)]
    public async Task SyncRecentDaysAsync_InvalidDayCount_Throws(int days)
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => svc.SyncRecentDaysAsync(days));
    }

    [Fact]
    public async Task SyncRecentDaysAsync_AllowsLeapYearDayCount()
    {
        var result = await CreateService().SyncRecentDaysAsync(366);

        Assert.Equal(366, result.RequestedDays);
        Assert.Equal(366, result.PersistedDays);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(30)]
    [InlineData(90)]
    public async Task SyncRecentDaysAsync_PersistsExactInclusiveRequestedRange(int dayCount)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var result = await CreateService().SyncRecentDaysAsync(dayCount);
        var snapshots = await _dbContext.DailyMetricSnapshots
            .OrderBy(snapshot => snapshot.MetricDate)
            .ToListAsync();

        Assert.Equal(dayCount, result.RequestedDays);
        Assert.Equal(dayCount, result.PersistedDays);
        Assert.Equal(dayCount, snapshots.Count);
        Assert.Equal(today.AddDays(1 - dayCount), snapshots.First().MetricDate);
        Assert.Equal(today, snapshots.Last().MetricDate);
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
            CardioLoad = 78m,
            TargetLoad = 60m,
            SleepEfficiency = 91m,
            DeepSleepMinutes = 85,
            RemSleepMinutes = 105,
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
        Assert.Equal(78m, stored.CardioLoad);
        Assert.Equal(60m, stored.TargetLoad);
        Assert.Equal(91m, stored.SleepEfficiency);
        Assert.Equal(85, stored.DeepSleepMinutes);
        Assert.Equal(105, stored.RemSleepMinutes);
        Assert.Equal(2000, stored.ConsumedCaloriesKcal);
        Assert.Equal(200m, stored.CarbohydratesGrams);
        Assert.Equal(60m, stored.FatGrams);
        Assert.Equal(100m, stored.ProteinGrams);
        Assert.Equal(37.14m, stored.EstimatedAlcoholGrams);
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

    [Fact]
    public async Task SyncRecentDaysAsync_Merge_PreservesManualLoadAndStoresSleep()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _dbContext.DailyMetricSnapshots.Add(new DailyMetricSnapshot
        {
            MetricDate = today,
            CardioLoad = 78m,
            TargetLoad = 75m,
            Acwr = 1.05m,
        });
        await _dbContext.SaveChangesAsync();

        var handler = new StubHandler(req =>
        {
            var path = req.RequestUri!.ToString();
            if (path.Contains("dataTypes/sleep/dataPoints"))
                return Json($$$"""
                    {
                      "dataPoints": [
                        {
                          "value": {
                            "sleep": {
                              "interval": {
                                "civilEndTime": {"date": {"year":{{{today.Year}}},"month":{{{today.Month}}},"day":{{{today.Day}}}}}
                              },
                              "metadata": {"mainSleep": true},
                              "summary": {
                                "sleepEfficiency": 88,
                                "stagesSummary": [
                                  {"type": "DEEP", "minutes": 70},
                                  {"type": "REM", "minutes": 100}
                                ]
                              }
                            }
                          }
                        }
                      ]
                    }
                    """);
            return Json("""{}""");
        });

        await new GoogleHealthSyncService(
            _dbContext,
            new FakeAuthorizationService(),
            new GoogleHealthApiClient(
                new HttpClient(handler) { BaseAddress = new Uri("https://health.googleapis.com/v4/") },
                Options.Create(new GoogleHealthHttpLoggingOptions()),
                NullLogger<GoogleHealthApiClient>.Instance),
            NullLogger<GoogleHealthSyncService>.Instance).SyncRecentDaysAsync(1);

        var stored = await _dbContext.DailyMetricSnapshots.SingleAsync();
        Assert.Equal(78m, stored.CardioLoad);
        Assert.Equal(75m, stored.TargetLoad);
        Assert.Equal(88m, stored.SleepEfficiency);
        Assert.Equal(70, stored.DeepSleepMinutes);
        Assert.Equal(100, stored.RemSleepMinutes);
    }

    [Fact]
    public async Task SyncRecentDaysAsync_RecalculatesManualAcwrFromPersistedHistory()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _dbContext.DailyMetricSnapshots.AddRange(
            Enumerable.Range(0, 28).Select(offset => new DailyMetricSnapshot
            {
                MetricDate = today.AddDays(-offset),
                CardioLoad = 100m
            }));
        await _dbContext.SaveChangesAsync();

        var handler = new StubHandler(req =>
        {
            return Json("""{}""");
        });

        await CreateService(handler).SyncRecentDaysAsync(1);

        var stored = await _dbContext.DailyMetricSnapshots
            .SingleAsync(snapshot => snapshot.MetricDate == today);
        Assert.Equal(1m, stored.Acwr);
        Assert.Null(stored.TargetLoad);
        Assert.All(
            await _dbContext.DailyMetricSnapshots.Where(snapshot => snapshot.MetricDate < today).ToListAsync(),
            snapshot => Assert.Null(snapshot.Acwr));
    }

    [Fact]
    public async Task SyncRecentDaysAsync_WhenStoredGrantLacksSleepScope_SkipsSleepAndPersistsCoreMetrics()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _dbContext.HealthConnections.Add(new HealthConnection
        {
            GoogleUserId = "legacy-user",
            AccessToken = "unused-by-fake-authorization",
            RefreshToken = "unused-by-fake-authorization",
            Scope = "openid email",
            AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        });
        await _dbContext.SaveChangesAsync();

        var sleepRequested = false;
        var handler = new StubHandler(req =>
        {
            var path = req.RequestUri!.ToString();
            if (path.Contains("daily-resting-heart-rate"))
            {
                return Json($$$"""
                    {
                      "dataPoints": [
                        {
                          "date": {"year":{{{today.Year}}},"month":{{{today.Month}}},"day":{{{today.Day}}}},
                          "value": {"beatsPerMinute": 59}
                        }
                      ]
                    }
                    """);
            }

            if (path.Contains("dataTypes/sleep/dataPoints"))
            {
                sleepRequested = true;
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }

            return Json("""{}""");
        });

        await CreateService(handler).SyncRecentDaysAsync(1);

        Assert.False(sleepRequested);
        var stored = await _dbContext.DailyMetricSnapshots.SingleAsync();
        Assert.Equal(59, stored.RestingHeartRateBpm);
        Assert.Null(stored.SleepEfficiency);

        var history = await _dbContext.SyncHistory.SingleAsync();
        Assert.Equal(SyncOutcome.Success, history.Outcome);
        Assert.NotNull((await _dbContext.HealthConnections.SingleAsync()).LastSuccessfulSyncAtUtc);
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
