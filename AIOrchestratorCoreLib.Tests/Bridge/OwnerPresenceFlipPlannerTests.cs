using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

public class OwnerPresenceFlipPlannerTests
{
    static OrchestrationPresence Terminal(string orchId) => new(orchId, OwnerPresenceModes.Terminal);
    static OrchestrationPresence Remote(string orchId) => new(orchId, OwnerPresenceModes.Remote);

    /// <summary>
    /// THE FINDING. Terminal mode had ONE exit and it was topic-scoped, so an owner who walked away
    /// without toggling left that orchestration silent indefinitely. A message from Telegram proves
    /// they are holding a phone — which is proof about EVERY terminal, not just the topic they typed in.
    /// </summary>
    [Fact]
    public void TextingOneTopic_EndsTheMeetingInEVERYOrchestration_NotJustThatOne()
    {
        var flips = OwnerPresenceFlip_Planner.Resolve_Flips(
            [Terminal("arb-fix"), Terminal("option-lab-2")],
            textedOrchId: "arb-fix",
            isPresenceCommand: false);

        Assert.Equal(["arb-fix", "option-lab-2"], flips);
    }

    /// <summary>Texting General from a train demonstrates remoteness just as conclusively.</summary>
    [Fact]
    public void TextingGeneral_EndsAnOrchestrationsMeetingToo()
    {
        var flips = OwnerPresenceFlip_Planner.Resolve_Flips(
            [Terminal("arb-fix")],
            textedOrchId: "general",
            isPresenceCommand: false);

        Assert.Equal(["arb-fix"], flips);
    }

    /// <summary>
    /// The command asking for terminal mode must not be the message that undoes it — the trap the mode
    /// commands already dodge by deferring themselves out of the inbound loop.
    /// </summary>
    [Fact]
    public void ThePresenceCommand_DoesNotFlipTheOrchestrationItTargets()
    {
        var flips = OwnerPresenceFlip_Planner.Resolve_Flips(
            [Terminal("arb-fix")],
            textedOrchId: "arb-fix",
            isPresenceCommand: true);

        Assert.Empty(flips);
    }

    /// <summary>
    /// …and it is exempt for NOTHING ELSE. Typing `/pc` in one topic while another is in terminal mode
    /// proves they are not at that other terminal: they cannot be sitting at two at once.
    /// </summary>
    [Fact]
    public void ThePresenceCommand_StillEndsEveryOTHERMeeting()
    {
        var flips = OwnerPresenceFlip_Planner.Resolve_Flips(
            [Terminal("arb-fix"), Terminal("option-lab-2")],
            textedOrchId: "arb-fix",
            isPresenceCommand: true);

        Assert.Equal(["option-lab-2"], flips);
    }

    /// <summary>An orchestration already Remote is not "flipped" — nothing to say and nothing to log.</summary>
    [Fact]
    public void OrchestrationsAlreadyRemote_AreNeverFlipped()
    {
        var flips = OwnerPresenceFlip_Planner.Resolve_Flips(
            [Remote("arb-fix"), Terminal("option-lab-2")],
            textedOrchId: "arb-fix",
            isPresenceCommand: false);

        Assert.Equal(["option-lab-2"], flips);
    }

    /// <summary>
    /// A message in a topic with no session behind it is still a message from a phone, so it still
    /// ends every meeting. Nothing is exempt because nothing was targeted.
    /// </summary>
    [Fact]
    public void AMessageFromAnUnknownTopic_StillProvesTheyAreNotAtAnyTerminal()
    {
        var flips = OwnerPresenceFlip_Planner.Resolve_Flips(
            [Terminal("arb-fix")],
            textedOrchId: null,
            isPresenceCommand: false);

        Assert.Equal(["arb-fix"], flips);
    }
}
