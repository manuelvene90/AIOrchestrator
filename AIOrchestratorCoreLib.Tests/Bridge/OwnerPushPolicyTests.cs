using AIOrchestratorCoreLib.Bridge;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// Owner: "I answer the sup a question, and then the sup doesn't disturb me anymore unless it has
/// another question. A brief every 30 minutes about how the work is going is fine, but not the
/// waterfall of messages I get now."
///
/// Nothing is deleted by this — every entry still lands in the channel and the app. It decides only
/// what becomes a NOTIFICATION.
/// </summary>
public class OwnerPushPolicyTests
{
    [Fact]
    public void AQuestion_IsPushed()
    {
        var entry = "## [7] FROM supervisor — d — s\nQUESTION: merge now or hold?\nOPTION: Merge\nOPTION: Hold";

        Assert.True(OwnerPush_Policy.Should_Push(entry, ownerIsWaitingForAReply: false));
    }

    [Fact]
    public void BlockedOnOwner_IsPushed_BecauseOnlyTheyCanRestartIt()
    {
        Assert.True(OwnerPush_Policy.Should_Push("## [3] FROM supervisor — d — s\nBLOCKED ON OWNER: need the token", false));
    }

    /// <summary>The reply to something they asked must always get through — they are waiting for it.</summary>
    [Fact]
    public void AnAnswer_IsPushed_EvenWithNoQuestionInIt()
    {
        Assert.True(OwnerPush_Policy.Should_Push("## [9] FROM supervisor — d — s\nYes, the daily DD is feasible.", ownerIsWaitingForAReply: true));
    }

    /// <summary>
    /// THE OWNER'S WAIT IS NOT SPENT ON A STATUS LINE. Sessions write a "WAITING ON …" turn-end
    /// declaration in the seconds before the actual answer, and on 2026-09-10 the declaration took
    /// the one-shot credit three times in one topic; the answer that followed was narration by shape
    /// and never reached the phone. The subject is the hook's marker, boundary included.
    /// </summary>
    [Theory]
    [InlineData("WAITING ON the task 6 re-review - fix landed 7af0aafe, 116 tests")]
    [InlineData("fix landed — WAITING ON the suite")]
    [InlineData("WAITING ON: the build")]
    [InlineData("WAITING ON")]
    public void AWaitingOnStatusLine_DoesNotSpendTheOwnersWait(string subject)
    {
        var narration = "## [11] FROM solo — d — s\nTask 6 fix round landed and the re-review is running.";

        Assert.False(OwnerPush_Policy.Should_Push(narration, ownerIsWaitingForAReply: true, subject));
        Assert.True(OwnerPush_Policy.Is_TurnEndDeclaration(subject));
    }

    /// <summary>"WAITING ONLY" contains "WAITING ON" — the boundary the hook enforces, kept here.</summary>
    [Theory]
    [InlineData("WAITING ONLY for the reviewer")]
    [InlineData("defaults per root, C is the continuous automatically")]
    [InlineData("")]
    public void AnythingElse_StillSpendsTheOwnersWait(string subject)
    {
        var answer = "## [12] FROM solo — d — s\nAgreed: every futures root carries a default roll rule.";

        Assert.True(OwnerPush_Policy.Should_Push(answer, ownerIsWaitingForAReply: true, subject));
        Assert.False(OwnerPush_Policy.Is_TurnEndDeclaration(subject));
    }

    /// <summary>A status line that ASKS something still pushes on its own merits — the credit was never the only route.</summary>
    [Fact]
    public void AWaitingOnStatusLine_WithAQuestionInIt_IsStillPushed()
    {
        var entry = "## [13] FROM solo — d — s\nQUESTION: merge now or hold?\nOPTION: Merge\nOPTION: Hold";

        Assert.True(OwnerPush_Policy.Should_Push(entry, ownerIsWaitingForAReply: true, "WAITING ON your call"));
    }

    /// <summary>
    /// The body says nothing about what an entry IS: sessions end nearly every entry — answers
    /// included — with a "WAITING ON …" line to satisfy the turn-end hook. Only the subject counts.
    /// </summary>
    [Fact]
    public void AWaitingOnLineInTheBody_DoesNotMakeTheEntryAStatusLine()
    {
        var answer = "## [14] FROM solo — d — s\nAgreed, three rules.\nWAITING ON the task 6 re-review (sub-agent).";

        Assert.True(OwnerPush_Policy.Should_Push(answer, ownerIsWaitingForAReply: true, "defaults per root"));
    }

