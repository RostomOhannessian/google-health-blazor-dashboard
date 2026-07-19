using System.ComponentModel.DataAnnotations;

namespace FitbitMetrics.Infrastructure.Options;

public sealed class FitbitApiOptions
{
    public const string SectionName = "FitbitApi";

    [Required]
    public required string ClientId { get; init; }

    [Required]
    public required string ClientSecret { get; init; }

    [Required]
    public required string RedirectUri { get; init; }

    [Required]
    public required string[] Scopes { get; init; }
}
