using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Kit;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// THE COMPILED EXPECTATION AND THE SHIPPED MANIFEST ARE ONE FACT IN TWO FILES, and this is what
/// keeps them one. Without it, KitPlugin.EXPECTED_VERSION is a number a host asserts about a kit
/// nobody made match it: a protocol change that forgets to bump plugin.json would install happily
/// and the verifier would certify it, which is precisely the "certifying the absence of the thing
/// you are testing" failure decision 20 was written after.
///
/// IT REFUSES TO RUN IF IT CANNOT FIND THE MANIFEST. A pass from a test that located no file is a
/// pass about nothing.
/// </summary>
public class KitPluginIdentityTests
{
    [Fact]
    public void PluginManifest_Version_IsTheVersionTheHostExpects()
    {
        var manifest = Read_Manifest("plugin.json");

        Assert.Equal(KitPlugin.EXPECTED_VERSION, manifest["version"]?.GetValue<string>());
    }

    [Fact]
    public void PluginManifest_Name_IsTheNameTheHostLooksFor()
    {
        var manifest = Read_Manifest("plugin.json");

        Assert.Equal(KitPlugin.NAME, manifest["name"]?.GetValue<string>());
    }

    [Fact]
    public void MarketplaceManifest_Name_IsTheMarketplaceHalfOfTheInstalledId()
    {
        var manifest = Read_Manifest("marketplace.json");

        Assert.Equal(KitPlugin.MARKETPLACE, manifest["name"]?.GetValue<string>());
        Assert.Equal($"{KitPlugin.NAME}@{KitPlugin.MARKETPLACE}", KitPlugin.ID);
    }

    [Fact]
    public void MarketplaceManifest_ServesExactlyThePluginTheHostExpects()
    {
        var plugins = Read_Manifest("marketplace.json")["plugins"] as JsonArray;

        var served = Assert.Single(plugins!);
        Assert.Equal(KitPlugin.NAME, served!["name"]?.GetValue<string>());
    }

    static JsonObject Read_Manifest(string fileName)
    {
        var path = KitRepoFiles.Find(Path.Combine("kit", ".claude-plugin", fileName))
            ?? throw new Exception($"kit/.claude-plugin/{fileName} was not found from {AppContext.BaseDirectory} — REFUSING to pass about a manifest this test never read.");

        return JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new Exception($"{path} is not a JSON object.");
    }
}
