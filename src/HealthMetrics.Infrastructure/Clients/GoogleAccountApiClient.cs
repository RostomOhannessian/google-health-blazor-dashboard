using System.Net.Http.Headers;
using System.Text.Json;

namespace HealthMetrics.Infrastructure.Clients;

internal sealed class GoogleAccountApiClient(HttpClient httpClient)
{
    public async Task<string> GetEmailAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "v1/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Google account email lookup failed with status {(int)response.StatusCode}.", null, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (!root.TryGetProperty("email", out var emailElement)
            || emailElement.ValueKind is not JsonValueKind.String)
            throw new InvalidOperationException("Google account response did not include an email address.");

        var email = emailElement.GetString();
        return !string.IsNullOrWhiteSpace(email)
            ? email
            : throw new InvalidOperationException("Google account response did not include an email address.");
    }
}
