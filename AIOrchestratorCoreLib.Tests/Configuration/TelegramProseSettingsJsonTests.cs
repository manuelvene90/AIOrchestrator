using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.TelegramProseSettings;
using AIOrchestratorCoreLib.Mirroring;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Configuration;

/// <summary>
/// The <c>telegram</c> block — how a long owner-facing entry is SHAPED on the phone.
///
/// <para>
/// Both keys are hand-edited, so this follows the same three rules as the <c>defaults</c> block: an
/// untouched machine gets the shipped numbers, every way a hand-edit can be wrong costs that ONE
/// setting rather than the bridge's start, and a save from a window neither erases nor invents the
/// key.
/// </para>
/// <para>
/// AND NEITHER KEY CAN COST A MESSAGE. Both switch off at 0 into the delivery shape that predates
/// them, which is why they are safe to leave hand-edited: the worst a wrong value can do is present a
/// message differently.
/// </para>
/// </summary>
public class TelegramProseSettingsJsonTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public TelegramProseSettingsJsonTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-telegram-prose-tests-{Guid.NewGuid():N}");
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.Root);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// THE DEFAULTS ARE THE COMPONENTS' OWN, not a second copy of the numbers — the drift CLAUDE.md
    /// decision 12 forbids would be a config that means 900 while the folder means something else.
    /// </summary>
    [Fact]
    public void AnAbsentBlock_MeansTheShippedNumbers()
    {
        foreach (var settings in new[]
        {
            TelegramProseSettings_Json.Parse(null),
            TelegramProseSettings_Json.Parse(Parse("""{"repos":[]}""")),
            TelegramProseSettings_Json.Parse(Parse("""{"telegram":{}}""")),
        })
        {
            Assert.Equal(OwnerMessage_Folder.DEFAULT_FOLD_THRESHOLD, settings.FoldLongEntriesAbove);
            Assert.Equal(OwnerDocument_Builder.DEFAULT_ATTACH_ABOVE_CHUNKS, settings.AttachEntriesAbove);
        }
    }

    [Fact]
    public void TheTwoKeysAreRead()
    {
        var settings = TelegramProseSettings_Json.Parse(
            Parse("""{"telegram":{"foldLongEntriesAbove":1500,"attachEntriesAbove":5}}"""));

        Assert.Equal(1_500, settings.FoldLongEntriesAbove);
        Assert.Equal(5, settings.AttachEntriesAbove);
    }

    /// <summary>Zero is the owner saying "off", and it must survive the parser as itself.</summary>
    [Fact]
    public void ZeroIsKept_BecauseItIsTheOffSwitch()
    {
        var settings = TelegramProseSettings_Json.Parse(
            Parse("""{"telegram":{"foldLongEntriesAbove":0,"attachEntriesAbove":0}}"""));

        Assert.Equal(0, settings.FoldLongEntriesAbove);
        Assert.Equal(0, settings.AttachEntriesAbove);
    }

    /// <summary>
    /// EVERY WAY A HAND-EDIT CAN GO WRONG lands on the shipped default rather than throwing.
    /// <c>GetValue&lt;int&gt;</c> throws on a JSON string, and a config file that throws while being
    /// read is the bridge not starting — the defect the loader's numeric readers were fixed for.
    /// </summary>
    [Theory]
    [InlineData("""{"telegram":{"foldLongEntriesAbove":"900"}}""")]
    [InlineData("""{"telegram":{"foldLongEntriesAbove":{"value":900}}}""")]
    [InlineData("""{"telegram":{"foldLongEntriesAbove":[900]}}""")]
    [InlineData("""{"telegram":{"foldLongEntriesAbove":null}}""")]
    [InlineData("""{"telegram":900}""")]
    [InlineData("""{"telegram":"on"}""")]
    public void AnUnreadableValue_FallsBackToTheShippedDefault_RatherThanThrowing(string json)
    {
        Assert.Equal(
            OwnerMessage_Folder.DEFAULT_FOLD_THRESHOLD,
            TelegramProseSettings_Json.Parse(Parse(json)).FoldLongEntriesAbove);
    }

    /// <summary>
    /// THE LOADER READS IT, which is what makes the keys mean anything at all — a parser nothing calls
    /// is a setting that does not exist.
    /// </summary>
    [Fact]
    public void TheLoaderReadsTheBlock_AndAConfigWithoutOneStillHasTheDefaults()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"telegram":{"foldLongEntriesAbove":1200,"attachEntriesAbove":2}}""");

        var configured = OrchestratorConfig_Loader.Load_OrEmpty(_paths).TelegramProse;

        Assert.Equal(1_200, configured.FoldLongEntriesAbove);
        Assert.Equal(2, configured.AttachEntriesAbove);

        File.WriteAllText(_paths.ConfigFile, """{"repos":[]}""");

        var untouched = OrchestratorConfig_Loader.Load_OrEmpty(_paths).TelegramProse;

        Assert.Equal(OwnerMessage_Folder.DEFAULT_FOLD_THRESHOLD, untouched.FoldLongEntriesAbove);
        Assert.Equal(OwnerDocument_Builder.DEFAULT_ATTACH_ABOVE_CHUNKS, untouched.AttachEntriesAbove);
    }

    /// <summary>
    /// A SAVE MUST NOT TOUCH IT. The Settings window rebuilds a config from its own fields and saves;
    /// if that erased the block, the owner's hand-edit would survive exactly until the next time they
    /// opened a window and pressed OK.
    /// </summary>
    [Fact]
    public void SavingFromAWindow_LeavesTheHandEditedBlockExactlyAsItWas()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"telegram":{"foldLongEntriesAbove":1200},"supervisorModel":"opus"}""");

        OrchestratorConfig_Loader.Save(OrchestratorConfig_Loader.Load_OrEmpty(_paths), _paths);

        Assert.Equal(1_200, Parse(File.ReadAllText(_paths.ConfigFile))!["telegram"]!["foldLongEntriesAbove"]!.GetValue<int>());
        Assert.Equal(1_200, OrchestratorConfig_Loader.Load_OrEmpty(_paths).TelegramProse.FoldLongEntriesAbove);
    }

    /// <summary>
    /// AND A SAVE MUST NOT INVENT IT EITHER — writing this build's numbers into an untouched file
    /// would freeze defaults that are meant to move when the app is updated, as if the owner had
    /// chosen them.
    /// </summary>
    [Fact]
    public void SavingAConfigThatNeverHadTheBlock_DoesNotMaterialiseIt()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[]}""");

        OrchestratorConfig_Loader.Save(OrchestratorConfig_Loader.Load_OrEmpty(_paths), _paths);

        Assert.Null(Parse(File.ReadAllText(_paths.ConfigFile))!["telegram"]);
    }

    static JsonObject? Parse(string json) => JsonNode.Parse(json) as JsonObject;
}
