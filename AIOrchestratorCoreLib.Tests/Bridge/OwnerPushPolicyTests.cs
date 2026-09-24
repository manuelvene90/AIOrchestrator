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
    /// the one-shot credit three times in one topic; the answer that followed was filed as narration
    /// and never reached the phone. The subject is the run-to-the-end hook's own marker, boundary
    /// included.
    ///
    /// <para>
    /// ONLY THE PREDICATE IS PINNED HERE, not <see cref="OwnerPush_Policy.Should_Push"/>: that is the
    /// <c>everything</c> arm, where the credit decides nothing about the push. The engine is one
    /// reader — it refuses to CONSUME the credit on a turn-end subject — and the push half, under
    /// <c>phone.push = filtered</c>, is pinned in <c>OwnerPushDeciderTests</c>.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("WAITING ON the task 6 re-review - fix landed 7af0aafe, 116 tests")]
    [InlineData("fix landed — WAITING ON the suite")]
    [InlineData("WAITING ON: the build")]
    [InlineData("WAITING ON")]
    public void AWaitingOnSubject_IsATurnEndDeclaration(string subject)
    {
        Assert.True(OwnerPush_Policy.Is_TurnEndDeclaration(subject));
    }

    /// <summary>"WAITING ONLY" contains "WAITING ON" — the boundary the hook enforces, kept here.</summary>
    [Theory]
    [InlineData("WAITING ONLY for the reviewer")]
    [InlineData("defaults per root, C is the continuous automatically")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElse_IsNotATurnEndDeclaration(string? subject)
    {
        Assert.False(OwnerPush_Policy.Is_TurnEndDeclaration(subject));
    }

    /// <summary>
    /// The body says nothing about what an entry IS: sessions end nearly every entry — answers
    /// included — with a "WAITING ON …" line to satisfy the turn-end hook. Only the subject counts,
    /// which is why the predicate takes a subject and never the raw text.
    /// </summary>
    [Fact]
    public void AWaitingOnLineInTheBody_DoesNotMakeTheEntryAStatusLine()
    {
        Assert.False(OwnerPush_Policy.Is_TurnEndDeclaration("defaults per root, C is the continuous automatically"));
    }

    /// <summary>
    /// The waterfall. Every one of these is real narration from the transcript that prompted this —
    /// useful in the channel, noise on a phone.
    ///
    /// <para>
    /// MEASURED UNDER BOTH MODES SINCE PLAN 03 (<c>phone.push</c>), and the name keeps its meaning under
    /// <see cref="PhonePushModes.Everything"/>: there the filter IS gone. The owner's ruling of
    /// 2026-09-09 — *"If the supervisor writes to me, I must know it"* — came after their own quoted
    /// example of a message they needed was suppressed and arrived five minutes late through the
    /// deadlock net, in raw Markdown; the brake on chatter under that mode is the SKILL and the brevity
    /// nudge, and five progress entries in ten minutes are five notifications.
    /// </para>
    /// <para>
    /// Under <see cref="PhonePushModes.Filtered"/> — master's, and classic's — the same entries are
    /// HELD for the turn-end digest instead: not rung, not lost. Both answers are now a choice rather
    /// than a build, which is why one theory pins both rather than one file pinning whichever shipped.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(PhonePushModes.Everything, "## [11] FROM supervisor — d — s\nimp-1 is pricing the matrix; rev-1 has the diff.", OwnerPushDecisions.SendNow)]
    [InlineData(PhonePushModes.Everything, "## [12] FROM supervisor — d — s\nConfirmed: the provider list is complete.", OwnerPushDecisions.SendNow)]
    [InlineData(PhonePushModes.Everything, "## [13] FROM supervisor — d — s\nAccepted imp-3's report and merged it to staging.", OwnerPushDecisions.SendNow)]
    [InlineData(PhonePushModes.Filtered, "## [11] FROM supervisor — d — s\nimp-1 is pricing the matrix; rev-1 has the diff.", OwnerPushDecisions.HoldForDigest)]
    [InlineData(PhonePushModes.Filtered, "## [12] FROM supervisor — d — s\nConfirmed: the provider list is complete.", OwnerPushDecisions.HoldForDigest)]
    [InlineData(PhonePushModes.Filtered, "## [13] FROM supervisor — d — s\nAccepted imp-3's report and merged it to staging.", OwnerPushDecisions.HoldForDigest)]
    public void ProgressNarration_IsPushed_NowThatTheFilterIsGone(PhonePushModes mode, string entry, OwnerPushDecisions expected)
    {
        Assert.Equal(expected, OwnerPush_Policy.Decide(mode, entry, ownerIsWaitingForAReply: false, subject: "s"));

        // Should_Push is the Everything arm and nothing else, so it pushes in both rows.
        Assert.True(OwnerPush_Policy.Should_Push(entry, ownerIsWaitingForAReply: false));
    }

    [Theory]
    [InlineData("## [3] FROM solo — d — s\nInvestigating the ? glyph, the rename and the hook now.")]
    [InlineData("## [5] FROM solo — d — s\nSTANDING BY.\nLedger line sits at [?] until they answer.")]
    [InlineData("## [9] FROM solo — d — s\nMarked it - [?] blocked on you, the rest continues.")]
    public void AQueryMarkThatDoesNotEndALine_IsNotAnAsk(string entry)
    {
        // ASKS_INPROSE STILL MATTERS — the topic's ❓ glyph and the typed-answer binding read it —
        // but it no longer decides whether an entry is PUSHED: everything the supervisor writes is.
        Assert.False(OwnerPush_Policy.Asks_InProse(entry));
        Assert.True(OwnerPush_Policy.Should_Push(entry, ownerIsWaitingForAReply: false));
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

        // Pushed all the same — it is the supervisor writing to the owner. What the fenced block
        // must not do is make the topic wear a ❓ or absorb a typed reply as an answer.
        Assert.True(OwnerPush_Policy.Should_Push(entry, false));
    }

    /// <summary>
    /// The owner's own words with the session's label on them (2026-09-07): not a reply, and it must
    /// not spend the wait — so it is refused even while they ARE waiting, which is the one condition
    /// that otherwise pushes anything.
    /// </summary>
    [Fact]
    public void TheOwnerQuotedBackToThemselves_IsNotPushed_EvenWhileTheyWait()
    {
        var entry = "## [12] FROM supervisor — d — s\nOwner: \"What do you think? The old app does not ask for it.\"";

        Assert.True(OwnerPush_Policy.Is_OwnerRestatement(entry));

        // THE SECOND OF THE TWO SURVIVING REFUSALS, and it is refused even while they ARE waiting —
        // the one condition that used to push anything. Their own words with the session's label on
        // them say nothing they did not just type, and they would spend the wait the real answer
        // needs.
        Assert.False(OwnerPush_Policy.Should_Push(entry, ownerIsWaitingForAReply: true));
    }

    [Theory]
    [InlineData("## [12] FROM supervisor — d — s\nThe owner said: “Che ne pensi? La vecchia app non lo richiede.”")]
    [InlineData("## [12] FROM supervisor — d — s\n\nowner asked: 'is the rebuild done'\n")]
    public void EveryShapeOfTheQuotation_IsARestatement(string entry)
    {
        Assert.True(OwnerPush_Policy.Is_OwnerRestatement(entry));
    }

    /// <summary>A reply that opens by quoting them and then answers is a reply; only the bare quote is caught.</summary>
    [Fact]
    public void AReplyThatQuotesThemAndGoesOn_IsPushed_WhileTheyWait()
    {
        var entry = "## [12] FROM supervisor — d — s\nOwner: \"is the rebuild done\"\nYes — 214 green, merged at 12:40.";

        Assert.False(OwnerPush_Policy.Is_OwnerRestatement(entry));
        Assert.True(OwnerPush_Policy.Should_Push(entry, ownerIsWaitingForAReply: true));
    }

    /// <summary>Their words without the "Owner:" label are just an answer in quotation marks — untouched.</summary>
    [Fact]
    public void AQuotedLineWithoutTheOwnerLabel_IsNotARestatement()
    {
        var entry = "## [12] FROM supervisor — d — s\n\"No card\" is the ruling, as in the old app.";

        Assert.False(OwnerPush_Policy.Is_OwnerRestatement(entry));
        Assert.True(OwnerPush_Policy.Should_Push(entry, ownerIsWaitingForAReply: true));
    }

    [Fact]
    /// <summary>
    /// THE ONE THING STILL NOT PUSHED besides the owner's own words quoted back: an entry with no
    /// body. Not a filter — Telegram refuses an empty message, and there is nothing to read.
    /// </summary>
    public void AnEmptyEntry_IsStillNotPushed()
    {
        // ONE OF THE TWO SURVIVING REFUSALS, and the reason it is not a filter: Telegram refuses an
        // empty message, and there is nothing in one to read.
        Assert.False(OwnerPush_Policy.Should_Push("", ownerIsWaitingForAReply: false));
        Assert.False(OwnerPush_Policy.Should_Push("   \n  ", ownerIsWaitingForAReply: true));
    }

    /// <summary>
    /// The button label is what fits on a phone; the text the supervisor receives is the actual
    /// instruction. They MUST differ — a supervisor told only "💬 Let's talk" would have to guess
    /// what was being asked of it, and it must know to re-ask afterwards.
    ///
    /// <para>
    /// THE RE-ASK IS THE PART THAT INVERTED. The instruction used to end with "do NOT ask it again",
    /// which was right only while the tap left the question live on the phone. It closes the
    /// question now, so a supervisor obeying the old sentence leaves the decision permanently
    /// untaken — which is what happened on 2026-09-09 to an orphaned-processes question.
    /// </para>
    /// </summary>
    [Fact]
    public void TheTalkButton_SendsAFullInstruction_NotItsOwnLabel_AndEndsByReAsking()
    {
        Assert.NotEqual(OwnerPush_Policy.TALK_LABEL, OwnerPush_Policy.TALK_REQUEST);
        Assert.True(OwnerPush_Policy.TALK_LABEL.Length <= 30, "the label has to fit a phone button");

        Assert.Contains("recommend", OwnerPush_Policy.TALK_REQUEST);
        Assert.Contains("costs to get wrong", OwnerPush_Policy.TALK_REQUEST);
        Assert.Contains("ask the question again", OwnerPush_Policy.TALK_REQUEST);
        Assert.Contains("briefly", OwnerPush_Policy.TALK_REQUEST, StringComparison.OrdinalIgnoreCase);

        // The inverted order it replaces, in the exact words that shipped.
        Assert.DoesNotContain("do NOT ask it again", OwnerPush_Policy.TALK_REQUEST, StringComparison.Ordinal);
    }

    /// <summary>
    /// The owner's own wording for what the message must say after the tap: *"the message says 'ok,
    /// tell me what you have in mind'"*. Short, because it is an acknowledgement rather than a
    /// second question.
    /// </summary>
    [Fact]
    public void TheTalkAcknowledgement_IsWhatTheMessageBecomes_AndItRecordsNoChoice()
    {
        Assert.Contains("Ok", OwnerPush_Policy.TALK_ACKNOWLEDGEMENT, StringComparison.Ordinal);
        Assert.Contains("what you have in mind", OwnerPush_Policy.TALK_ACKNOWLEDGEMENT, StringComparison.Ordinal);

        // ✅ is the record of a CHOICE, and no choice was made by tapping this.
        Assert.DoesNotContain("✅", OwnerPush_Policy.TALK_ACKNOWLEDGEMENT, StringComparison.Ordinal);
    }

    /// <summary>
    /// MASTER'S BUTTON, VERBATIM (owner, 2026-09-24 entry [123]: "I more often use the explain in more details
    /// feature"). The label and the request are master's words at <c>a58ef7e</c> — <c>MORE_DETAIL_LABEL</c> and
    /// <c>MORE_DETAIL_REQUEST</c> there — pinned as literals so a paraphrase cannot pass as a restoration.
    /// </summary>
    [Fact]
    public void TheExplainButton_IsMastersLabelAndMastersRequest_Verbatim()
    {
        Assert.Equal("❔ Explain the options", OwnerPush_Policy.EXPLAIN_LABEL);
        Assert.Equal(
            "Explain this decision before I choose: what each option actually means in practice, what "
            + "differs between them, what it costs to get wrong, and which one you recommend and why. "
            + "Keep it short. Then ask the question again.",
            OwnerPush_Policy.EXPLAIN_REQUEST);

        Assert.True(OwnerPush_Policy.EXPLAIN_LABEL.Length <= 30, "the label has to fit a phone button");
        Assert.NotEqual(OwnerPush_Policy.TALK_LABEL, OwnerPush_Policy.EXPLAIN_LABEL);
        Assert.NotEqual(OwnerPush_Policy.TALK_REQUEST, OwnerPush_Policy.EXPLAIN_REQUEST);
    }

    /// <summary>The Explain tap closes its question like Let's talk does, so it leaves a record in the same style — and no ✅.</summary>
    [Fact]
    public void TheExplainAcknowledgement_IsInTheTalkStyle_AndRecordsNoChoice()
    {
        Assert.StartsWith("❔ Ok — ", OwnerPush_Policy.EXPLAIN_ACKNOWLEDGEMENT, StringComparison.Ordinal);
        Assert.Contains("explaining the options", OwnerPush_Policy.EXPLAIN_ACKNOWLEDGEMENT, StringComparison.Ordinal);
        Assert.DoesNotContain("✅", OwnerPush_Policy.EXPLAIN_ACKNOWLEDGEMENT, StringComparison.Ordinal);
        Assert.NotEqual(OwnerPush_Policy.TALK_ACKNOWLEDGEMENT, OwnerPush_Policy.EXPLAIN_ACKNOWLEDGEMENT);
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

        // AND IT IS PUSHED ANYWAY, which is the change: the greeting exemption existed because the
        // filter suppressed everything else. With the filter gone (2026-09-09) what Is_OnlineGreeting
        // still decides is the topic's own bookkeeping, never whether the entry reaches the phone.
        Assert.True(OwnerPush_Policy.Should_Push($"## [3] FROM supervisor — d — {subject}\nbody", ownerIsWaitingForAReply: false, subject));
    }

    [Fact]
    public void NoSubject_ChangesNothing_ForCallersThatDoNotPassOne()
    {
        Assert.False(OwnerPush_Policy.Is_OnlineGreeting(null));
        Assert.False(OwnerPush_Policy.Is_OnlineGreeting("   "));

        // A caller with no subject to offer pushes exactly as one with a subject does.
        Assert.True(OwnerPush_Policy.Should_Push("## [3] FROM supervisor — d — s\nbody", ownerIsWaitingForAReply: false));
    }

    [Fact]
    public void AnEntryCarryingAFile_IsPushed_BecauseAFileIsADeliveryAndNotNarration()
    {
        // Pure narration by every other rule: no question mark, no marker, no BLOCKED.
        Assert.True(OwnerPush_Policy.Should_Push(
            "The plan-card mockup is ready.\nATTACH: /repo/mockups/plan-card.html\nOpen it in a browser.",
            ownerIsWaitingForAReply: false));

        // The same hole IMAGE: had, silently, until 2026-09-07.
        Assert.True(OwnerPush_Policy.Should_Push(
            "The comparison table now shows the cap.\nIMAGE: /repo/shots/table.png",
            ownerIsWaitingForAReply: false));
    }

    /// <summary>
    /// Renamed with the filter's removal: a prose mention of ATTACH:/IMAGE: no longer decides
    /// anything about the PUSH (everything the supervisor writes is pushed). What it still decides
    /// is whether an UPLOAD happens, which is what this always actually tested.
    /// </summary>
    [Fact]
    public void AProseMentionOfTheMarkers_DeliversNoFile()
    {
        // Only a column-0 marker line produces an upload — the engine's extractor is anchored.

        Assert.False(OwnerPush_Policy.Carries_FileForTheOwner("ATTACH:"));
        Assert.False(OwnerPush_Policy.Carries_FileForTheOwner("ATTACH:   "));
    }
}
