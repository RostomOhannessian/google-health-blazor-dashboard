using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HealthMetrics.Application.Models;

namespace HealthMetrics.Infrastructure.Clients;

internal sealed class GoogleHealthApiClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> GetIdentityAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, "users/me/identity", accessToken, null, cancellationToken);
        var root = doc.RootElement;

        return FindString(root, "googleUserId", "google_user_id", "userId", "id")
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

        return snapshots.Values.OrderBy(snapshot => snapshot.MetricDate).ToList();
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
        var filter = $"{filterPrefix}.date >= \"{startDate:yyyy-MM-dd}\" AND {filterPrefix}.date <= \"{endDate:yyyy-MM-dd}\"";
        var pageToken = string.Empty;

        do
        {
            var path = new StringBuilder($"users/me/dataTypes/{dataType}/dataPoints?pageSize=10000&filter={Uri.EscapeDataString(filter)}");
            if (!string.IsNullOrWhiteSpace(pageToken))
                path.Append("&pageToken=").Append(Uri.EscapeDataString(pageToken));

            using var doc = await SendJsonAsync(HttpMethod.Get, path.ToString(), accessToken, null, cancellationToken);
            foreach (var point in ReadDataPoints(doc.RootElement))
            {
                if (TryReadDate(point, out var date) && snapshots.TryGetValue(date, out var snapshot))
                    apply(snapshot, point);
            }

            pageToken = FindString(doc.RootElement, "nextPageToken") ?? string.Empty;
        }
        while (!string.IsNullOrWhiteSpace(pageToken));
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
        foreach (var (chunkStart, chunkEnd) in ChunkRange(startDate, endDate, maxDays: 14))
        {
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

            foreach (var point in ReadDataPoints(doc.RootElement))
            {
                if (TryReadDate(point, out var date) && snapshots.TryGetValue(date, out var snapshot))
                    apply(snapshot, point);
            }
        }
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
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (jsonBody is not null)
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var snippet = responseBody.Length > 500 ? responseBody[..500] : responseBody;
            throw new GoogleHealthApiException(response.StatusCode, $"Google Health API call failed for '{path}' with status {(int)response.StatusCode}: {snippet}");
        }

        return JsonDocument.Parse(responseBody);
    }

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
