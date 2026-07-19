namespace HealthMetrics.Application.Models;

public sealed class HealthConnection
{
    public int Id { get; set; }

    public string UserKey { get; set; } = LocalUser.Key;

    public required string GoogleUserId { get; set; }

    public required string AccessToken { get; set; }

    public required string RefreshToken { get; set; }

    public required string Scope { get; set; }

    public DateTimeOffset AccessTokenExpiresAtUtc { get; set; }

    public DateTimeOffset? RefreshTokenExpiresAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastSuccessfulSyncAtUtc { get; set; }
}
