using System.Net;
using HealthMetrics.Infrastructure.Clients;
using HealthMetrics.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HealthMetrics.Tests.Clients;

public sealed class GoogleHealthApiClientTests
{
    [Fact]
    public async Task GetIdentityAsync_ReturnsGoogleHealthUserId()
    {
        var handler = new StubHttpMessageHandler(_ =>
            Json("""{"name":"users/me/identity","healthUserId":"google-health-user-123","legacyUserId":"fitbit-user-456"}"""));
        var client = CreateClient(handler);

        var userId = await client.GetIdentityAsync("token", CancellationToken.None);

        Assert.Equal("google-health-user-123", userId);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains("users/me/identity", request.Uri);
    }

    [Fact]
    public async Task GetAccountEmailAsync_ReturnsGoogleAccountEmail()
    {
        var handler = new StubHttpMessageHandler(_ => Json("""{"email":"user@example.com"}"""));
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://openidconnect.googleapis.com/")
        };
        var client = new GoogleAccountApiClient(httpClient);

        var email = await client.GetEmailAsync("token", CancellationToken.None);

        Assert.Equal("user@example.com", email);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains("v1/userinfo", request.Uri);
    }

    [Fact]
    public async Task FetchDailyMetricsAsync_MapsGoogleHealthDailyData()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.ToString();
            if (path.Contains("users/me/settings"))
                return Json("""{"timeZone":"America/Toronto"}""");

            if (path.Contains("daily-resting-heart-rate"))
                return Json("""
                    {
                      "dataPoints": [
                        {
                          "date": { "year": 2026, "month": 7, "day": 18 },
                          "value": { "dailyRestingHeartRate": { "beatsPerMinute": 58 } }
                        }
                      ]
                    }
                    """);

            if (path.Contains("daily-heart-rate-variability"))
                return Json("""
                    {
                      "dataPoints": [
                        {
                          "date": { "year": 2026, "month": 7, "day": 18 },
                          "value": { "dailyHeartRateVariability": { "rmssdMilliseconds": 42.5 } }
                        }
                      ]
                    }
                    """);

            if (path.Contains("run-vo2-max"))
                return Json("""
                    {
                      "dailyRollupDataPoints": [
                        {
                          "date": { "year": 2026, "month": 7, "day": 18 },
                          "value": { "runVo2Max": { "rateAvg": 47.2 } }
                        }
                      ]
                    }
                    """);

            if (path.Contains("nutrition-log"))
                return Json("""
                    {
                      "dailyRollupDataPoints": [
                        {
                          "date": { "year": 2026, "month": 7, "day": 18 },
                          "value": {
                            "nutritionLog": {
                              "energy": { "kcalSum": 2200 },
                              "totalCarbohydrate": { "gramsSum": 260.5 },
                              "totalFat": { "gramsSum": 70 },
                              "nutrients": [
                                { "nutrient": "PROTEIN", "quantity": { "gramsSum": 120 } }
                              ]
                            }
                          }
                        }
                      ]
                    }
                    """);

            return Json("""{}""");
        });

        var client = CreateClient(handler);
        var snapshots = await client.FetchDailyMetricsAsync("token", new DateOnly(2026, 7, 18), new DateOnly(2026, 7, 18), CancellationToken.None);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(new DateOnly(2026, 7, 18), snapshot.MetricDate);
        Assert.Equal(58, snapshot.RestingHeartRateBpm);
        Assert.Equal(42.5m, snapshot.HrvRmssdMilliseconds);
        Assert.Equal(47.2m, snapshot.RunVo2MaxMlKgMin);
        Assert.Equal(2200, snapshot.ConsumedCaloriesKcal);
        Assert.Equal(260.5m, snapshot.CarbohydratesGrams);
        Assert.Equal(70m, snapshot.FatGrams);
        Assert.Equal(120m, snapshot.ProteinGrams);
    }

    [Fact]
    public async Task FetchDailyMetricsAsync_ListFiltersUseExclusiveEndDate()
    {
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.ToString().Contains("users/me/settings")
                ? Json("""{"timeZone":"UTC"}""")
                : Json("""{}"""));
        var client = CreateClient(handler);

        await client.FetchDailyMetricsAsync("token", new DateOnly(2026, 7, 18), new DateOnly(2026, 7, 18), CancellationToken.None);

        var request = Assert.Single(handler.Requests, request => request.Uri.Contains("daily-resting-heart-rate"));
        var filter = Uri.UnescapeDataString(request.Uri);
        Assert.Contains("daily_resting_heart_rate.date >= \"2026-07-18\"", filter);
        Assert.Contains("daily_resting_heart_rate.date < \"2026-07-19\"", filter);
        Assert.DoesNotContain("<=", filter);
    }

    [Fact]
    public async Task FetchDailyMetricsAsync_MapsStringDates()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.ToString();
            if (path.Contains("users/me/settings"))
                return Json("""{"timeZone":"UTC"}""");

            if (path.Contains("daily-resting-heart-rate"))
                return Json("""
                    {
                      "dataPoints": [
                        {
                          "date": "2026-07-18",
                          "value": { "dailyRestingHeartRate": { "beatsPerMinute": 58 } }
                        }
                      ]
                    }
                    """);

            return Json("""{}""");
        });
        var client = CreateClient(handler);

        var snapshots = await client.FetchDailyMetricsAsync("token", new DateOnly(2026, 7, 18), new DateOnly(2026, 7, 18), CancellationToken.None);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(new DateOnly(2026, 7, 18), snapshot.MetricDate);
        Assert.Equal(58, snapshot.RestingHeartRateBpm);
    }

    [Fact]
    public async Task FetchDailyMetricsAsync_PaginatesListEndpoints()
    {
        var restingHeartRateCalls = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.ToString();
            if (path.Contains("users/me/settings"))
                return Json("""{"timeZone":"UTC"}""");

            if (path.Contains("daily-resting-heart-rate"))
            {
                restingHeartRateCalls++;
                return restingHeartRateCalls == 1
                    ? Json("""
                        {
                          "nextPageToken": "page-2",
                          "dataPoints": [
                            { "date": { "year": 2026, "month": 7, "day": 18 }, "value": { "bpm": 60 } }
                          ]
                        }
                        """)
                    : Json("""
                        {
                          "dataPoints": [
                            { "date": { "year": 2026, "month": 7, "day": 19 }, "value": { "bpm": 61 } }
                          ]
                        }
                        """);
            }

            return Json("""{}""");
        });

        var client = CreateClient(handler);
        var snapshots = await client.FetchDailyMetricsAsync("token", new DateOnly(2026, 7, 18), new DateOnly(2026, 7, 19), CancellationToken.None);

        Assert.Equal(2, restingHeartRateCalls);
        Assert.Equal([60, 61], snapshots.Select(snapshot => snapshot.RestingHeartRateBpm).ToArray());
        Assert.Contains(handler.Requests, request => request.Uri.Contains("pageToken=page-2"));
    }

    [Fact]
    public async Task FetchDailyMetricsAsync_DailyRollupUsesCivilTimeWindow()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.ToString().Contains("users/me/settings"))
                return Json("""{"timeZone":"America/Toronto"}""");

            return Json("""{}""");
        });

        var client = CreateClient(handler);
        await client.FetchDailyMetricsAsync("token", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 1), CancellationToken.None);

        var runVo2Request = Assert.Single(handler.Requests, request => request.Uri.Contains("run-vo2-max"));
        Assert.Contains("\"timeZone\":\"America/Toronto\"", runVo2Request.Body);
        Assert.Contains("\"windowSizeDays\":1", runVo2Request.Body);
    }

    [Fact]
    public async Task FetchDailyMetricsAsync_AuthorizationFailureThrowsGoogleHealthApiException()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{"error":"insufficient_scope"}""")
        });

        var client = CreateClient(handler);
        var exception = await Assert.ThrowsAsync<GoogleHealthApiException>(
            () => client.FetchDailyMetricsAsync("token", new DateOnly(2026, 7, 18), new DateOnly(2026, 7, 18), CancellationToken.None));

        Assert.True(exception.IsAuthorizationFailure);
    }

    [Fact]
    public async Task FetchDailyMetricsAsync_RedactsPageTokensInFailureMessages()
    {
        var restingHeartRateCalls = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.ToString();
            if (path.Contains("users/me/settings"))
                return Json("""{"timeZone":"UTC"}""");

            if (path.Contains("daily-resting-heart-rate"))
            {
                restingHeartRateCalls++;
                return restingHeartRateCalls == 1
                    ? Json("""{"nextPageToken":"page-secret","dataPoints":[]}""")
                    : new HttpResponseMessage(HttpStatusCode.Forbidden)
                    {
                        Content = new StringContent("""{"error":"bad_page","nextPageToken":"response-secret"}""")
                    };
            }

            return Json("""{}""");
        });

        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<GoogleHealthApiException>(
            () => client.FetchDailyMetricsAsync("access-token-secret", new DateOnly(2026, 7, 18), new DateOnly(2026, 7, 18), CancellationToken.None));

        Assert.DoesNotContain("page-secret", exception.Message);
        Assert.DoesNotContain("response-secret", exception.Message);
        Assert.Contains("[redacted]", exception.Message);
    }

    [Fact]
    public async Task FetchDailyMetricsAsync_LogsRequestAndResponseBodiesWithRedactionWhenEnabled()
    {
        var logger = new CapturingLogger<GoogleHealthApiClient>();
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.ToString().Contains("users/me/settings"))
                return Json("""{"timeZone":"America/Toronto","nextPageToken":"response-secret"}""");

            return Json("""{}""");
        });

        var client = CreateClient(
            handler,
            new GoogleHealthHttpLoggingOptions { LogRequestBodies = true, LogResponseBodies = true, MaxBodyCharacters = 4096 },
            logger);

        await client.FetchDailyMetricsAsync("access-token-secret", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 1), CancellationToken.None);

        var messages = string.Join('\n', logger.Messages);
        Assert.Contains("Google Health API request body", messages);
        Assert.Contains("\"timeZone\":\"America/Toronto\"", messages);
        Assert.Contains("Google Health API response body", messages);
        Assert.Contains("\"nextPageToken\":\"[redacted]\"", messages);
        Assert.DoesNotContain("response-secret", messages);
        Assert.DoesNotContain("access-token-secret", messages);
    }

    private static GoogleHealthApiClient CreateClient(
        HttpMessageHandler handler,
        GoogleHealthHttpLoggingOptions? options = null,
        ILogger<GoogleHealthApiClient>? logger = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://health.googleapis.com/v4/")
        };

        return new GoogleHealthApiClient(
            httpClient,
            Options.Create(options ?? new GoogleHealthHttpLoggingOptions()),
            logger ?? NullLogger<GoogleHealthApiClient>.Instance);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

    private sealed record CapturedRequest(HttpMethod Method, string Uri, string Body);

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!.ToString(), body));
            return responder(request);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullLogScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    private sealed class NullLogScope : IDisposable
    {
        public static readonly NullLogScope Instance = new();

        public void Dispose()
        {
        }
    }
}
