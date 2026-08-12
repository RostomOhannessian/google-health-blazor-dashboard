using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HealthMetrics.Application.Models;
using HealthMetrics.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HealthMetrics.Infrastructure.Clients;

internal sealed class GoogleHealthApiClient(
    HttpClient httpClient,
    IOptions<GoogleHealthHttpLoggingOptions> loggingOptions,
    ILogger<GoogleHealthApiClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex SensitiveJsonPropertyPattern = new(
        "(?i)(\"(?:access[_-]?token|refresh[_-]?token|client[_-]?secret|authorization|pageToken|nextPageToken)\"\\s*:\\s*\")([^\"]*)(\")",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private readonly GoogleHealthHttpLoggingOptions _loggingOptions = loggingOptions.Value;

    public async Task<string> GetIdentityAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, "users/me/identity", accessToken, null, cancellationToken);
        var root = doc.RootElement;

        return FindString(root, "healthUserId", "googleUserId", "google_user_id", "userId", "id")
            ?? throw new InvalidOperationException("Google Health identity response did not include a user id.");
    }

    public async Task<IReadOnlyList<DailyMetricSnapshot>> FetchDailyMetricsAsync(
        string accessToken,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken,
        bool includeSleep = true)
    {
        if (endDate < startDate)
            throw new ArgumentException("End date must be on or after start date.", nameof(endDate));

        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation("Google Health metric fetch started for {StartDate} through {EndDate}.", startDate, endDate);

        var snapshots = Enumerable.Range(0, endDate.DayNumber - startDate.DayNumber + 1)
            .Select(offset =>
            {
                var date = startDate.AddDays(offset);
                return new DailyMetricSnapshot
                {
                    UserKey = LocalUser.Key,
                    MetricDate = date,
                    CapturedAtUtc = DateTimeOffset.UtcNow
                };
            })
            .ToDictionary(snapshot => snapshot.MetricDate);

        await MergeDailyListAsync(
            snapshots,
            "daily-resting-heart-rate",
            "daily_resting_heart_rate",
            startDate,
            endDate,
            accessToken,
            (snapshot, point) => snapshot.RestingHeartRateBpm = ReadInt(point, "beatsPerMinute", "bpm", "heartRateBpm", "value"),
            cancellationToken);

        await MergeDailyListAsync(
            snapshots,
            "daily-heart-rate-variability",
            "daily_heart_rate_variability",
            startDate,
            endDate,
            accessToken,
            (snapshot, point) => snapshot.HrvRmssdMilliseconds = ReadDecimal(
                point,
                "averageHeartRateVariabilityMilliseconds",
                "deepSleepRootMeanSquareOfSuccessiveDifferencesMilliseconds",
                "rmssdMilliseconds",
                "rmssdMillis",
                "dailyRmssd",
                "rmssd"),
            cancellationToken);

        if (includeSleep)
            await MergeSleepAsync(snapshots, startDate, endDate, accessToken, cancellationToken);

        await MergeDailyListAsync(
            snapshots,
            "daily-vo2-max",
            "daily_vo2_max",
            startDate,
            endDate,
            accessToken,
            (snapshot, point) => snapshot.DailyVo2MaxMlKgMin = ReadDecimal(point, "vo2Max", "value"),
            cancellationToken);

        await MergeDailyRollupAsync(
            snapshots,
            "run-vo2-max",
            startDate,
            endDate,
            accessToken,
            (snapshot, point) => snapshot.RunVo2MaxMlKgMin = ReadDecimal(point, "rateAvg", "vo2Max", "score"),
            cancellationToken);

        await MergeDailyRollupAsync(
            snapshots,
            "nutrition-log",
            startDate,
            endDate,
            accessToken,
            ApplyNutrition,
            cancellationToken);

        var results = snapshots.Values.OrderBy(snapshot => snapshot.MetricDate).ToList();
        stopwatch.Stop();
        logger.LogInformation(
            "Google Health metric fetch completed for {StartDate} through {EndDate}. Returned {SnapshotCount} day(s); {DaysWithMetricValues} day(s) included metric values in {ElapsedMs} ms.",
            startDate,
            endDate,
            results.Count,
            results.Count(HasAnyMetricValue),
            stopwatch.ElapsedMilliseconds);

        return results;
    }

    private async Task MergeDailyListAsync(
        Dictionary<DateOnly, DailyMetricSnapshot> snapshots,
        string dataType,
        string filterPrefix,
        DateOnly startDate,
        DateOnly endDate,
        string accessToken,
        Action<DailyMetricSnapshot, JsonElement> apply,
        CancellationToken cancellationToken)
    {
        var exclusiveEndDate = endDate.AddDays(1);
        var filter = $"{filterPrefix}.date >= \"{startDate:yyyy-MM-dd}\" AND {filterPrefix}.date < \"{exclusiveEndDate:yyyy-MM-dd}\"";
        var pageToken = string.Empty;
        var pageCount = 0;
        var pointCount = 0;
        logger.LogInformation(
            "Google Health daily list fetch started for {DataType} from {StartDate} through {EndDate}.",
            dataType,
            startDate,
            endDate);

        do
        {
            var path = new StringBuilder($"users/me/dataTypes/{dataType}/dataPoints?pageSize=10000&filter={Uri.EscapeDataString(filter)}");
            if (!string.IsNullOrWhiteSpace(pageToken))
                path.Append("&pageToken=").Append(Uri.EscapeDataString(pageToken));

            using var doc = await SendJsonAsync(HttpMethod.Get, path.ToString(), accessToken, null, cancellationToken);
            pageCount++;
            var points = ReadDataPoints(doc.RootElement).ToList();
            pointCount += points.Count;
            foreach (var point in points)
            {
                if (TryReadDate(point, out var date) && snapshots.TryGetValue(date, out var snapshot))
                    apply(snapshot, point);
            }

            pageToken = FindString(doc.RootElement, "nextPageToken") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(pageToken))
                logger.LogDebug("Google Health daily list fetch for {DataType} has another page.", dataType);
        }
        while (!string.IsNullOrWhiteSpace(pageToken));

        logger.LogInformation(
            "Google Health daily list fetch completed for {DataType}. Pages: {PageCount}; data points: {PointCount}.",
            dataType,
            pageCount,
            pointCount);
    }

    private async Task MergeDailyRollupAsync(
        Dictionary<DateOnly, DailyMetricSnapshot> snapshots,
        string dataType,
        DateOnly startDate,
        DateOnly endDate,
        string accessToken,
        Action<DailyMetricSnapshot, JsonElement> apply,
        CancellationToken cancellationToken)
    {
        var chunkCount = 0;
        var pointCount = 0;
        logger.LogInformation(
            "Google Health daily rollup fetch started for {DataType} from {StartDate} through {EndDate}.",
            dataType,
            startDate,
            endDate);

        foreach (var (chunkStart, chunkEnd) in ChunkRange(startDate, endDate, maxDays: 14))
        {
            chunkCount++;
            logger.LogDebug(
                "Google Health daily rollup chunk requested for {DataType} from {StartDate} through {EndDate}.",
                dataType,
                chunkStart,
                chunkEnd);

            var body = new
            {
                range = new
                {
                    start = new { date = ToDateObject(chunkStart) },
                    end = new { date = ToDateObject(chunkEnd.AddDays(1)) }
                },
                windowSizeDays = 1
            };

            using var doc = await SendJsonAsync(
                HttpMethod.Post,
                $"users/me/dataTypes/{dataType}/dataPoints:dailyRollUp",
                accessToken,
                JsonSerializer.Serialize(body, JsonOptions),
                cancellationToken);

            var points = ReadDataPoints(doc.RootElement).ToList();
            pointCount += points.Count;
            foreach (var point in points)
            {
                if (TryReadDate(point, out var date) && snapshots.TryGetValue(date, out var snapshot))
                    apply(snapshot, point);
            }
        }

        logger.LogInformation(
            "Google Health daily rollup fetch completed for {DataType}. Chunks: {ChunkCount}; data points: {PointCount}.",
            dataType,
            chunkCount,
            pointCount);
    }

    private async Task MergeSleepAsync(
        Dictionary<DateOnly, DailyMetricSnapshot> snapshots,
        DateOnly startDate,
        DateOnly endDate,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var exclusiveEndDate = endDate.AddDays(1);
        var filter = $"sleep.interval.civil_end_time >= \"{startDate:yyyy-MM-dd}\" AND sleep.interval.civil_end_time < \"{exclusiveEndDate:yyyy-MM-dd}\"";
        var pageToken = string.Empty;
        var candidates = new Dictionary<DateOnly, SleepMetrics>();

        do
        {
            var path = new StringBuilder(
                $"users/me/dataTypes/sleep/dataPoints?pageSize=25&filter={Uri.EscapeDataString(filter)}");
            if (!string.IsNullOrWhiteSpace(pageToken))
                path.Append("&pageToken=").Append(Uri.EscapeDataString(pageToken));

            using var doc = await SendJsonAsync(HttpMethod.Get, path.ToString(), accessToken, null, cancellationToken);
            foreach (var point in ReadDataPoints(doc.RootElement))
            {
                if (!TryReadSleepMetrics(point, out var sleepMetrics)
                    || sleepMetrics.Date < startDate
                    || sleepMetrics.Date > endDate)
                {
                    continue;
                }

                if (!candidates.TryGetValue(sleepMetrics.Date, out var current)
                    || IsPreferredSleep(sleepMetrics, current))
                {
                    candidates[sleepMetrics.Date] = sleepMetrics;
                }
            }

            pageToken = FindString(doc.RootElement, "nextPageToken") ?? string.Empty;
        }
        while (!string.IsNullOrWhiteSpace(pageToken));

        foreach (var (date, sleepMetrics) in candidates)
        {
            if (snapshots.TryGetValue(date, out var snapshot))
            {
                snapshot.SleepEfficiency = sleepMetrics.SleepEfficiency;
                snapshot.DeepSleepMinutes = sleepMetrics.DeepSleepMinutes;
                snapshot.RemSleepMinutes = sleepMetrics.RemSleepMinutes;
            }
        }
    }

    private static bool IsPreferredSleep(SleepMetrics candidate, SleepMetrics current) =>
        candidate.IsMainSleep != current.IsMainSleep
            ? candidate.IsMainSleep
            : candidate.DurationMinutes > current.DurationMinutes;

    private static void ApplyNutrition(DailyMetricSnapshot snapshot, JsonElement point)
    {
        snapshot.ConsumedCaloriesKcal = ReadInt(point, "kcalSum", "kilocaloriesSum", "caloriesKcal");
        snapshot.CarbohydratesGrams = ReadNestedDecimal(point, "totalCarbohydrate", "gramsSum")
            ?? ReadNestedDecimal(point, "carbohydrate", "gramsSum");
        snapshot.FatGrams = ReadNestedDecimal(point, "totalFat", "gramsSum")
            ?? ReadNestedDecimal(point, "fat", "gramsSum");
        snapshot.ProteinGrams = ReadNutrientGrams(point, "PROTEIN")
            ?? ReadNestedDecimal(point, "protein", "gramsSum");
        NutritionEnergyEstimator.UpdateEstimatedAlcoholGrams(snapshot);
    }

    private async Task<JsonDocument> SendJsonAsync(
        HttpMethod method,
        string path,
        string accessToken,
        string? jsonBody,
        CancellationToken cancellationToken)
    {
        var sanitizedPath = SanitizePath(path);
        var operation = ResolveOperation(method, path);
        var dataType = TryReadDataType(path);
        var stopwatch = Stopwatch.StartNew();

        logger.LogInformation(
            "Google Health API request started. Method: {Method}; Operation: {Operation}; DataType: {DataType}; Path: {Path}.",
            method.Method,
            operation,
            dataType,
            sanitizedPath);

        if (jsonBody is not null && _loggingOptions.LogRequestBodies)
        {
            logger.LogDebug(
                "Google Health API request body. Operation: {Operation}; DataType: {DataType}; Body: {RequestBody}.",
                operation,
                dataType,
                SanitizeAndTruncate(jsonBody));
        }

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (jsonBody is not null)
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        string responseBody;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
            responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            logger.LogError(
                ex,
                "Google Health API request failed before a response was received. Method: {Method}; Operation: {Operation}; DataType: {DataType}; Path: {Path}; ElapsedMs: {ElapsedMs}.",
                method.Method,
                operation,
                dataType,
                sanitizedPath,
                stopwatch.ElapsedMilliseconds);
            throw;
        }

        using (response)
        {
            stopwatch.Stop();
            var contentLength = response.Content.Headers.ContentLength ?? responseBody.Length;
            if (_loggingOptions.LogResponseBodies)
            {
                logger.LogDebug(
                    "Google Health API response body. Operation: {Operation}; DataType: {DataType}; StatusCode: {StatusCode}; Body: {ResponseBody}.",
                    operation,
                    dataType,
                    (int)response.StatusCode,
                    SanitizeAndTruncate(responseBody));
            }

            if (!response.IsSuccessStatusCode)
            {
                var snippet = SanitizeAndTruncate(responseBody);
                logger.LogWarning(
                    "Google Health API response failed. Method: {Method}; Operation: {Operation}; DataType: {DataType}; Path: {Path}; StatusCode: {StatusCode}; ContentLength: {ContentLength}; ElapsedMs: {ElapsedMs}; ErrorSnippet: {ErrorSnippet}.",
                    method.Method,
                    operation,
                    dataType,
                    sanitizedPath,
                    (int)response.StatusCode,
                    contentLength,
                    stopwatch.ElapsedMilliseconds,
                    snippet);

                throw new GoogleHealthApiException(response.StatusCode, $"Google Health API call failed for '{sanitizedPath}' with status {(int)response.StatusCode}: {snippet}");
            }

            try
            {
                var document = JsonDocument.Parse(responseBody);
                logger.LogInformation(
                    "Google Health API response received. Method: {Method}; Operation: {Operation}; DataType: {DataType}; Path: {Path}; StatusCode: {StatusCode}; ContentLength: {ContentLength}; DataPointCount: {DataPointCount}; ElapsedMs: {ElapsedMs}.",
                    method.Method,
                    operation,
                    dataType,
                    sanitizedPath,
                    (int)response.StatusCode,
                    contentLength,
                    CountDataPoints(document.RootElement),
                    stopwatch.ElapsedMilliseconds);

                return document;
            }
            catch (JsonException ex)
            {
                logger.LogError(
                    ex,
                    "Google Health API response JSON parsing failed. Method: {Method}; Operation: {Operation}; DataType: {DataType}; Path: {Path}; StatusCode: {StatusCode}; ContentLength: {ContentLength}; ElapsedMs: {ElapsedMs}.",
                    method.Method,
                    operation,
                    dataType,
                    sanitizedPath,
                    (int)response.StatusCode,
                    contentLength,
                    stopwatch.ElapsedMilliseconds);
                throw;
            }
        }
    }

    private string SanitizeAndTruncate(string value)
    {
        var sanitized = SensitiveJsonPropertyPattern.Replace(value, "$1[redacted]$3");
        if (_loggingOptions.MaxBodyCharacters <= 0 || sanitized.Length <= _loggingOptions.MaxBodyCharacters)
            return sanitized;

        return sanitized[.._loggingOptions.MaxBodyCharacters] + "...[truncated]";
    }

    private static string SanitizePath(string path) =>
        RedactQueryParameter(RedactQueryParameter(path, "pageToken"), "page_token");

    private static string RedactQueryParameter(string path, string parameterName)
    {
        var marker = parameterName + "=";
        var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var valueStart = index + marker.Length;
            var valueEnd = path.IndexOf('&', valueStart);
            if (valueEnd < 0)
                return path[..valueStart] + "[redacted]";

            path = path[..valueStart] + "[redacted]" + path[valueEnd..];
            index = path.IndexOf(marker, valueStart + "[redacted]".Length, StringComparison.OrdinalIgnoreCase);
        }

        return path;
    }

    private static string ResolveOperation(HttpMethod method, string path)
    {
        if (path.Contains("identity", StringComparison.OrdinalIgnoreCase))
            return "identity";

        if (path.Contains("settings", StringComparison.OrdinalIgnoreCase))
            return "settings";

        if (path.Contains(":dailyRollUp", StringComparison.OrdinalIgnoreCase))
            return "daily-rollup";

        if (path.Contains("/dataPoints", StringComparison.OrdinalIgnoreCase))
            return "data-points";

        return method.Method;
    }

    private static string? TryReadDataType(string path)
    {
        const string marker = "dataTypes/";
        var start = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        start += marker.Length;
        var end = path.IndexOfAny(['/', '?'], start);
        if (end < 0)
            end = path.Length;

        return Uri.UnescapeDataString(path[start..end]);
    }

    private static int CountDataPoints(JsonElement root) => ReadDataPoints(root).Count();

    private static bool HasAnyMetricValue(DailyMetricSnapshot snapshot) =>
        snapshot.RestingHeartRateBpm is not null
        || snapshot.HrvRmssdMilliseconds is not null
        || snapshot.DailyVo2MaxMlKgMin is not null
        || snapshot.RunVo2MaxMlKgMin is not null
        || snapshot.SleepEfficiency is not null
        || snapshot.DeepSleepMinutes is not null
        || snapshot.RemSleepMinutes is not null
        || snapshot.ConsumedCaloriesKcal is not null
        || snapshot.CarbohydratesGrams is not null
        || snapshot.FatGrams is not null
        || snapshot.ProteinGrams is not null;

    private static IEnumerable<JsonElement> ReadDataPoints(JsonElement root)
    {
        if (root.TryGetProperty("dataPoints", out var dataPoints) && dataPoints.ValueKind is JsonValueKind.Array)
            return dataPoints.EnumerateArray();

        if (root.TryGetProperty("dailyRollupDataPoints", out var dailyRollups) && dailyRollups.ValueKind is JsonValueKind.Array)
            return dailyRollups.EnumerateArray();

        if (root.TryGetProperty("rollupDataPoints", out var rollupDataPoints) && rollupDataPoints.ValueKind is JsonValueKind.Array)
            return rollupDataPoints.EnumerateArray();

        return [];
    }

    private static bool TryReadSleepMetrics(JsonElement point, out SleepMetrics metrics)
    {
        metrics = default;
        var sleep = FindObject(point, "sleep")
            ?? (FindObject(point, "interval") is not null ? point : null);
        if (sleep is null)
            return false;

        if (!TryReadSleepDate(sleep.Value, out var date))
            return false;

        var summary = FindObject(sleep.Value, "summary", "sleepSummary");
        var minutesAsleep = summary is null ? null : ReadDecimal(summary.Value, "minutesAsleep");
        var minutesInSleepPeriod = summary is null
            ? null
            : ReadDecimal(summary.Value, "minutesInSleepPeriod", "timeInBed");
        var durationMinutes = minutesInSleepPeriod ?? ReadDecimal(sleep.Value, "durationMinutes");

        if (durationMinutes is null
            && TryReadSleepInterval(sleep.Value, out var start, out var end))
        {
            durationMinutes = (decimal)(end - start).TotalMinutes;
        }

        var efficiency = ReadDecimal(sleep.Value, "sleepEfficiency", "sleep_efficiency", "efficiency")
            ?? (summary is null ? null : ReadDecimal(summary.Value, "sleepEfficiency", "sleep_efficiency", "efficiency"));
        if (efficiency is null && minutesAsleep is > 0 && minutesInSleepPeriod is > 0)
            efficiency = minutesAsleep.Value / minutesInSleepPeriod.Value * 100;
        if (efficiency is not null && efficiency is >= 0 and <= 1)
            efficiency *= 100;

        metrics = new SleepMetrics(
            date,
            ReadBool(sleep.Value, "mainSleep", "main_sleep") ?? false,
            durationMinutes ?? minutesAsleep ?? 0,
            efficiency is null ? null : Math.Round(efficiency.Value, 2, MidpointRounding.AwayFromZero),
            ReadSleepStageMinutes(sleep.Value, "DEEP"),
            ReadSleepStageMinutes(sleep.Value, "REM"));
        return true;
    }

    private static bool TryReadSleepDate(JsonElement sleep, out DateOnly date)
    {
        if (TryReadDateStringProperty(sleep, "dateOfSleep", out date)
            || TryReadDateObject(sleep, "date", out date))
        {
            return true;
        }

        var interval = FindObject(sleep, "interval");
        if (interval is not null)
        {
            var civilEndTime = FindObject(interval.Value, "civilEndTime", "civil_end_time");
            if (civilEndTime is not null && TryReadDateObject(civilEndTime.Value, "date", out date)
                || TryReadDateTimeProperty(interval.Value, "endTime", out date)
                || TryReadDateFromNested(interval.Value, out date))
            {
                return true;
            }
        }

        return TryReadDate(sleep, out date);
    }

    private static bool TryReadSleepInterval(
        JsonElement sleep,
        out DateTimeOffset start,
        out DateTimeOffset end)
    {
        start = default;
        end = default;
        var interval = FindObject(sleep, "interval");
        return interval is not null
            && TryReadDateTimeStringProperty(interval.Value, "startTime", out start)
            && TryReadDateTimeStringProperty(interval.Value, "endTime", out end)
            && end >= start;
    }

    private static int? ReadSleepStageMinutes(JsonElement element, string stageType)
    {
        if (element.ValueKind is JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, stageType, StringComparison.OrdinalIgnoreCase))
                {
                    if (TryConvertDecimal(property.Value, out var directMinutes))
                        return (int)Math.Round(directMinutes, MidpointRounding.AwayFromZero);

                    var minutes = ReadDecimal(property.Value, "minutes", "minutesSum", "durationMinutes");
                    if (minutes is not null)
                        return (int)Math.Round(minutes.Value, MidpointRounding.AwayFromZero);
                }

                if (string.Equals(property.Name, "stagesSummary", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind is JsonValueKind.Array)
                {
                    var summaryMinutes = ReadStageSummaryMinutes(property.Value, stageType);
                    if (summaryMinutes is not null)
                        return summaryMinutes;
                }

                if (string.Equals(property.Name, "stages", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind is JsonValueKind.Array)
                {
                    var segmentMinutes = ReadStageSegmentMinutes(property.Value, stageType);
                    if (segmentMinutes is not null)
                        return segmentMinutes;
                }

                var nested = ReadSleepStageMinutes(property.Value, stageType);
                if (nested is not null)
                    return nested;
            }
        }
        else if (element.ValueKind is JsonValueKind.Array)
        {
            var summaryMinutes = ReadStageSummaryMinutes(element, stageType);
            if (summaryMinutes is not null)
                return summaryMinutes;

            var segmentMinutes = ReadStageSegmentMinutes(element, stageType);
            if (segmentMinutes is not null)
                return segmentMinutes;

            foreach (var item in element.EnumerateArray())
            {
                var nested = ReadSleepStageMinutes(item, stageType);
                if (nested is not null)
                    return nested;
            }
        }

        return null;
    }

    private static int? ReadStageSummaryMinutes(JsonElement array, string stageType)
    {
        var total = 0;
        var found = false;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind is not JsonValueKind.Object
                || !item.TryGetProperty("type", out var type)
                || type.ValueKind is not JsonValueKind.String
                || !string.Equals(type.GetString(), stageType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var minutes = ReadDecimal(item, "minutes", "minutesSum", "durationMinutes");
            if (minutes is not null)
            {
                total += (int)Math.Round(minutes.Value, MidpointRounding.AwayFromZero);
                found = true;
            }
        }

        return found ? total : null;
    }

    private static int? ReadStageSegmentMinutes(JsonElement array, string stageType)
    {
        var total = 0;
        var found = false;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind is not JsonValueKind.Object
                || !item.TryGetProperty("type", out var type)
                || type.ValueKind is not JsonValueKind.String
                || !string.Equals(type.GetString(), stageType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryReadDateTimeStringProperty(item, "startTime", out var start)
                && TryReadDateTimeStringProperty(item, "endTime", out var end)
                && end >= start)
            {
                total += (int)Math.Round((end - start).TotalMinutes, MidpointRounding.AwayFromZero);
                found = true;
            }
        }

        return found ? total : null;
    }

    private static JsonElement? FindObject(JsonElement element, params string[] propertyNames)
    {
        if (element.ValueKind is JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (propertyNames.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase))
                    && property.Value.ValueKind is JsonValueKind.Object)
                {
                    return property.Value;
                }

                var nested = FindObject(property.Value, propertyNames);
                if (nested is not null)
                    return nested;
            }
        }
        else if (element.ValueKind is JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindObject(item, propertyNames);
                if (nested is not null)
                    return nested;
            }
        }

        return null;
    }

    private static bool? ReadBool(JsonElement element, params string[] propertyNames)
    {
        if (element.ValueKind is JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (propertyNames.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase))
                    && property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    return property.Value.GetBoolean();
                }

                var nested = ReadBool(property.Value, propertyNames);
                if (nested is not null)
                    return nested;
            }
        }
        else if (element.ValueKind is JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = ReadBool(item, propertyNames);
                if (nested is not null)
                    return nested;
            }
        }

        return null;
    }

    private static bool TryReadDateStringProperty(JsonElement element, string propertyName, out DateOnly date)
    {
        date = default;
        if (element.ValueKind is JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind is JsonValueKind.String
                    && DateOnly.TryParse(
                        property.Value.GetString(),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out date))
                {
                    return true;
                }

                if (TryReadDateStringProperty(property.Value, propertyName, out date))
                    return true;
            }
        }
        else if (element.ValueKind is JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryReadDateStringProperty(item, propertyName, out date))
                    return true;
            }
        }

        return false;
    }

    private static bool TryReadDateTimeProperty(JsonElement element, string propertyName, out DateOnly date)
    {
        date = default;
        if (!TryReadDateTimeStringProperty(element, propertyName, out var parsed))
            return false;

        date = DateOnly.FromDateTime(parsed.UtcDateTime);
        return true;
    }

    private static bool TryReadDateTimeStringProperty(
        JsonElement element,
        string propertyName,
        out DateTimeOffset dateTime)
    {
        dateTime = default;
        if (element.ValueKind is not JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var value)
            || value.ValueKind is not JsonValueKind.String)
        {
            return false;
        }

        return DateTimeOffset.TryParse(
            value.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out dateTime);
    }

    private static IEnumerable<(DateOnly Start, DateOnly End)> ChunkRange(DateOnly startDate, DateOnly endDate, int maxDays)
    {
        var current = startDate;
        while (current <= endDate)
        {
            var chunkEnd = current.AddDays(maxDays - 1);
            if (chunkEnd > endDate)
                chunkEnd = endDate;

            yield return (current, chunkEnd);
            current = chunkEnd.AddDays(1);
        }
    }

    private static object ToDateObject(DateOnly date) => new { year = date.Year, month = date.Month, day = date.Day };

    private static bool TryReadDate(JsonElement element, out DateOnly date)
    {
        if (TryReadDateObject(element, "date", out date)
            || TryReadDateObject(element, "startDate", out date)
            || TryReadDateObject(element, "civilStartDate", out date)
            || TryReadDateObject(element, "civilDate", out date))
            return true;

        if (TryReadDateFromNested(element, out date))
            return true;

        var instant = FindString(element, "startTime", "time", "dateTime");
        return instant is not null
            && DateTimeOffset.TryParse(instant, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            && (date = DateOnly.FromDateTime(parsed.UtcDateTime)) != default;
    }

    private static bool TryReadDateObject(JsonElement element, string propertyName, out DateOnly date)
    {
        date = default;
        if (element.ValueKind is not JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var dateElement))
            return false;

        if (dateElement.ValueKind is JsonValueKind.String)
        {
            return DateOnly.TryParseExact(
                dateElement.GetString(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);
        }

        if (dateElement.ValueKind is not JsonValueKind.Object
            || !TryReadIntProperty(dateElement, "year", out var year)
            || !TryReadIntProperty(dateElement, "month", out var month)
            || !TryReadIntProperty(dateElement, "day", out var day))
            return false;

        return DateOnly.TryParseExact(
            $"{year:D4}-{month:D2}-{day:D2}",
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    private static bool TryReadDateFromNested(JsonElement element, out DateOnly date)
    {
        date = default;
        if (element.ValueKind is JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (TryReadDateObject(property.Value, "date", out date)
                    || TryReadDateFromNested(property.Value, out date))
                    return true;
            }
        }
        else if (element.ValueKind is JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryReadDateFromNested(item, out date))
                    return true;
            }
        }

        return false;
    }

    private static bool TryReadIntProperty(JsonElement element, string propertyName, out int value)
    {
        value = default;
        return element.ValueKind is JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.Number
            && property.TryGetInt32(out value);
    }

    private static int? ReadInt(JsonElement element, params string[] propertyNames)
    {
        var value = ReadDecimal(element, propertyNames);
        return value is null ? null : (int)Math.Round(value.Value, MidpointRounding.AwayFromZero);
    }

    private static decimal? ReadDecimal(JsonElement element, params string[] propertyNames)
    {
        if (element.ValueKind is JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (propertyNames.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase))
                    && TryConvertDecimal(property.Value, out var directValue))
                    return directValue;

                var nestedValue = ReadDecimal(property.Value, propertyNames);
                if (nestedValue is not null)
                    return nestedValue;
            }
        }
        else if (element.ValueKind is JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nestedValue = ReadDecimal(item, propertyNames);
                if (nestedValue is not null)
                    return nestedValue;
            }
        }

        return null;
    }

    private static decimal? ReadNestedDecimal(JsonElement element, string objectName, string valueName)
    {
        if (element.ValueKind is JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, objectName, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind is JsonValueKind.Object)
                {
                    var result = ReadDecimal(property.Value, valueName);
                    if (result is not null)
                        return result;
                }

                var nested = ReadNestedDecimal(property.Value, objectName, valueName);
                if (nested is not null)
                    return nested;
            }
        }
        else if (element.ValueKind is JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = ReadNestedDecimal(item, objectName, valueName);
                if (nested is not null)
                    return nested;
            }
        }

        return null;
    }

    private static decimal? ReadNutrientGrams(JsonElement element, string nutrientName)
    {
        if (element.ValueKind is JsonValueKind.Object)
        {
            if (element.TryGetProperty("nutrient", out var nutrientElement)
                && string.Equals(nutrientElement.GetString(), nutrientName, StringComparison.OrdinalIgnoreCase))
                return ReadDecimal(element, "gramsSum", "grams", "value");

            foreach (var property in element.EnumerateObject())
            {
                var nested = ReadNutrientGrams(property.Value, nutrientName);
                if (nested is not null)
                    return nested;
            }
        }
        else if (element.ValueKind is JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = ReadNutrientGrams(item, nutrientName);
                if (nested is not null)
                    return nested;
            }
        }

        return null;
    }

    private static string? FindString(JsonElement element, params string[] propertyNames)
    {
        if (element.ValueKind is JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (propertyNames.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase))
                    && property.Value.ValueKind is JsonValueKind.String)
                    return property.Value.GetString();

                var nested = FindString(property.Value, propertyNames);
                if (nested is not null)
                    return nested;
            }
        }
        else if (element.ValueKind is JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindString(item, propertyNames);
                if (nested is not null)
                    return nested;
            }
        }

        return null;
    }

    private static bool TryConvertDecimal(JsonElement element, out decimal value)
    {
        value = default;
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDecimal(out value),
            JsonValueKind.String => decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value),
            _ => false
        };
    }
}

internal readonly record struct SleepMetrics(
    DateOnly Date,
    bool IsMainSleep,
    decimal DurationMinutes,
    decimal? SleepEfficiency,
    int? DeepSleepMinutes,
    int? RemSleepMinutes);

internal sealed class GoogleHealthApiException(HttpStatusCode statusCode, string message) : HttpRequestException(message, null, statusCode)
{
    public bool IsAuthorizationFailure => statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}
