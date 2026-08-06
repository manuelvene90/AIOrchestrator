using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Configuration;

public class ConfigReposReordererTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public ConfigReposReordererTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-reorder-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void Persist_Order_ReordersRepos_AndPreservesEveryOtherKey()
    {
        File.WriteAllText(_paths.ConfigFile, """
            {
              "repos": [
                { "name": "Alpha", "path": "C:\\a", "customAgentKey": 7 },
                { "name": "Beta", "path": "C:\\b" },
                { "name": "Gamma", "path": "C:\\c" }
              ],
              "supervisorModel": "opus",
              "unknownFutureKey": { "kept": true }
            }
            """);

        ConfigRepos_Reorderer.Persist_Order(_paths, ["Gamma", "Alpha", "Beta"]);

        var root = JsonNode.Parse(File.ReadAllText(_paths.ConfigFile)) as JsonObject;
        Assert.NotNull(root);

        var reposArray = root["repos"] as JsonArray;
        Assert.NotNull(reposArray);
        Assert.Equal("Gamma", reposArray[0]?["name"]?.GetValue<string>());
        Assert.Equal("Alpha", reposArray[1]?["name"]?.GetValue<string>());
        Assert.Equal("Beta", reposArray[2]?["name"]?.GetValue<string>());

        // Unknown keys — top-level AND per-repo (agents extend config.json) — must survive.
        Assert.Equal(7, reposArray[1]?["customAgentKey"]?.GetValue<int>());
        Assert.Equal("opus", root["supervisorModel"]?.GetValue<string>());
        Assert.True(root["unknownFutureKey"]?["kept"]?.GetValue<bool>());
    }

    [Fact]
    public void Persist_Order_NamesNotInTheOrder_KeepRelativePositionAtTheEnd()
    {
        File.WriteAllText(_paths.ConfigFile, """
            {
              "repos": [
                { "name": "Alpha", "path": "C:\\a" },
                { "name": "Beta", "path": "C:\\b" },
                { "name": "Gamma", "path": "C:\\c" }
              ]
            }
            """);

        ConfigRepos_Reorderer.Persist_Order(_paths, ["Gamma"]);

        var root = JsonNode.Parse(File.ReadAllText(_paths.ConfigFile)) as JsonObject;
        var reposArray = root?["repos"] as JsonArray;
        Assert.NotNull(reposArray);
        Assert.Equal("Gamma", reposArray[0]?["name"]?.GetValue<string>());
        Assert.Equal("Alpha", reposArray[1]?["name"]?.GetValue<string>());
        Assert.Equal("Beta", reposArray[2]?["name"]?.GetValue<string>());
    }

    [Fact]
    public void Persist_Order_MissingConfigFile_DoesNothing()
    {
        ConfigRepos_Reorderer.Persist_Order(_paths, ["Anything"]);

        Assert.False(File.Exists(_paths.ConfigFile));
    }
}
