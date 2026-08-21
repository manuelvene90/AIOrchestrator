using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// ONLY `/pc` ENDS TERMINAL MODE (owner's ruling, 2026-08-21). This reverses the rule these tests
/// pinned before: an ordinary message used to end every meeting, on the reasoning that texting
/// PROVES the owner is holding a phone. The reasoning was sound and the behaviour was still wrong —
/// the owner sets `/pc` because they are at the keyboard, glances at their phone, and the mode they
/// just asked for is revoked without a word. Their log has it twice: set 10:43:02, revoked 10:45:55
/// "they texted Telegram"; and on 2026-08-18, set 12:33:18, revoked 12:33:39 — twenty-one seconds.
///
/// The cost is the trap that widening it was meant to fix: an owner who walks away without toggling
/// leaves that topic silent. That is accepted deliberately — the 💻 in the topic name is the standing
/// reminder, which the auto-flip never was.
/// </summary>
public class OwnerPresenceFlipPlannerTests
{
    static OrchestrationPresence Terminal(string orchId) => new(orchId, OwnerPresenceModes.Terminal);
    static OrchestrationPresence Remote(string orchId) => new(orchId, OwnerPresenceModes.Remote);

    /// <summary>
    /// THE RULING ITSELF, and the case that produced it. An ordinary message ends NOTHING — not the
    /// topic it arrived in, not any other. The owner texting while sitting at a terminal is the
    /// normal case, not evidence against the mode they just set.
    /// </summary>
    [Fact]
    public void AnOrdinaryMessage_EndsNoMeetingAtAll()
    {
        var flips = OwnerPresenceFlip_Planner.Resolve_Flips(
            [Terminal("arb-fix"), Terminal("option-lab-2")],
            textedOrchId: "arb-fix",
            isPresenceCommand: false);

        Assert.Empty(flips);
    }

    /// <summary>Texting General is a message like any other, so it ends nothing either.</summary>
    [Fact]
    public void TextingGeneral_EndsNoMeeting()
    {
        var flips = OwnerPresenceFlip_Planner.Resolve_Flips(
            [Terminal("arb-fix")],
            textedOrchId: "general",
            isPresenceCommand: false);

        Assert.Empty(flips);
    }

    /// <summary>
    /// A message from a topic with no session behind it is still just a message. Before the ruling
    /// this was the strongest "they are demonstrably on a phone" case; it is now worth nothing.
    /// </summary>
    [Fact]
    public void AMessageFromAnUnknownTopic_EndsNoMeeting()
    {
        var flips = OwnerPresenceFlip_Planner.Resolve_Flips(
            [Terminal("arb-fix")],
            textedOrchId: null,
            isPresenceCommand: false);

        Assert.Empty(flips);
    }

    /// <summary>
    /// The command asking for terminal mode must not be the message that undoes it — the trap the
    /// mode commands already dodge by deferring themselves out of the inbound loop.
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
    /// THE HALF THAT SURVIVES THE RULING, and it survives because it is still `/pc` doing the ending.
    /// Typing `/pc` in one topic while another is in terminal mode proves they are not at that other
    /// terminal: they cannot be sitting at two at once.
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
            isPresenceCommand: true);

        Assert.Equal(["option-lab-2"], flips);
    }
}
