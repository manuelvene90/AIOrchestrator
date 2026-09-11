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
    /// </summary>
    [Fact]
    public void Save_ThenLoad_ExplicitTrueRoundTrips()
    {
        var config = OrchestratorConfig_Factory.Create(
            [RepoEntry_Factory.Create("Arb Studio", @"C:\repos\arb")],
            "opus",
            "fable",
            null,
            null,
            "sonnet",
            "haiku",
            -1001234567890,
            42,
            "bot-token",
            telegramStatusScreenshots: true,
            "whisper --file",
            5_000_000);

        OrchestratorConfig_Loader.Save(config, _paths);
        var reloaded = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

        Assert.True(reloaded.TelegramStatusScreenshots);

        // The neighbours survived the trip too — a new key must not disturb the existing ones.
        Assert.Single(reloaded.Repos);
        Assert.Equal("Arb Studio", reloaded.Repos[0].Name);
        Assert.Equal("opus", reloaded.SupervisorModel);
        Assert.Equal("fable", reloaded.ImplementerModel);
        Assert.Equal("sonnet", reloaded.GeneralSupervisorModel);
        Assert.Equal("haiku", reloaded.CommunicatorModel);
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
}