    /// <summary>
    /// The waterfall. Every one of these is real narration from the transcript that prompted this —
    /// useful in the channel, noise on a phone.
    /// </summary>
    [Theory]
    [InlineData("## [11] FROM supervisor — d — s\nimp-1 is pricing both options now, still read-only.")]
    [InlineData("## [12] FROM supervisor — d — s\nConfirmed: the preliminary simulation is the mechanism.")]
    [InlineData("## [13] FROM supervisor — d — s\nAccepted imp-3's Task 6; the ledger is updated.")]
    public void ProgressNarration_IsNotPushed(string entry)
    {
        Assert.False(OwnerPush_Policy.Should_Push(entry, ownerIsWaitingForAReply: false));
    }

    /// <summary>
    /// The dangerous case: a real question asked in prose, with no marker. Dropping it would leave
    /// the supervisor waiting for an answer the owner never saw — a deadlock neither can observe.
    /// A false positive costs one ignorable message; a false negative costs the whole conversation.
    /// </summary>
    [Theory]
    [InlineData("## [4] FROM supervisor — d — s\nShould I merge this into master or hold it?")]
    [InlineData("## [4] FROM supervisor — d — s\nDo you want the synthetic drawdown or the real one?")]
    [InlineData("## [4] FROM supervisor — d — s\nwhich approach do you prefer")]
    public void AQuestionInPlainProse_IsStillPushed(string entry)
    {
        // The last case has no '?' and is NOT caught by this filter — deliberately asserted so the
        // boundary stays visible. It is not a deadlock: an entry this filter suppresses is
        // remembered, and the engine releases it once the supervisor AND every member have been
        // idle for minutes (Break_SilentDeadlock_Async). The filter is the fast path; that is the
        // guarantee.
        var pushed = OwnerPush_Policy.Should_Push(entry, ownerIsWaitingForAReply: false);

        Assert.Equal(entry.Contains('?'), pushed);
    }

    /// <summary>
    /// A QUERY MARK THAT DOES NOT END A LINE IS NOT AN ASK, and this is the case that broke.
    ///
    /// 2026-08-21, the owner: *"after I put it in pc mode a ? icon appeared in the name for no
    /// reason"*, and minutes later, of a second topic, *"you just put ? in the topic name"*. Both
    /// entries were reports. What they had in common is a query mark used as a NOUN or inside a
    /// LEDGER MARKER — "the ? glyph", "sits at [?] until they answer" — and a bare Contains
    /// cannot tell those from a question. Sessions write about the `- [?]` marker constantly, so
    /// this was not a rare shape: it lit the topic glyph and pushed the phone on plain narration.
    ///
    /// The replacement is deliberately blunt rather than clever: a prose question ENDS its line
    /// with the mark. Anything else uses the explicit markers, which every role command already
    /// tells sessions to use when they actually need a decision.
    /// </summary>
    [Theory]
    [InlineData("## [3] FROM solo — d — s\nInvestigating the ? glyph, the rename and the hook now.")]
    [InlineData("## [5] FROM solo — d — s\nSTANDING BY.\nLedger line sits at [?] until they answer.")]
    [InlineData("## [9] FROM solo — d — s\nMarked it - [?] blocked on you, the rest continues.")]
    public void AQueryMarkThatDoesNotEndALine_IsNotAnAsk(string entry)
    {
        Assert.False(OwnerPush_Policy.Asks_InProse(entry));
        Assert.False(OwnerPush_Policy.Should_Push(entry, ownerIsWaitingForAReply: false));
    }

    /// <summary>
    /// The other half of the boundary: ending the line is all it takes, on ANY line of the entry,
    /// so a question followed by more prose still counts and trailing spaces do not hide it.
    /// </summary>
    [Theory]
    [InlineData("## [4] FROM solo — d — s\nShould I merge this or hold it?")]
    [InlineData("## [4] FROM solo — d — s\nMerge or hold?\nEither is cheap from here.")]
    [InlineData("## [4] FROM solo — d — s\ntrailing spaces do not hide it?   ")]
    public void AQueryMarkEndingAnyLine_IsAnAsk(string entry)
    {
        Assert.True(OwnerPush_Policy.Asks_InProse(entry));
    }

