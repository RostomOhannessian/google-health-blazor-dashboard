using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;

namespace HealthMetrics.Tests.Web;

public sealed class ConfigurationSetupTests
{
    [Fact]
    public void LocalConfiguration_IsInsertedBeforeHigherPrecedenceProviders()
    {
        var configuration = new ConfigurationManager();
        configuration.AddJsonFile("appsettings.json");
        configuration.AddJsonFile("appsettings.Development.json");
        configuration.AddJsonFile("additional.json", optional: true);
        configuration.AddInMemoryCollection(
        [
            new KeyValuePair<string, string?>("Example", "user-secret")
        ]);
        configuration.AddEnvironmentVariables();
        configuration.AddCommandLine([]);

        ConfigurationSetup.AddLocalConfiguration(configuration);

        var sources = configuration.Sources.ToList();
        var localIndex = sources.FindIndex(source =>
            source is JsonConfigurationSource { Path: "appsettings.Local.json" });
        Assert.True(localIndex >= 0);
        var localSource = Assert.IsType<JsonConfigurationSource>(sources[localIndex]);

        Assert.Equal("appsettings.Local.json", localSource.Path);
        Assert.Equal(2, sources.Take(localIndex).OfType<JsonConfigurationSource>().Count());
        Assert.Contains(sources.Skip(localIndex + 1), source =>
            source is JsonConfigurationSource { Path: "additional.json" });
        Assert.Contains(sources.Skip(localIndex + 1), source => source is MemoryConfigurationSource);
        Assert.Contains(sources.Skip(localIndex + 1), source => source is EnvironmentVariablesConfigurationSource);
        Assert.Contains(sources.Skip(localIndex + 1), source => source is CommandLineConfigurationSource);
    }
}
