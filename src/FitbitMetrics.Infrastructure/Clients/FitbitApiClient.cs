using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FitbitMetrics.Application.Models;

namespace FitbitMetrics.Infrastructure.Clients;

internal sealed class FitbitApiClient(HttpClient httpClient)
{
    public async Task<DailyMetricSnapshot> FetchMetricsForDateAsync(
        string accessToken,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var snapshot = new DailyMetricSnapshot
        {
            UserKey = DemoUser.Key,
            MetricDate = date,
            CapturedAtUtc = DateTimeOffset.UtcNow
        };

        var heartRateDoc = await GetDocumentAsync(
            $"/1/user/-/activities/heart/date/{date:yyyy-MM-dd}/1d.json",
            accessToken,
            cancellationToken);
        if (heartRateDoc is not null)
        {
            snapshot.RestingHeartRateBpm = ReadNestedInt(
                heartRateDoc.RootElement,
                "activities-heart",
                "value",
                "restingHeartRate");
        }

        var hrvDoc = await GetDocumentAsync(
            $"/1/user/-/hrv/date/{date:yyyy-MM-dd}.json",
            accessToken,
            cancellationToken);
        if (hrvDoc is not null)
        {
            snapshot.HrvRmssdMilliseconds = ReadNestedDecimal(hrvDoc.RootElement, "hrv", "value", "dailyRmssd");
        }

        var vo2Doc = await GetDocumentAsync(
            $"/1/user/-/cardioscore/date/{date:yyyy-MM-dd}.json",
            accessToken,
            cancellationToken);
        if (vo2Doc is not null)
        {
            snapshot.Vo2MaxMlKgMin = ReadNestedDecimal(vo2Doc.RootElement, "cardioScore", "value", "vo2Max")
                ?? ReadNestedDecimal(vo2Doc.RootElement, "cardioScore", "value", "score");
        }

        var nutritionDoc = await GetDocumentAsync(
            $"/1/user/-/foods/log/date/{date:yyyy-MM-dd}.json",
            accessToken,
            cancellationToken);
        if (nutritionDoc is not null && nutritionDoc.RootElement.TryGetProperty("summary", out var summary))
        {
            snapshot.ConsumedCaloriesKcal = ReadInt(summary, "calories");
            snapshot.CarbohydratesGrams = ReadDecimal(summary, "carbs");
            snapshot.FatGrams = ReadDecimal(summary, "fat");
            snapshot.ProteinGrams = ReadDecimal(summary, "protein");
            snapshot.FiberGrams = ReadDecimal(summary, "fiber");
            snapshot.SodiumMilligrams = ReadDecimal(summary, "sodium");
            snapshot.PotassiumMilligrams = ReadDecimal(summary, "potassium");
            snapshot.CalciumMilligrams = ReadDecimal(summary, "calcium");
            snapshot.IronMilligrams = ReadDecimal(summary, "iron");
        }

        return snapshot;
    }

    private async Task<JsonDocument?> GetDocumentAsync(
        string path,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Fitbit API call failed for '{path}' with status {(int)response.StatusCode}: {errorBody}");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
    }

    private static int? ReadNestedInt(JsonElement root, string arrayName, string objectName, string fieldName)
    {
        if (!root.TryGetProperty(arrayName, out var arrayElement) || arrayElement.GetArrayLength() == 0)
        {
            return null;
        }

        var firstElement = arrayElement[0];
        if (!firstElement.TryGetProperty(objectName, out var nestedObject))
        {
            return null;
        }

        return ReadInt(nestedObject, fieldName);
    }

    private static decimal? ReadNestedDecimal(JsonElement root, string arrayName, string objectName, string fieldName)
    {
        if (!root.TryGetProperty(arrayName, out var arrayElement) || arrayElement.GetArrayLength() == 0)
        {
            return null;
        }

        var firstElement = arrayElement[0];
        if (!firstElement.TryGetProperty(objectName, out var nestedObject))
        {
            return null;
        }

        return ReadDecimal(nestedObject, fieldName);
    }

    private static int? ReadInt(JsonElement element, string fieldName)
    {
        if (!element.TryGetProperty(fieldName, out var fieldValue))
        {
            return null;
        }

        if (fieldValue.ValueKind is JsonValueKind.Number && fieldValue.TryGetInt32(out var intValue))
        {
            return intValue;
        }

        if (fieldValue.ValueKind is JsonValueKind.String && int.TryParse(fieldValue.GetString(), out var parsedValue))
        {
            return parsedValue;
        }

        return null;
    }

    private static decimal? ReadDecimal(JsonElement element, string fieldName)
    {
        if (!element.TryGetProperty(fieldName, out var fieldValue))
        {
            return null;
        }

        if (fieldValue.ValueKind is JsonValueKind.Number && fieldValue.TryGetDecimal(out var decimalValue))
        {
            return decimalValue;
        }

        if (fieldValue.ValueKind is JsonValueKind.String && decimal.TryParse(fieldValue.GetString(), out var parsedValue))
        {
            return parsedValue;
        }

        return null;
    }
}
