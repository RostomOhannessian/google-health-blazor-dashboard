using HealthMetrics.Application.Models;

namespace HealthMetrics.Application.Interfaces;

public interface IHealthAuthorizationService
{
    Task<Uri> BuildAuthorizationUriAsync(string state, CancellationToken cancellationToken = default);

    Task HandleAuthorizationCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken = default);

    Task<HealthConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
