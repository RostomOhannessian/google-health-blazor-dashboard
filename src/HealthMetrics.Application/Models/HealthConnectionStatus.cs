namespace HealthMetrics.Application.Models;

public sealed record HealthConnectionStatus(
    bool IsConnected,
    string? GoogleUserId,
    string? GoogleEmail,
    DateTimeOffset? AccessTokenExpiresAtUtc,
    DateTimeOffset? RefreshTokenExpiresAtUtc,
    DateTimeOffset? LastSuccessfulSyncAtUtc);
