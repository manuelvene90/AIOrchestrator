using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.DefaultsSettings;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Configuration;

/// <summary>
/// The <c>defaults</c> block — one setting so far, and it is the one the first live round asked for:
/// the shape an orchestration started without an explicit <c>mode</c> comes up in.
///
/// <para>
/// TOLERANT ON THE WAY IN, because a config the app refuses to load is a bridge that does not start,
/// and every way this key can be wrong is a hand-edit typo. READ BUT NEVER WRITTEN, because no window
/// has a field for it: a save that serialised it would freeze this build's default into the owner's
/// file as if they had chosen it, which is the argument the loader already makes about the guardrail
/// keys.
/// </para>
/// </summary>
public class DefaultsSettingsJsonTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public DefaultsSettingsJsonTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-defaults-tests-{Guid.NewGuid():N}");
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.Root);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// AN UNTOUCHED MACHINE DOES NOT CHANGE. The owner set basic on 2026-08-13 to stop a crew being
    /// spawned for work that needs one session; making the key editable is not licence to move it.
    /// </summary>
    [Fact]
    public void AnAbsentBlock_MeansTheOwnersShippedDefault_Basic()
    {
        Assert.True(DefaultsSettings_Json.Parse(null).OrchestrationIsBasic);
        Assert.True(DefaultsSettings_Json.Parse(Parse("""{"repos":[]}""")).OrchestrationIsBasic);
        Assert.True(DefaultsSettings_Json.Parse(Parse("""{"defaults":{}}""")).OrchestrationIsBasic);
    }

    [Theory]
    [InlineData("full", false)]
    [InlineData("basic", true)]
    [InlineData("FULL", false)]
    [InlineData("  Basic  ", true)]
    public void TheWordDecidesTheShape_CaseAndSpaceInsensitively(string word, bool expectedBasic)
    {
        var defaults = DefaultsSettings_Json.Parse(Parse($$$"""{"defaults":{"orchestrationMode":"{{{word}}}"}}"""));

        Assert.Equal(expectedBasic, defaults.OrchestrationIsBasic);
    }

    /// <summary>
    /// EVERY WAY A HAND-EDIT CAN GO WRONG lands on the shipped default rather than throwing — a word
    /// nobody recognises, and the three JSON types that make <c>GetValue&lt;string&gt;</c> throw. The
    /// last three are the ones that would take the bridge down on start, which is a far worse outcome
    /// than a shape the owner can see is wrong the first time they start something.
    /// </summary>
    [Theory]
    [InlineData("""{"defaults":{"orchestrationMode":"crew"}}""")]
    [InlineData("""{"defaults":{"orchestrationMode":"sol"}}""")]
    [InlineData("""{"defaults":{"orchestrationMode":7}}""")]
    [InlineData("""{"defaults":{"orchestrationMode":{"mode":"full"}}}""")]
    [InlineData("""{"defaults":{"orchestrationMode":["full"]}}""")]
    [InlineData("""{"defaults":"full"}""")]
    public void AnUnreadableValue_FallsBackToTheShippedDefault_RatherThanThrowing(string json)
    {
        Assert.True(DefaultsSettings_Json.Parse(Parse(json)).OrchestrationIsBasic);
    }

    /// <summary>
    /// THE LOADER READS IT, which is what makes the key mean anything at all — a parser nothing calls
    /// is a setting that does not exist.
    /// </summary>
    [Fact]
    public void TheLoaderReadsTheKey_AndAConfigWithoutOneStillHasDefaults()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"defaults":{"orchestrationMode":"full"}}""");
        Assert.False(OrchestratorConfig_Loader.Load_OrEmpty(_paths).Defaults.OrchestrationIsBasic);

        File.WriteAllText(_paths.ConfigFile, """{"repos":[]}""");
        Assert.True(OrchestratorConfig_Loader.Load_OrEmpty(_paths).Defaults.OrchestrationIsBasic);
    }

    /// <summary>
    /// A SAVE MUST NOT TOUCH IT. The Settings window rebuilds a config from its own fields and saves;
    /// if that erased or rewrote this key, the owner's hand-edit would survive exactly until the next
    /// time they opened a window and pressed OK.
    /// </summary>
    [Fact]
    public void SavingFromAWindow_LeavesTheHandEditedKeyExactlyAsItWas()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"defaults":{"orchestrationMode":"full"},"supervisorModel":"opus"}""");

        var loaded = OrchestratorConfig_Loader.Load_OrEmpty(_paths);
        OrchestratorConfig_Loader.Save(loaded, _paths);

        var root = Parse(File.ReadAllText(_paths.ConfigFile))!;

        Assert.Equal("full", root["defaults"]!["orchestrationMode"]!.GetValue<string>());
        Assert.False(OrchestratorConfig_Loader.Load_OrEmpty(_paths).Defaults.OrchestrationIsBasic);
    }

    /// <summary>
    /// AND A SAVE MUST NOT INVENT IT EITHER — the guardrail keys' own argument, applied here: writing
    /// this build's default into an untouched file would freeze a default that is meant to move when
    /// the app is updated, as if the owner had chosen it.
    /// </summary>
    [Fact]
    public void SavingAConfigThatNeverHadTheKey_DoesNotMaterialiseIt()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[]}""");

        OrchestratorConfig_Loader.Save(OrchestratorConfig_Loader.Load_OrEmpty(_paths), _paths);

        Assert.Null(Parse(File.ReadAllText(_paths.ConfigFile))!["defaults"]);
    }

    static JsonObject? Parse(string json) => JsonNode.Parse(json) as JsonObject;
}
