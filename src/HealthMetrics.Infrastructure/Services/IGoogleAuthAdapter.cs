using Google.Apis.Auth.OAuth2.Responses;

namespace HealthMetrics.Infrastructure.Services;

/// <summary>
/// Abstracts the Google OAuth code-flow operations so they can be replaced in tests
/// without requiring a live Google endpoint.
/// </summary>
internal interface IGoogleAuthAdapter
{
    Task<Uri> BuildAuthorizationUriAsync(string state, CancellationToken cancellationToken);

    Task<TokenResponse> ExchangeCodeForTokenAsync(string code, CancellationToken cancellationToken);

    Task<TokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken);

    Task RevokeTokenAsync(string token, CancellationToken cancellationToken);
}
