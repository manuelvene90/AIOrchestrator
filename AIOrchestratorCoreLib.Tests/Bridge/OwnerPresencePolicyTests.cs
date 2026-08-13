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

    // The three flip tests that stood here moved to OwnerPresenceFlipPlannerTests with the rule
    // itself: an owner message ends EVERY meeting, not only the topic it arrived in, so the decision
    // needs the whole roster and no longer fits a one-orchestration predicate. All three cases are
    // preserved there (Terminal flips, the presence command is exempt for its own target, Remote has
    // nothing to flip) — they were not dropped, and leaving them here would have left three greens
    // pinning a function nothing calls.

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
