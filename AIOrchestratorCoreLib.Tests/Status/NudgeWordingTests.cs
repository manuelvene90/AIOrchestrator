using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// ONE RULE: every remedy this text names must be one that can actually change the state it is
/// complaining about. The mid-task nudge broke it twice — it asserted the app knew a session's
/// monitor was dead, which the app cannot see, and then offered three remedies of which none could
/// work. Telling a stuck session to do three things that leave it stuck is worse than saying
/// nothing, because it reads as instructions that were followed.
///
/// These assert against the marker CONSTANTS rather than against copies of the words, so renaming a
/// marker cannot leave the message quietly pointing at vocabulary that no longer exists.
/// </summary>
public class NudgeWordingTests
{
    /// <summary>
    /// The open-window branch is reached if and only if a window is open, and closing it is the only
    /// escape — the resolver tests the window BEFORE the blocked and standing-by markers.
    /// </summary>
    [Fact]
    public void TheOpenWindowNudgeNamesTheOnlyRemedyThatWorks()
    {
        var body = Nudge_Wording.Body_ForOpenWindow(42, "12 min");

        Assert.Contains(MemberState_Resolver.WRITING_WINDOW_CLOSED_MARKER, body);
        Assert.Contains(MemberState_Resolver.MUTATION_WINDOW_CLOSED_MARKER, body);
    }

    /// <summary>
    /// It may still MENTION the other two markers — a member told to "say what you are waiting for"
    /// will reach for them — but only to say they will not work here. If they appear, the disclaimer
    /// must appear too.
    /// </summary>
    [Fact]
    public void TheOpenWindowNudgeDoesNotOfferTheMarkersThatCannotHelp()
    {
        var body = Nudge_Wording.Body_ForOpenWindow(42, "12 min");

        if (body.Contains(MemberState_Resolver.STANDING_BY_MARKER) || body.Contains(MemberState_Resolver.BLOCKED_ON_OWNER_MARKER))
            Assert.Contains("will not stop these", body);
    }

    /// <summary>
    /// The other branch, where the declaration IS the escape: traffic that asked for nothing is the
    /// case that used to nudge an idle member every 8 minutes forever.
    /// </summary>
    [Fact]
    public void TheUnansweredTrafficNudgeOffersTheDeclaration()
    {
        var body = Nudge_Wording.Body_ForUnansweredTraffic(7, "supervisor", "9 min");

        Assert.Contains(MemberState_Resolver.STANDING_BY_MARKER, body);
    }

    /// <summary>
    /// NO CLAIM ABOUT THE MONITOR, in either branch. The app cannot see one, and it said this to a
    /// reviewer whose monitor was alive and had fired on every write for the previous half hour.
    /// </summary>
    [Fact]
    public void NeitherNudgeClaimsToKnowNothingWouldWakeTheSession()
    {
        Assert.DoesNotContain("nothing was going to wake you", Nudge_Wording.Body_ForOpenWindow(1, "10 min"));
        Assert.DoesNotContain("nothing was going to wake you", Nudge_Wording.Body_ForUnansweredTraffic(1, "supervisor", "10 min"));
    }

    /// <summary>The two branches must not be describable by the same subject line.</summary>
    [Fact]
    public void TheTwoSubjectsAreDistinct()
    {
        Assert.NotEqual(Nudge_Wording.Subject_For(true), Nudge_Wording.Subject_For(false));
    }

    [Fact]
    public void BothBodiesQuoteTheEntryAndTheWait()
    {
        Assert.Contains("[42]", Nudge_Wording.Body_ForOpenWindow(42, "12 min"));
        Assert.Contains("12 min", Nudge_Wording.Body_ForOpenWindow(42, "12 min"));
        Assert.Contains("[7]", Nudge_Wording.Body_ForUnansweredTraffic(7, "supervisor", "9 min"));
        Assert.Contains("9 min", Nudge_Wording.Body_ForUnansweredTraffic(7, "supervisor", "9 min"));
    }
}
