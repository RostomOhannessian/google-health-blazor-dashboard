using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using HealthMetrics.Application.Models;
using HealthMetrics.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace HealthMetrics.Infrastructure.Services;

internal sealed class DefaultGoogleAuthAdapter(IOptions<GoogleHealthApiOptions> options) : IGoogleAuthAdapter
{
    private readonly GoogleHealthApiOptions _options = options.Value;

    public Task<Uri> BuildAuthorizationUriAsync(string state, CancellationToken cancellationToken)
    {
        var request = CreateFlow("consent").CreateAuthorizationCodeRequest(_options.RedirectUri);
        request.State = state;
        return Task.FromResult(request.Build());
    }

    public Task<TokenResponse> ExchangeCodeForTokenAsync(string code, CancellationToken cancellationToken)
        => CreateFlow("consent").ExchangeCodeForTokenAsync(LocalUser.Key, code, _options.RedirectUri, cancellationToken);

    public Task<TokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
        => CreateFlow(null).RefreshTokenAsync(LocalUser.Key, refreshToken, cancellationToken);

    public Task RevokeTokenAsync(string token, CancellationToken cancellationToken)
        => CreateFlow(null).RevokeTokenAsync(LocalUser.Key, token, cancellationToken);

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
}