    /// <summary>A '?' inside an ASCII mockup or snippet is not the supervisor asking anything.</summary>
    [Fact]
    public void AQuestionMarkInsideAFencedBlock_DoesNotCount()
    {
        var entry = "## [5] FROM supervisor — d — s\nprogress update\n```\n| ready? | yes |\n```\nnothing else";

        Assert.False(OwnerPush_Policy.Asks_InProse(entry));
        Assert.False(OwnerPush_Policy.Should_Push(entry, false));
    }

    [Fact]
    public void EmptyEntry_IsNeverPushed()
    {
        Assert.False(OwnerPush_Policy.Should_Push("", false));
    }

    /// <summary>
    /// The button label is what fits on a phone; the text the supervisor receives is the actual
    /// instruction. They MUST differ — a supervisor told only "❔ Explain the options" would have to
    /// guess what was being asked of it, and it must know to re-ask afterwards.
    /// </summary>
    [Fact]
    public void TheExplainButton_SendsAFullInstruction_NotItsOwnLabel()
    {
        Assert.NotEqual(OwnerPush_Policy.MORE_DETAIL_LABEL, OwnerPush_Policy.MORE_DETAIL_REQUEST);
        Assert.True(OwnerPush_Policy.MORE_DETAIL_LABEL.Length <= 30, "the label has to fit a phone button");

        Assert.Contains("recommend", OwnerPush_Policy.MORE_DETAIL_REQUEST);
        Assert.Contains("costs to get wrong", OwnerPush_Policy.MORE_DETAIL_REQUEST);
        Assert.Contains("ask the question again", OwnerPush_Policy.MORE_DETAIL_REQUEST);
        Assert.Contains("short", OwnerPush_Policy.MORE_DETAIL_REQUEST, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Carries_Question_SpotsEitherMarker()
    {
        Assert.True(OwnerPush_Policy.Carries_Question("QUESTION: which one?"));
        Assert.True(OwnerPush_Policy.Carries_Question("OPTION: Merge it"));
        Assert.False(OwnerPush_Policy.Carries_Question("I have a question about the design"));
    }
}

/// <summary>
/// The boot greeting — the FOURTH thing the phone gets, added 2026-08-25.
///
/// The owner could not tell when a session was ready to be written to: *"At the start of a new
/// session I don't receive a message from the sup/solo telling me it's online and ready, so I don't
/// know when I can start writing. Absurdly I receive a message from the impl saying it's online …
/// I should receive Sup Online, or Solo Online, and I also want Rev1 Online."*
///
/// A greeting is progress narration by shape, so the policy above killed it; a member's identical
/// greeting only ever got through because a spoke channel never meets this policy at all.
/// </summary>
public class OnlineGreetingPushTests
{
    [Theory]
    [InlineData("supervisor online — AIOrchestrator — repos\\AIOrchestrator")]
    [InlineData("solo online — CRM — Projects\\Prova Amazon")]
    [InlineData("imp-1 online")]
    [InlineData("rev-1 online")]
    [InlineData("general supervisor online")]
    [InlineData("online")]
    public void EveryRolesBootGreeting_IsPushed(string subject)
    {
        // EMPTY BODY IS THE NORMAL CASE and it is what made this unrescuable: supervisor.md and
        // solo.md both mandate it, so there is not even a stray '?' for Asks_InProse to catch.
        var entry = $"## [1] FROM supervisor — 2026-08-25 09:49 — {subject}";

        Assert.True(OwnerPush_Policy.Should_Push(entry, ownerIsWaitingForAReply: false, subject));
    }

    /// <summary>
    /// The word in a SENTENCE is not a greeting. Matching the raw text would have pushed every
    /// entry that happened to mention being online — narration again, wearing the exemption.
    /// </summary>
    [Theory]
    [InlineData("the build is online again after the outage")]
    [InlineData("checked whether imp-2 was online")]
    [InlineData("brought the staging environment back online")]
    public void TheWordInProse_IsNotAGreeting(string subject)
    {
        Assert.False(OwnerPush_Policy.Is_OnlineGreeting(subject));
        Assert.False(OwnerPush_Policy.Should_Push($"## [8] FROM supervisor — d — {subject}\nnothing to decide here.", false, subject));
    }

    [Fact]
    public void NoSubject_ChangesNothing_ForCallersThatDoNotPassOne()
    {
        Assert.False(OwnerPush_Policy.Is_OnlineGreeting(null));
        Assert.False(OwnerPush_Policy.Is_OnlineGreeting("   "));
        Assert.False(OwnerPush_Policy.Should_Push("## [12] FROM supervisor — d — s\nimp-1 is still pricing.", false));
    }
}
