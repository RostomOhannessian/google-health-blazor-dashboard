using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FitbitMetrics.Application.Interfaces;
using FitbitMetrics.Application.Models;
using FitbitMetrics.Infrastructure.Options;
using FitbitMetrics.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FitbitMetrics.Infrastructure.Services;

internal sealed class FitbitOAuthService(
    FitbitDbContext dbContext,
    IHttpClientFactory httpClientFactory,
    IOptions<FitbitApiOptions> options,
    IDataProtectionProvider dataProtectionProvider) : IFitbitOAuthService
{
    private readonly FitbitApiOptions _options = options.Value;
    private readonly IDataProtector _tokenProtector =
        dataProtectionProvider.CreateProtector("FitbitMetrics.Tokens.v1");

    public Task<Uri> BuildAuthorizationUriAsync(string state, CancellationToken cancellationToken = default)
    {
        var scopeValue = string.Join(' ', _options.Scopes);
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"]     = _options.ClientId,
            ["redirect_uri"]  = _options.RedirectUri,
            ["scope"]         = scopeValue,
            ["expires_in"]    = "604800",
            ["state"]         = state
        };

        var queryString = string.Join(
            "&",
            query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        return Task.FromResult(new Uri($"https://www.fitbit.com/oauth2/authorize?{queryString}"));
    }

    public async Task HandleAuthorizationCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var token = await RequestTokenAsync(
            new Dictionary<string, string>
            {
                ["grant_type"]   = "authorization_code",
                ["code"]         = code,
                ["redirect_uri"] = _options.RedirectUri
            },
            cancellationToken);

        var connection = await dbContext.FitbitConnections
            .SingleOrDefaultAsync(item => item.UserKey == DemoUser.Key, cancellationToken);

        if (connection is null)
        {
            connection = new FitbitConnection
            {
                UserKey                  = DemoUser.Key,
                FitbitUserId             = token.UserId,
                AccessToken              = _tokenProtector.Protect(token.AccessToken),
                RefreshToken             = _tokenProtector.Protect(token.RefreshToken),
                Scope                    = token.Scope,
                AccessTokenExpiresAtUtc  = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresInSeconds),
                CreatedAtUtc             = DateTimeOffset.UtcNow,
                UpdatedAtUtc             = DateTimeOffset.UtcNow
            };

            dbContext.FitbitConnections.Add(connection);
        }
        else
        {
            connection.FitbitUserId            = token.UserId;
            connection.AccessToken             = _tokenProtector.Protect(token.AccessToken);
            connection.RefreshToken            = _tokenProtector.Protect(token.RefreshToken);
            connection.Scope                   = token.Scope;
            connection.AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresInSeconds);
            connection.UpdatedAtUtc            = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var connection = await dbContext.FitbitConnections
            .SingleOrDefaultAsync(item => item.UserKey == DemoUser.Key, cancellationToken)
            ?? throw new InvalidOperationException("Fitbit is not connected yet.");

        if (connection.AccessTokenExpiresAtUtc <= DateTimeOffset.UtcNow.AddMinutes(2))
        {
            var token = await RequestTokenAsync(
                new Dictionary<string, string>
                {
                    ["grant_type"]    = "refresh_token",
                    ["refresh_token"] = _tokenProtector.Unprotect(connection.RefreshToken)
                },
                cancellationToken);

            connection.AccessToken             = _tokenProtector.Protect(token.AccessToken);
            connection.RefreshToken            = _tokenProtector.Protect(token.RefreshToken);
            connection.Scope                   = token.Scope;
            connection.AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresInSeconds);
            connection.UpdatedAtUtc            = DateTimeOffset.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return _tokenProtector.Unprotect(connection.AccessToken);
    }

    public async Task<FitbitConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken = default)
    {
        var connection = await dbContext.FitbitConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserKey == DemoUser.Key, cancellationToken);

        return connection is null
            ? new FitbitConnectionStatus(false, null, null)
            : new FitbitConnectionStatus(
                true,
                connection.AccessTokenExpiresAtUtc,
                connection.LastSuccessfulSyncAtUtc);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var connection = await dbContext.FitbitConnections
            .SingleOrDefaultAsync(item => item.UserKey == DemoUser.Key, cancellationToken);

        if (connection is null)
            return;

        // Attempt to revoke the token with Fitbit — best effort; ignore failures.
        try
        {
            var refreshToken = _tokenProtector.Unprotect(connection.RefreshToken);
            await RevokeTokenAsync(refreshToken, cancellationToken);
        }
        catch
        {
            // Revocation failure is non-fatal; continue with local cleanup.
        }

        dbContext.FitbitConnections.Remove(connection);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RevokeTokenAsync(string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.fitbit.com/oauth2/revoke");
        request.Content    = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token });
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", BuildBasicAuthHeader());

        var httpClient = httpClientFactory.CreateClient();
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<TokenResult> RequestTokenAsync(
        IReadOnlyDictionary<string, string> content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.fitbit.com/oauth2/token");
        request.Content = new FormUrlEncodedContent(content);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", BuildBasicAuthHeader());

        var httpClient = httpClientFactory.CreateClient();
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Limit error snippet; never log the raw request (contains credentials).
            var snippet = responseBody.Length > 200 ? responseBody[..200] : responseBody;
            throw new HttpRequestException(
                $"Fitbit token request failed ({(int)response.StatusCode}): {snippet}");
        }

        return TokenResult.Deserialize(responseBody)
            ?? throw new InvalidOperationException("Fitbit token response was empty.");
    }

    private string BuildBasicAuthHeader()
    {
        var credentials = $"{_options.ClientId}:{_options.ClientSecret}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
    }

    private sealed record TokenResult(
        string AccessToken,
        int ExpiresInSeconds,
        string RefreshToken,
        string Scope,
        string UserId)
    {
        public static TokenResult? Deserialize(string json)
        {
            return JsonSerializer.Deserialize<TokenResultDto>(json)?.ToTokenResult();
        }

        private sealed record TokenResultDto(
            string access_token,
            int expires_in,
            string refresh_token,
            string scope,
            string user_id)
        {
            public TokenResult ToTokenResult() => new(
                access_token,
                expires_in,
                refresh_token,
                scope,
                user_id);
        }
    }
}
