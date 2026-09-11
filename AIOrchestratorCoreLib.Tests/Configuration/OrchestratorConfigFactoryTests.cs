using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.Configuration.RepoEntry;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Configuration;

public class OrchestratorConfigFactoryTests
{
    /// <summary>
    /// /screenshots flips ONE bool of a config it did not build, so this pins every neighbouring
    /// field by hand. The bug this guards against is not "the flag did not move" — it is the copy
    /// silently blanking the bot token on the way through. (A twin test covered /italian until
    /// 2026-09-09, when the translation layer was abolished.)
    /// </summary>
    [Fact]
    public void Create_WithStatusScreenshots_ChangesOnlyTheScreenshotFlag()
    {
        var original = OrchestratorConfig_Factory.Create(
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
        Assert.Equal("whisper --file", flipped.VoiceTranscribeCommand);
        Assert.Equal(5_000_000, flipped.OrchestrationTokenBudget);

        // And back off again — the toggle is used in both directions.
        Assert.False(OrchestratorConfig_Factory.Create_WithStatusScreenshots(flipped, false).TelegramStatusScreenshots);
    }
}
