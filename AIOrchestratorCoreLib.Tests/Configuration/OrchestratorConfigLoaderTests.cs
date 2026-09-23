using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.Configuration.RepoEntry;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Configuration;

public class OrchestratorConfigLoaderTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public OrchestratorConfigLoaderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-loader-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// Screenshots are OPT-IN: taking one raises and maximises a real window on the owner's desk,
    /// so every config.json written before the flag existed — and every one where the owner never
    /// asked — must read as OFF. An absent key defaulting the other way would start raising windows
    /// on machines whose owner never enabled anything.
    /// </summary>
    [Fact]
    public void Load_StatusScreenshotsKeyAbsent_ReadsAsFalse()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[]}""");

        Assert.False(OrchestratorConfig_Loader.Load_OrEmpty(_paths).TelegramStatusScreenshots);
    }

    /// <summary>And with no config file at all — the empty config is the same OFF answer.</summary>
    [Fact]
    public void Load_NoConfigFileAtAll_StatusScreenshotsIsFalse()
    {
        Assert.False(OrchestratorConfig_Loader.Load_OrEmpty(_paths).TelegramStatusScreenshots);
    }

    /// <summary>
    /// The /screenshots command persists through Save, and the next launch has to see it — a flag
    /// that is written but not parsed back reads as "the toggle never worked".
    ///
    /// <para>
    /// THE FOUR MODELS NO LONGER MAKE THE TRIP, AND THIS TEST NOW PINS THAT (plan 04 D1, 2026-09-23). It
    /// used to assert they round-tripped — and its first model assertion was green only because "opus" is
    /// also the shipped default, so it could not have told a written model from an unwritten one. Save now
    /// writes no model key: the config below carries four models that are NONE of the shipped defaults
    /// (asserted, so a default that later moved onto one of them could not turn this green by coincidence),
    /// and the reload reads the defaults, because a model is written by <c>Settings_Writer</c> when its
    /// row is edited and by nothing else. The neighbours Save still owns keep the round trip they had.
    /// This is the /screenshots path exactly: <c>BridgeEngineModel.Set_StatusScreenshots</c> saves a
    /// config rebuilt from the resolved one, which until D1 wrote those resolved models back.
    /// </para>
    /// </summary>
    [Fact]
    public void Save_ThenLoad_ExplicitTrueRoundTrips()
    {
        var config = OrchestratorConfig_Factory.Create(
            [RepoEntry_Factory.Create("Arb Studio", @"C:\repos\arb")],
            "haiku",
            "fable",
            null,
            null,
            "haiku",
            "fable",
            -1001234567890,
            42,
            "bot-token",
            telegramStatusScreenshots: true,
            "whisper --file",
            5_000_000);

        Assert.NotEqual(OrchestratorConfig_Factory.DEFAULT_SUPERVISOR_MODEL, config.SupervisorModel);
        Assert.NotEqual(OrchestratorConfig_Factory.DEFAULT_IMPLEMENTER_MODEL, config.ImplementerModel);
        Assert.NotEqual(OrchestratorConfig_Factory.DEFAULT_GENERAL_SUPERVISOR_MODEL, config.GeneralSupervisorModel);
        Assert.NotEqual(OrchestratorConfig_Factory.DEFAULT_COMMUNICATOR_MODEL, config.CommunicatorModel);

        OrchestratorConfig_Loader.Save(config, _paths);
        var reloaded = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

        Assert.True(reloaded.TelegramStatusScreenshots);

        // D1: the four models the config carried were not written, so the reload is a machine that
        // states none — the shipped defaults.
        Assert.Equal(OrchestratorConfig_Factory.DEFAULT_SUPERVISOR_MODEL, reloaded.SupervisorModel);
        Assert.Equal(OrchestratorConfig_Factory.DEFAULT_IMPLEMENTER_MODEL, reloaded.ImplementerModel);
        Assert.Equal(OrchestratorConfig_Factory.DEFAULT_GENERAL_SUPERVISOR_MODEL, reloaded.GeneralSupervisorModel);
        Assert.Equal(OrchestratorConfig_Factory.DEFAULT_COMMUNICATOR_MODEL, reloaded.CommunicatorModel);

        // The neighbours survived the trip too — a new key must not disturb the existing ones.
        Assert.Single(reloaded.Repos);
        Assert.Equal("Arb Studio", reloaded.Repos[0].Name);
        Assert.Equal(-1001234567890, reloaded.TelegramSupergroupChatId);
        Assert.Equal(42, reloaded.TelegramOwnerUserId);
        Assert.Equal("bot-token", reloaded.TelegramBotToken);
        Assert.Equal("whisper --file", reloaded.VoiceTranscribeCommand);
        Assert.Equal(5_000_000, reloaded.OrchestrationTokenBudget);
    }

    /// <summary>An explicit false stays false — the round trip is not just "true sticks".</summary>
    [Fact]
    public void Save_ThenLoad_ExplicitFalseRoundTrips()
    {
        var config = OrchestratorConfig_Factory.Create_WithStatusScreenshots(
            OrchestratorConfig_Factory.Create_Empty(), false);

        OrchestratorConfig_Loader.Save(config, _paths);

        Assert.False(OrchestratorConfig_Loader.Load_OrEmpty(_paths).TelegramStatusScreenshots);
    }

    /// <summary>
    /// THE BOT TOKEN HAS ITS OWN DOOR, AND IT OPENS ON secrets.json ALONE (plan 04 ruling P11). The full
    /// <see cref="OrchestratorConfig_Loader.Save"/> also rewrites <c>runners.*</c>, <c>limits.*</c> and five Kernel scalars
    /// at whatever they RESOLVED to, which turns their origin into "set here" and undoes a Reset made a
    /// minute earlier from the phone. The token is the one thing the settings renderers may not write
    /// (the catalogue leaves it out on purpose), so the window's Connection tab needs a save that touches
    /// nothing else: config.json byte for byte, and every other key of secrets.json carried through.
    /// </summary>
    [Fact]
    public void SaveBotToken_WritesSecretsJsonOnly_AndCarriesItsOtherKeys()
    {
        const string configText = """{"repos":[],"runners":{"supervisor":{"runner":"print"}},"telegramInbound":"off"}""";
        File.WriteAllText(_paths.ConfigFile, configText);
        File.WriteAllText(_paths.SecretsFile, """{"telegramBotToken":"old-token","somethingElse":7}""");

        OrchestratorConfig_Loader.Save_BotToken(_paths, "new-token");

        Assert.Equal(configText, File.ReadAllText(_paths.ConfigFile));

        var secrets = JsonNode.Parse(File.ReadAllText(_paths.SecretsFile))!.AsObject();
        Assert.Equal("new-token", secrets["telegramBotToken"]!.GetValue<string>());
        Assert.Equal(7, secrets["somethingElse"]!.GetValue<int>());

        Assert.Equal("new-token", OrchestratorConfig_Loader.Load_OrEmpty(_paths).TelegramBotToken);
    }
}
