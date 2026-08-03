namespace HealthMetrics.Infrastructure.Options;

internal static class GoogleHealthScopes
{
    public const string SleepRead = "https://www.googleapis.com/auth/googlehealth.sleep.readonly";

    public static bool Contains(string? grantedScopes, string requiredScope) =>
        Parse(grantedScopes).Contains(requiredScope);

    private static HashSet<string> Parse(string? scopes) =>
        string.IsNullOrWhiteSpace(scopes)
            ? []
            : scopes.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.Ordinal);
}
