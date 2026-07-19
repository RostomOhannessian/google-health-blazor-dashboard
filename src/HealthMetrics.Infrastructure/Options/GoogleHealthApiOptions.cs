using System.ComponentModel.DataAnnotations;

namespace HealthMetrics.Infrastructure.Options;

public sealed class GoogleHealthApiOptions
{
    public const string SectionName = "GoogleHealthApi";

    [Required]
    public required string ClientId { get; init; }

    [Required]
    public required string ClientSecret { get; init; }

    [Required]
    public required string RedirectUri { get; init; }

    [Required]
    public required string[] Scopes { get; init; }
}
