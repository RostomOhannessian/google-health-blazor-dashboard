namespace FitbitMetrics.Application.Models;

public sealed record FitbitConnectionStatus(
    bool IsConnected,
    DateTimeOffset? AccessTokenExpiresAtUtc,
    DateTimeOffset? LastSuccessfulSyncAtUtc);
