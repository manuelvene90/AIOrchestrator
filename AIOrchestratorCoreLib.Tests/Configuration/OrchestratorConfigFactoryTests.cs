using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.Configuration.RepoEntry;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Configuration;

public class OrchestratorConfigFactoryTests
{
    /// <summary>
    /// The ladder the owner set on 2026-09-09: the roles that supervise and implement run on
    /// Fable 5.1, routing and narration stay cheap. An absent key in config.json must land here.
    /// </summary>
    [Fact]
    public void Create_Empty_UsesTheOwnersModelLadder()
    {
        var config = OrchestratorConfig_Factory.Create_Empty();

        Assert.Equal("claude-fable-5-1", config.SupervisorModel);
        Assert.Equal("claude-fable-5-1", config.ImplementerModel);
        Assert.Equal("sonnet", config.GeneralSupervisorModel);
        Assert.Equal("sonnet", config.CommunicatorModel);
    }

    /// <summary>
    /// The /italian command and the app's status-bar checkbox both flip ONE field of a config they
    /// did not build. If this copy dropped a neighbouring value, toggling the language from the
    /// phone would quietly erase the bot token or the model ladder on the way through.
    /// </summary>
    [Fact]
    public void Create_WithItalianLayer_ChangesOnlyTheLayer()
    {
        var original = OrchestratorConfig_Factory.Create(
            [RepoEntry_Factory.Create("Arb Studio", @"C:\repos\arb")],
            "opus",
            "fable",
            "sonnet",
            "haiku",
            -1001234567890,
            42,
            "bot-token",
            telegramItalianLayer: true,
            telegramStatusScreenshots: true,
            "whisper --file",
            5_000_000);

        var flipped = OrchestratorConfig_Factory.Create_WithItalianLayer(original, false);

        Assert.False(flipped.TelegramItalianLayer);

        Assert.Equal(original.Repos, flipped.Repos);
        Assert.Equal("opus", flipped.SupervisorModel);
        Assert.Equal("fable", flipped.ImplementerModel);
        Assert.Equal("sonnet", flipped.GeneralSupervisorModel);
        Assert.Equal("haiku", flipped.CommunicatorModel);
        Assert.Equal(-1001234567890, flipped.TelegramSupergroupChatId);
        Assert.Equal(42, flipped.TelegramOwnerUserId);
        Assert.Equal("bot-token", flipped.TelegramBotToken);
        Assert.True(flipped.TelegramStatusScreenshots);
        Assert.Equal("whisper --file", flipped.VoiceTranscribeCommand);
        Assert.Equal(5_000_000, flipped.OrchestrationTokenBudget);

        // And back again — the toggle is used in both directions.
        Assert.True(OrchestratorConfig_Factory.Create_WithItalianLayer(flipped, true).TelegramItalianLayer);
    }

    /// <summary>
    /// Same hazard, second toggle: /screenshots flips ONE bool of a config it did not build, so
    /// this pins every neighbouring field by hand. The bug this guards against is not "the flag
    /// did not move" — it is the copy silently blanking the bot token on the way through.
    /// </summary>
    [Fact]
    public void Create_WithStatusScreenshots_ChangesOnlyTheScreenshotFlag()
    {
        var original = OrchestratorConfig_Factory.Create(
            [RepoEntry_Factory.Create("Arb Studio", @"C:\repos\arb")],
            "opus",
            "fable",
            "sonnet",
            "haiku",
            -1001234567890,
            42,
            "bot-token",
            telegramItalianLayer: true,
            telegramStatusScreenshots: false,
            "whisper --file",
            5_000_000);

        var flipped = OrchestratorConfig_Factory.Create_WithStatusScreenshots(original, true);

        Assert.True(flipped.TelegramStatusScreenshots);

        Assert.Equal(original.Repos, flipped.Repos);
        Assert.Equal("opus", flipped.SupervisorModel);
        Assert.Equal("fable", flipped.ImplementerModel);
        Assert.Equal("sonnet", flipped.GeneralSupervisorModel);
        Assert.Equal("haiku", flipped.CommunicatorModel);
        Assert.Equal(-1001234567890, flipped.TelegramSupergroupChatId);
        Assert.Equal(42, flipped.TelegramOwnerUserId);
        Assert.Equal("bot-token", flipped.TelegramBotToken);
        Assert.True(flipped.TelegramItalianLayer);
        Assert.Equal("whisper --file", flipped.VoiceTranscribeCommand);
        Assert.Equal(5_000_000, flipped.OrchestrationTokenBudget);

        // And back off again — the toggle is used in both directions.
        Assert.False(OrchestratorConfig_Factory.Create_WithStatusScreenshots(flipped, false).TelegramStatusScreenshots);
    }
}
