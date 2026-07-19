using FitbitMetrics.Application.Models;

namespace FitbitMetrics.Application.Interfaces;

public interface IFitbitOAuthService
{
    Task<Uri> BuildAuthorizationUriAsync(string state, CancellationToken cancellationToken = default);

    Task HandleAuthorizationCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken = default);

    Task<FitbitConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken = default);
}
