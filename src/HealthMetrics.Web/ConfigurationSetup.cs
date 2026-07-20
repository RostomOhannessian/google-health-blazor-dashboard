using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

internal static class ConfigurationSetup
{
    internal static void AddLocalConfiguration(IConfigurationManager configuration)
    {
        var insertIndex = configuration.Sources
            .Select((source, index) => (source, index))
            .Where(item =>
                item.source is JsonConfigurationSource json &&
                json.Path?.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) == true)
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .Max() + 1;

        configuration.Sources.Insert(
            insertIndex,
            new JsonConfigurationSource
            {
                Path = "appsettings.Local.json",
                Optional = true,
                ReloadOnChange = true
            });
    }
}
