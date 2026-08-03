namespace HealthMetrics.Application.Models;

public sealed class UserDataOwnership
{
    public int Id { get; set; }

    public string UserKey { get; set; } = LocalUser.Key;

    public required string GoogleUserId { get; set; }

    public string? GoogleEmail { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
