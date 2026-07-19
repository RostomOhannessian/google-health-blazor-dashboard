namespace HealthMetrics.Application.Models;

public sealed record HealthConnectionStatus(
    bool IsConnected,
    string? GoogleUserId,
    DateTimeOffset? AccessTokenExpiresAtUtc,
    DateTimeOffset? RefreshTokenExpiresAtUtc,
    DateTimeOffset? LastSuccessfulSyncAtUtc);
