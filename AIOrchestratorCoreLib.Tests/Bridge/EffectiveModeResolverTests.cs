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
            paused: false,
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
            paused: false,
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
            paused: false,
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
            paused: false,
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
            paused: false,
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
            paused: false,
            topicMode: TelegramDeliveryModes.Silenced,
            appWideDeferred: true,
            appWideSilenced: false);

        Assert.Equal(TelegramDeliveryModes.Deferred, mode);
    }

    /// <summary>
    /// THE COMPOSITION. A Deferred topic has a backlog frozen behind its cursor. The owner sits down
    /// at that terminal, presence outranks delivery, the topic resolves to Silenced — and Silenced
    /// topics ARE polled, so the held backlog would be read, dropped, and its offset advanced past
    /// every entry the owner had explicitly asked to keep.
    /// </summary>
    [Fact]
    public void ADeferredTopic_KeepsFreezing_WhenPresenceSilencesIt()
    {
        Assert.True(EffectiveMode_Resolver.Freezes_Offsets(TelegramDeliveryModes.Silenced, topicMode: TelegramDeliveryModes.Deferred));
    }

    /// <summary>
    /// A topic that was NOT deferred is still dropped-while-silenced, deliberately: the owner is
    /// reading it live in the terminal, and this is the behaviour Silenced is documented to have.
    /// Without this the fix above would quietly turn every meeting into a replay queue.
    /// </summary>
    [Fact]
    public void ANormalTopic_SilencedByPresence_IsStillPolledAndDropped()
    {
        Assert.False(EffectiveMode_Resolver.Freezes_Offsets(TelegramDeliveryModes.Silenced, topicMode: TelegramDeliveryModes.Normal));
    }

    [Fact]
    public void AnEffectivelyDeferredTopic_Freezes()
    {
        Assert.True(EffectiveMode_Resolver.Freezes_Offsets(TelegramDeliveryModes.Deferred, topicMode: TelegramDeliveryModes.Normal));
    }

    [Fact]
    public void ANormalTopic_IsPolled()
    {
        Assert.False(EffectiveMode_Resolver.Freezes_Offsets(TelegramDeliveryModes.Normal, topicMode: TelegramDeliveryModes.Normal));
    }

    [Fact]
    public void NobodyHasAnOpinion_IsNormal()
    {
        var mode = EffectiveMode_Resolver.Resolve(
            OwnerPresenceModes.Remote,
            isGeneral: false,
            paused: false,
            topicMode: TelegramDeliveryModes.Normal,
            appWideDeferred: false,
            appWideSilenced: false);

        Assert.Equal(TelegramDeliveryModes.Normal, mode);
    }

    /// <summary>
    /// PAUSE OUTRANKS EVERYTHING, and it must resolve to DEFERRED rather than Silenced: Silenced
    /// drops, and the licence to drop rests on the owner reading the same content live in a
    /// terminal. A paused orchestration is one they walked away from, so nothing written while it
    /// slept may be thrown away — it is held, and it replays when they lift the pause.
    /// </summary>
    [Fact]
    public void APausedTopic_IsDeferred_SoNothingWrittenWhileItSleptIsLost()
    {
        var mode = EffectiveMode_Resolver.Resolve(
            OwnerPresenceModes.Remote,
            isGeneral: false,
            paused: true,
            topicMode: TelegramDeliveryModes.Normal,
            appWideDeferred: false,
            appWideSilenced: false);

        Assert.Equal(TelegramDeliveryModes.Deferred, mode);
    }

    /// <summary>
    /// The composition that would otherwise eat the backlog: presence outranks a topic's own mode,
    /// and Silenced topics ARE polled. If presence still won here, sitting down at the terminal of a
    /// paused orchestration would read its held entries and drop them — the exact defect
    /// Freezes_Offsets was written for, arriving by a new route.
    /// </summary>
    [Fact]
    public void APausedTopic_StaysDeferred_EvenWhenTheOwnerIsAtItsTerminal()
    {
        var mode = EffectiveMode_Resolver.Resolve(
            OwnerPresenceModes.Terminal,
            isGeneral: false,
            paused: true,
            topicMode: TelegramDeliveryModes.Normal,
            appWideDeferred: false,
            appWideSilenced: false);

        Assert.Equal(TelegramDeliveryModes.Deferred, mode);
        Assert.True(EffectiveMode_Resolver.Freezes_Offsets(mode, TelegramDeliveryModes.Normal));
    }

    [Fact]
    public void APausedTopic_StaysDeferred_EvenWhenItsOwnModeSaysSilenced()
    {
        var mode = EffectiveMode_Resolver.Resolve(
            OwnerPresenceModes.Remote,
            isGeneral: false,
            paused: true,
            topicMode: TelegramDeliveryModes.Silenced,
            appWideDeferred: false,
            appWideSilenced: true);

        Assert.Equal(TelegramDeliveryModes.Deferred, mode);
    }

    /// <summary>An unpaused topic must be exactly what it was before pause existed.</summary>
    [Fact]
    public void NotPaused_ChangesNothing()
    {
        var mode = EffectiveMode_Resolver.Resolve(
            OwnerPresenceModes.Remote,
            isGeneral: false,
            paused: false,
            topicMode: TelegramDeliveryModes.Silenced,
            appWideDeferred: false,
            appWideSilenced: false);

        Assert.Equal(TelegramDeliveryModes.Silenced, mode);
    }
}
