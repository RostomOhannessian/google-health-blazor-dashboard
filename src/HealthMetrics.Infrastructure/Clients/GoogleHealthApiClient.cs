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

    public async Task<string> GetUserTimeZoneAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, "users/me/settings", accessToken, null, cancellationToken);
        return FindString(doc.RootElement, "timeZone", "time_zone", "timezone") ?? "UTC";
    }

    public async Task<IReadOnlyList<DailyMetricSnapshot>> FetchDailyMetricsAsync(
        string accessToken,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
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

        var timeZone = await GetUserTimeZoneAsync(accessToken, cancellationToken);

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
            (snapshot, point) => snapshot.HrvRmssdMilliseconds = ReadDecimal(point, "rmssdMilliseconds", "rmssdMillis", "dailyRmssd", "rmssd"),
            cancellationToken);

        await MergeDailyRollupAsync(
            snapshots,
            "run-vo2-max",
            startDate,
            endDate,
            timeZone,
            accessToken,
            (snapshot, point) => snapshot.RunVo2MaxMlKgMin = ReadDecimal(point, "rateAvg", "vo2Max", "score"),
            cancellationToken);

        await MergeDailyRollupAsync(
            snapshots,
            "nutrition-log",
            startDate,
            endDate,
            timeZone,
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
        string timeZone,
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
                civilTimeInterval = new
                {
                    startDate = ToDateObject(chunkStart),
                    endDate = ToDateObject(chunkEnd.AddDays(1)),
                    timeZone
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

    private static void ApplyNutrition(DailyMetricSnapshot snapshot, JsonElement point)
    {
        snapshot.ConsumedCaloriesKcal = ReadInt(point, "kcalSum", "kilocaloriesSum", "caloriesKcal");
        snapshot.CarbohydratesGrams = ReadNestedDecimal(point, "totalCarbohydrate", "gramsSum")
            ?? ReadNestedDecimal(point, "carbohydrate", "gramsSum");
        snapshot.FatGrams = ReadNestedDecimal(point, "totalFat", "gramsSum")
            ?? ReadNestedDecimal(point, "fat", "gramsSum");
        snapshot.ProteinGrams = ReadNutrientGrams(point, "PROTEIN")
            ?? ReadNestedDecimal(point, "protein", "gramsSum");
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
        || snapshot.RunVo2MaxMlKgMin is not null
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

        return [];
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
            || TryReadDateObject(element, "civilStartDate", out date))
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
        if (!element.TryGetProperty(propertyName, out var dateElement) || dateElement.ValueKind is not JsonValueKind.Object)
            return false;

        if (!TryReadIntProperty(dateElement, "year", out var year)
            || !TryReadIntProperty(dateElement, "month", out var month)
            || !TryReadIntProperty(dateElement, "day", out var day))
            return false;

        date = new DateOnly(year, month, day);
        return true;
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
        return element.TryGetProperty(propertyName, out var property)
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

internal sealed class GoogleHealthApiException(HttpStatusCode statusCode, string message) : HttpRequestException(message, null, statusCode)
{
    public bool IsAuthorizationFailure => statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}
