using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.Configuration.RepoEntry;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Configuration;

public class OrchestratorConfigFactoryTests
{
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
        Assert.Equal("whisper --file", flipped.VoiceTranscribeCommand);
        Assert.Equal(5_000_000, flipped.OrchestrationTokenBudget);

        // And back again — the toggle is used in both directions.
        Assert.True(OrchestratorConfig_Factory.Create_WithItalianLayer(flipped, true).TelegramItalianLayer);
    }
}
