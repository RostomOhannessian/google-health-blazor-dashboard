using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using HealthMetrics.Application.Interfaces;
using HealthMetrics.Application.Models;
using HealthMetrics.Infrastructure.Clients;
using HealthMetrics.Infrastructure.Options;
using HealthMetrics.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HealthMetrics.Infrastructure.Services;

internal sealed class GoogleHealthAuthorizationService(
    HealthMetricsDbContext dbContext,
    GoogleHealthApiClient googleHealthApiClient,
    IOptions<GoogleHealthApiOptions> options,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<GoogleHealthAuthorizationService> logger) : IHealthAuthorizationService
{
    private readonly GoogleHealthApiOptions _options = options.Value;
    private readonly IDataProtector _tokenProtector =
        dataProtectionProvider.CreateProtector("HealthMetrics.GoogleTokens.v1");

    public Task<Uri> BuildAuthorizationUriAsync(string state, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Building Google Health authorization URL for {ScopeCount} scope(s).", _options.Scopes.Length);
        var request = CreateFlow(prompt: "consent").CreateAuthorizationCodeRequest(_options.RedirectUri);
        request.State = state;

        var uri = request.Build();
        logger.LogInformation("Google Health authorization URL built.");
        return Task.FromResult(uri);
    }

    public async Task HandleAuthorizationCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Google Health authorization code exchange started.");
        TokenResponse token;
        try
        {
            token = await CreateFlow(prompt: "consent")
                .ExchangeCodeForTokenAsync(LocalUser.Key, code, _options.RedirectUri, cancellationToken);
        }
        catch (TokenResponseException ex)
        {
            logger.LogError(ex, "Google Health authorization code exchange failed.");
            throw;
        }

        if (string.IsNullOrWhiteSpace(token.AccessToken) || string.IsNullOrWhiteSpace(token.RefreshToken))
            throw new InvalidOperationException("Google OAuth token response did not include both access and refresh tokens.");

        logger.LogInformation("Google Health authorization code exchange succeeded.");
        var googleUserId = await googleHealthApiClient.GetIdentityAsync(token.AccessToken, cancellationToken);
        var connection = await dbContext.HealthConnections
            .SingleOrDefaultAsync(item => item.UserKey == LocalUser.Key, cancellationToken);
        var isNewConnection = connection is null;

        if (connection is null)
        {
            connection = new HealthConnection
            {
                UserKey = LocalUser.Key,
                GoogleUserId = googleUserId,
                AccessToken = _tokenProtector.Protect(token.AccessToken),
                RefreshToken = _tokenProtector.Protect(token.RefreshToken),
                Scope = NormalizeScope(token.Scope),
                AccessTokenExpiresAtUtc = CalculateExpiry(token),
                RefreshTokenExpiresAtUtc = CalculateRefreshTokenExpiry(token),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            dbContext.HealthConnections.Add(connection);
        }
        else
        {
            connection.GoogleUserId = googleUserId;
            connection.AccessToken = _tokenProtector.Protect(token.AccessToken);
            connection.RefreshToken = _tokenProtector.Protect(token.RefreshToken);
            connection.Scope = NormalizeScope(token.Scope);
            connection.AccessTokenExpiresAtUtc = CalculateExpiry(token);
            connection.RefreshTokenExpiresAtUtc = CalculateRefreshTokenExpiry(token);
            connection.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Google Health connection {ConnectionAction}. ScopeCount: {ScopeCount}; AccessTokenExpiresAtUtc: {AccessTokenExpiresAtUtc}; RefreshTokenExpiresAtUtc: {RefreshTokenExpiresAtUtc}.",
            isNewConnection ? "created" : "replaced",
            connection.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
            connection.AccessTokenExpiresAtUtc,
            connection.RefreshTokenExpiresAtUtc);
    }

    public async Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var connection = await dbContext.HealthConnections
            .SingleOrDefaultAsync(item => item.UserKey == LocalUser.Key, cancellationToken)
            ?? throw new InvalidOperationException("Google Health is not connected yet.");

        if (connection.AccessTokenExpiresAtUtc <= DateTimeOffset.UtcNow.AddMinutes(2))
        {
            logger.LogInformation("Google Health access token refresh started. AccessTokenExpiresAtUtc: {AccessTokenExpiresAtUtc}.", connection.AccessTokenExpiresAtUtc);
            var refreshToken = _tokenProtector.Unprotect(connection.RefreshToken);
            TokenResponse token;
            try
            {
                token = await CreateFlow(prompt: null)
                    .RefreshTokenAsync(LocalUser.Key, refreshToken, cancellationToken);
            }
            catch (TokenResponseException ex)
            {
                logger.LogError(ex, "Google Health access token refresh failed.");
                throw;
            }

            if (string.IsNullOrWhiteSpace(token.AccessToken))
                throw new InvalidOperationException("Google OAuth refresh response did not include an access token.");

            connection.AccessToken = _tokenProtector.Protect(token.AccessToken);
            if (!string.IsNullOrWhiteSpace(token.RefreshToken))
                connection.RefreshToken = _tokenProtector.Protect(token.RefreshToken);

            connection.Scope = NormalizeScope(token.Scope, connection.Scope);
            connection.AccessTokenExpiresAtUtc = CalculateExpiry(token);
            connection.RefreshTokenExpiresAtUtc = CalculateRefreshTokenExpiry(token) ?? connection.RefreshTokenExpiresAtUtc;
            connection.UpdatedAtUtc = DateTimeOffset.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Google Health access token refresh succeeded. AccessTokenExpiresAtUtc: {AccessTokenExpiresAtUtc}; RefreshTokenExpiresAtUtc: {RefreshTokenExpiresAtUtc}.",
                connection.AccessTokenExpiresAtUtc,
                connection.RefreshTokenExpiresAtUtc);
        }

        return _tokenProtector.Unprotect(connection.AccessToken);
    }

    public async Task<HealthConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken = default)
    {
        var connection = await dbContext.HealthConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserKey == LocalUser.Key, cancellationToken);

        return connection is null
            ? new HealthConnectionStatus(false, null, null, null, null)
            : new HealthConnectionStatus(
                true,
                connection.GoogleUserId,
                connection.AccessTokenExpiresAtUtc,
                connection.RefreshTokenExpiresAtUtc,
                connection.LastSuccessfulSyncAtUtc);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var connection = await dbContext.HealthConnections
            .SingleOrDefaultAsync(item => item.UserKey == LocalUser.Key, cancellationToken);

        if (connection is null)
        {
            logger.LogInformation("Google Health disconnect requested, but no connection exists.");
            return;
        }

        logger.LogInformation("Google Health disconnect started.");
        var refreshToken = _tokenProtector.Unprotect(connection.RefreshToken);
        try
        {
            await CreateFlow(prompt: null).RevokeTokenAsync(LocalUser.Key, refreshToken, cancellationToken);
            logger.LogInformation("Google Health remote token revocation succeeded.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Google Health remote token revocation failed; local disconnect will continue.");
        }

        dbContext.HealthConnections.Remove(connection);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Google Health local connection cleanup completed.");
    }

    private GoogleAuthorizationCodeFlow CreateFlow(string? prompt) =>
        new(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = _options.ClientId,
                ClientSecret = _options.ClientSecret
            },
            Scopes = _options.Scopes,
            IncludeGrantedScopes = true,
            Prompt = prompt
        });

    private static DateTimeOffset CalculateExpiry(TokenResponse token)
    {
        var issuedAt = token.IssuedUtc == default
            ? DateTimeOffset.UtcNow
            : new DateTimeOffset(DateTime.SpecifyKind(token.IssuedUtc, DateTimeKind.Utc));

        return issuedAt.AddSeconds(token.ExpiresInSeconds ?? 3600);
    }

    private static DateTimeOffset? CalculateRefreshTokenExpiry(TokenResponse token)
    {
        var property = token.GetType().GetProperty("RefreshTokenExpiresInSeconds");
        if (property?.GetValue(token) is not long seconds)
            return null;

        return DateTimeOffset.UtcNow.AddSeconds(seconds);
    }

    private string NormalizeScope(string? scope, string? fallback = null)
        => string.IsNullOrWhiteSpace(scope) ? fallback ?? string.Join(' ', _options.Scopes) : scope;
}
