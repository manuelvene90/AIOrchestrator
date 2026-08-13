using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

public class EffectiveModeResolverTests
{
    /// <summary>
    /// THE DEFECT THIS UNIT EXISTS FOR. The presence check sat inside the "not General" branch, so
    /// General — the topic the owner sits at most — had terminal mode's dangerous half (the block
    /// lifted, which that path reads from presence directly) without the half they asked for.
    /// </summary>
    [Fact]
    public void General_InTerminalPresence_IsSilencedLikeEveryOtherTopic()
    {
        var mode = EffectiveMode_Resolver.Resolve(
            OwnerPresenceModes.Terminal,
            isGeneral: true,
            topicMode: TelegramDeliveryModes.Normal,
            appWideDeferred: false,
            appWideSilenced: false);

        Assert.Equal(TelegramDeliveryModes.Silenced, mode);
    }

    [Fact]
    public void AnOrchestrationTopic_InTerminalPresence_IsSilenced()
    {
        var mode = EffectiveMode_Resolver.Resolve(
            OwnerPresenceModes.Terminal,
            isGeneral: false,
            topicMode: TelegramDeliveryModes.Normal,
            appWideDeferred: false,
            appWideSilenced: false);

        Assert.Equal(TelegramDeliveryModes.Silenced, mode);
    }

    /// <summary>
    /// Presence outranks a DELIBERATE app-wide deferral, and this is the one direction worth pinning:
    /// the owner at the terminal is reading it live, so replaying it to their phone later — which is
    /// what Deferred means — is precisely what they do not want.
    /// </summary>
    [Fact]
    public void TerminalPresence_OutranksTheAppWideDeferral()
    {
        var mode = EffectiveMode_Resolver.Resolve(
            OwnerPresenceModes.Terminal,
            isGeneral: false,
            topicMode: TelegramDeliveryModes.Normal,
            appWideDeferred: true,
            appWideSilenced: false);

        Assert.Equal(TelegramDeliveryModes.Silenced, mode);
    }

    /// <summary>
    /// Remote has NO opinion — a deliberate mute must survive the owner walking away from a terminal.
    /// The same rule <c>Remote_HasNoOpinionAboutDelivery</c> pins one level down.
    /// </summary>
    [Fact]
    public void Remote_LeavesADeliberateTopicMuteStanding()
    {
        var mode = EffectiveMode_Resolver.Resolve(
            OwnerPresenceModes.Remote,
            isGeneral: false,
            topicMode: TelegramDeliveryModes.Deferred,
            appWideDeferred: false,
            appWideSilenced: false);

        Assert.Equal(TelegramDeliveryModes.Deferred, mode);
    }

    [Fact]
    public void ATopicsOwnMode_WinsOverTheAppWideSetting()
    {
        var mode = EffectiveMode_Resolver.Resolve(
            OwnerPresenceModes.Remote,
            isGeneral: false,
            topicMode: TelegramDeliveryModes.Silenced,
            appWideDeferred: true,
            appWideSilenced: false);

        Assert.Equal(TelegramDeliveryModes.Silenced, mode);
    }

    /// <summary>
    /// General has no session and so no topic mode of its own — the app-wide setting is all that is
    /// left to apply, which is the only thing the General branch was ever for.
    /// </summary>
    [Fact]
    public void General_WithNoPresenceOpinion_FallsThroughToTheAppWideSetting()
    {
        var mode = EffectiveMode_Resolver.Resolve(
            OwnerPresenceModes.Remote,
            isGeneral: true,
            topicMode: TelegramDeliveryModes.Silenced,
            appWideDeferred: true,
            appWideSilenced: false);

        Assert.Equal(TelegramDeliveryModes.Deferred, mode);
    }

    [Fact]
    public void NobodyHasAnOpinion_IsNormal()
    {
        var mode = EffectiveMode_Resolver.Resolve(
            OwnerPresenceModes.Remote,
            isGeneral: false,
            topicMode: TelegramDeliveryModes.Normal,
            appWideDeferred: false,
            appWideSilenced: false);

        Assert.Equal(TelegramDeliveryModes.Normal, mode);
    }
}
