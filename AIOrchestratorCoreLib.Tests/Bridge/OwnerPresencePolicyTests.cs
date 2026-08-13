using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

public class OwnerPresencePolicyTests
{
    [Fact]
    public void Terminal_SilencesTheTopic_RatherThanDeferringIt()
    {
        // SILENCED, not DEFERRED, and the difference is the feature: the owner is reading this
        // content live in the terminal, so replaying it to their phone afterwards would hand them a
        // burst of things they have already read.
        Assert.Equal(TelegramDeliveryModes.Silenced, OwnerPresence_Policy.Resolve_ModeOverride_OrNull(OwnerPresenceModes.Terminal));
    }

    [Fact]
    public void Remote_HasNoOpinionAboutDelivery()
    {
        // Null rather than Normal: presence must not overrule a mute the owner set deliberately.
        // "Remote and muted" is a real combination — on the phone, do not ping me.
        Assert.Null(OwnerPresence_Policy.Resolve_ModeOverride_OrNull(OwnerPresenceModes.Remote));
    }

    [Fact]
    public void Terminal_NeverRaisesTheAwaitingAnswerFlag()
    {
        // The load-bearing half. Without this the mode is cosmetic: the supervisor still freezes on
        // every Bash call waiting for a tap while the owner types the answer into the session.
        Assert.False(OwnerPresence_Policy.Should_RaiseAwaitingAnswer(OwnerPresenceModes.Terminal));
        Assert.True(OwnerPresence_Policy.Should_RaiseAwaitingAnswer(OwnerPresenceModes.Remote));
    }

    [Fact]
    public void OwnerTextingTheTopic_EndsTerminalMode()
    {
        Assert.True(OwnerPresence_Policy.Should_FlipToRemote(OwnerPresenceModes.Terminal, isPresenceCommandItself: false));
    }

    [Fact]
    public void ThePresenceCommandItself_DoesNotEndTheModeItJustAskedFor()
    {
        // /pc arrives from Telegram like any other message. Without this exclusion the command
        // would be undone by its own delivery — the trap the mode commands already dodge by
        // deferring themselves out of the inbound loop.
        Assert.False(OwnerPresence_Policy.Should_FlipToRemote(OwnerPresenceModes.Terminal, isPresenceCommandItself: true));
    }

    [Fact]
    public void AlreadyRemote_HasNothingToFlip()
    {
        Assert.False(OwnerPresence_Policy.Should_FlipToRemote(OwnerPresenceModes.Remote, isPresenceCommandItself: false));
    }

    [Fact]
    public void Terminal_StopsTheAppPullingTheSupervisorBackToWork()
    {
        // The meeting half: nudges, ledger complaints, idle flags and the periodic status are all
        // interruptions of the conversation the owner is having with it.
        Assert.True(OwnerPresence_Policy.Suppresses_SupervisorAttention(OwnerPresenceModes.Terminal));
        Assert.False(OwnerPresence_Policy.Suppresses_SupervisorAttention(OwnerPresenceModes.Remote));
    }

    [Fact]
    public void Toggle_GoesBothWays()
    {
        Assert.Equal(OwnerPresenceModes.Terminal, OwnerPresence_Policy.Toggle(OwnerPresenceModes.Remote));
        Assert.Equal(OwnerPresenceModes.Remote, OwnerPresence_Policy.Toggle(OwnerPresenceModes.Terminal));
    }
}
