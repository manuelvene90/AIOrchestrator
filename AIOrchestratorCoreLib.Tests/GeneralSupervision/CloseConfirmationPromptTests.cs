using AIOrchestratorCoreLib.GeneralSupervision.ParkedCloseRequest;
using AIOrchestratorCoreLib.GeneralSupervision.SpawnSiblingRequest;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.GeneralSupervision;

/// <summary>
/// The sentence the owner taps on. It is the entire user-facing surface of this guard: everything
/// else in the unit exists to put this text in front of them at the right moment.
///
/// A prompt that describes the WRONG close is worse than no guard at all — a guard that misleads at
/// the only moment it is read converts the owner's caution into a confirmation of something they did
/// not intend.
/// </summary>
public class CloseConfirmationPromptTests
{
    static readonly IParkedCloseRequest MEMBER =
        ParkedCloseRequest_Factory.Create_ForImplementer("crm-2", "imp-2", "a session in crm-2", "its task is delivered", "parked.json");

    static readonly IParkedCloseRequest ORCHESTRATION =
        ParkedCloseRequest_Factory.Create_ForOrchestration("crm-2", "supervisor of crm-2", "work is done", "parked.json");

    /// <summary>
    /// A member close names the member and says what SURVIVES. Without the second half the owner is
    /// asked to confirm something that reads exactly like ending everything.
    /// </summary>
    [Fact]
    public void AMemberPromptNamesTheMemberAndSaysTheRestKeepsRunning()
    {
        var text = CloseConfirmationPrompt_Builder.Build(MEMBER, null);

        Assert.Contains("imp-2", text);
        Assert.Contains("keep running", text);
        Assert.DoesNotContain("ends every session", text);
        Assert.DoesNotContain("deletes this topic", text);
    }

    /// <summary>And the orchestration prompt still says the thing that makes it the heavier close.</summary>
    [Fact]
    public void AnOrchestrationPromptSaysEverySessionEnds()
    {
        var text = CloseConfirmationPrompt_Builder.Build(ORCHESTRATION, null);

        Assert.Contains("ends every session", text);
        Assert.Contains("deletes this topic", text);
    }

    /// <summary>
    /// THE MISLEAD CASE, pinned from the wrong side on purpose: the ledger warning is passed in and
    /// must NOT reach a member prompt.
    ///
    /// The ledger belongs to the orchestration, so "3 lines neither done nor dropped" says nothing
    /// about one member being safe to retire — beside a one-member close it reads as "these lines die
    /// with it", which is false and pushes the owner toward keeping a session alive for a reason that
    /// does not apply. Asserting only that the orchestration prompt CONTAINS it would leave this
    /// direction unpinned, and this is the direction that misinforms.
    /// </summary>
    [Fact]
    public void TheLedgerWarningReachesAnOrchestrationPromptAndNeverAMemberOne()
    {
        const string LEDGER = "3 line(s) neither done nor dropped";

        Assert.Contains(LEDGER, CloseConfirmationPrompt_Builder.Build(ORCHESTRATION, LEDGER));
        Assert.DoesNotContain(LEDGER, CloseConfirmationPrompt_Builder.Build(MEMBER, LEDGER));
    }

    /// <summary>
    /// Both prompts say the tap is the only thing that acts. It is the sentence that makes a parked
    /// request safe to leave sitting there, and it is easy to lose when a second kind is added.
    /// </summary>
    [Fact]
    public void BothPromptsSayNothingHappensWithoutATap()
    {
        Assert.Contains("Nothing happens unless you tap", CloseConfirmationPrompt_Builder.Build(MEMBER, null));
        Assert.Contains("Nothing happens unless you tap", CloseConfirmationPrompt_Builder.Build(ORCHESTRATION, null));
    }

    /// <summary>
    /// The mid-sentence wording the held / declined / lapsed notices all share. One source, because
    /// four copies of "this orchestration" is how the notice about retiring one member comes to tell
    /// a supervisor its whole orchestration was up for closure.
    ///
    /// IT CARRIES ITS OWN VERB NOW. The earlier version named only the object and let each caller
    /// supply the verb, which is fine while every request is a close and produced "You asked to close
    /// the promotion to a full crew" the day one was not.
    /// </summary>
    [Fact]
    public void TheAskDescriptionNamesTheMemberButNotForAnOrchestration()
    {
        Assert.Equal("the close of 'imp-2'", CloseConfirmationPrompt_Builder.Describe_AskedFor(MEMBER));
        Assert.Equal("the close of this orchestration", CloseConfirmationPrompt_Builder.Describe_AskedFor(ORCHESTRATION));
    }

    /// <summary>
    /// A member close cannot be constructed without naming the member. The executor branches on the
    /// kind and dereferences MemberId, so a kind that could exist without one would be a null
    /// reference at the moment of a confirmed tap — and the close it was meant to perform would fail
    /// after the owner had already approved it.
    /// </summary>
    [Fact]
    public void AMemberCloseCannotBeBuiltWithoutTheMember()
    {
        Assert.Throws<ArgumentException>(() =>
            ParkedCloseRequest_Factory.Create_ForImplementer("crm-2", "", "asker", "reason", "parked.json"));
    }

    // ── What the prompt is REPLACED with, once the tap has been acted on ──────────────────────────

    [Fact]
    public void AConfirmedCloseThatRanIsReportedAsClosed()
    {
        Assert.Contains("✅ Closed — you confirmed.", Decide(CloseTapOutcomes.Closed));
    }

    [Fact]
    public void ADeclineSaysTheSessionsKeepRunning()
    {
        Assert.Contains("✋ Kept open — you declined.", Decide(CloseTapOutcomes.Declined));
        Assert.Contains("sessions keep running", Decide(CloseTapOutcomes.Declined));
    }

    /// <summary>
    /// THE DECISION MUST NAME WHAT THE PROMPT NAMED. Every one of these sentences used to open
    /// "Close 'crm-2'?" whatever had been tapped, so retiring one member announced itself under the
    /// orchestration's name — which this file's own header calls the worst version of this feature.
    /// The kind was in hand at the call site and was being discarded on the way back.
    /// </summary>
    [Fact]
    public void AMemberDecisionNamesTheMemberAndNotTheOrchestrationAlone()
    {
        foreach (var outcome in Enum.GetValues<CloseTapOutcomes>())
        {
            var text = CloseConfirmationPrompt_Builder.Describe_Decision("crm-2", MEMBER, outcome);

            Assert.Contains("member 'imp-2'", text);
            Assert.Contains("crm-2", text);
        }
    }

    /// <summary>
    /// The half-close sentence is the one where naming the wrong subject does real damage: told about
    /// "its sessions", the owner believes the whole orchestration may be half-closed when a single
    /// implementer failed to die.
    /// </summary>
    [Fact]
    public void AnUncertainMemberCloseSaysTheMemberMayBeRunning_NotTheSessions()
    {
        var text = CloseConfirmationPrompt_Builder.Describe_Decision("crm-2", MEMBER, CloseTapOutcomes.Uncertain);

        Assert.Contains("'imp-2' may still be running", text);
        Assert.DoesNotContain("its sessions may still be running", text);
    }

    /// <summary>
    /// An unreadable request is the ONE case where nobody can say what the tap was about, so the
    /// wording names NEITHER a member nor a verb.
    ///
    /// It used to fall back to the orchestration's close wording, and that was defensible while every
    /// parked request was a close. A PROMOTION request is one too, and it arrives on this same path:
    /// the file that could not be read is precisely the one whose kind is unknowable, so "Close
    /// 'crm-2'?" is a guess in front of a solo that had asked to be promoted — the same wrong-record
    /// failure the archive label and the button labels were both corrected for. Falling back to a
    /// GUESS is not the same as falling back to a default.
    /// </summary>
    [Fact]
    public void WithNoReadableRequestTheWordingGuessesNeitherAMemberNorAVerb()
    {
        var text = CloseConfirmationPrompt_Builder.Describe_Decision("crm-2", null, CloseTapOutcomes.NotAttempted);

        Assert.Contains("crm-2", text);
        Assert.DoesNotContain("member", text);
        Assert.DoesNotContain("clos", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("promot", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The tap did not take, and saying so is what keeps the FRESH prompt from arriving as a
    /// contradiction: the request is deliberately left parked, so the owner is asked again while a
    /// success message would still be on their screen.
    /// </summary>
    /// <summary>
    /// It says the tap did not take — which is what keeps the FRESH prompt from arriving as a
    /// contradiction — and it does NOT promise the re-ask unconditionally. "You will be asked again
    /// shortly" is true only while the file stays readable; a persistently unreadable one is archived
    /// and reported to the requester, and the owner is never asked and never told.
    /// </summary>
    [Fact]
    public void ACloseThatWasNeverAttemptedSaysNothingChanged_WithoutPromisingTheReAsk()
    {
        var text = Decide(CloseTapOutcomes.NotAttempted);

        // "NOT done", not "NOT closed": this outcome is produced only when the request could not be
        // read, so the verb is the one thing that cannot be known — see the neutrality test above.
        Assert.Contains("NOT done", text);
        Assert.Contains("nothing was changed", text);
        Assert.Contains("if it can be read", text);
        Assert.DoesNotContain("asked again shortly", text);
        Assert.DoesNotContain("✅", text);
    }

    /// <summary>
    /// THE CASE THE CHANGE EXISTS FOR. The executor threw partway, so the orchestration may be marked
    /// closed with its sessions alive, and nothing will re-offer it. This must claim NEITHER outcome —
    /// "we do not know" rendered as success is how the owner ends up believing live sessions are dead,
    /// and rendered as failure it invites them to re-close something already half-closed.
    /// </summary>
    /// <summary>
    /// ASSERTED WHOLE, not by fragments. The fragment version caught every SUBSTITUTION and none of the
    /// additions: appending " Everything was closed successfully." to the sentence contains no ✅, no
    /// "Closed — you confirmed" and no "NOT closed", keeps the four sentences distinct, and would have
    /// left the suite green while the sentence claimed success.
    ///
    /// It also has to be ACTIONABLE. It said "check the app", which sends the owner to a card that
    /// reads closed and dimmed, with the close button disabled and the one control that would reach a
    /// still-running session hidden. So it names what is unusual instead: recorded as closed, nothing
    /// will ask again, and the error is where errors land.
    /// </summary>
    [Fact]
    public void AnUncertainCloseClaimsNeitherSuccessNorFailure()
    {
        Assert.Equal(
            "⚠️ Close 'crm-2'?\n\n"
            + "⚠️ Close did not complete. It is recorded as closed, its sessions may still be running, "
            + "and you will NOT be asked again. The error is in the General topic.",
            Decide(CloseTapOutcomes.Uncertain));
    }

    /// <summary>
    /// FOUR OUTCOMES, FOUR SENTENCES. Any two outcomes sharing wording would put the owner back where
    /// they started — unable to tell from the message which of them happened.
    /// </summary>
    [Fact]
    public void EveryOutcomeReadsDifferently()
    {
        var texts = Enum.GetValues<CloseTapOutcomes>().Select(Decide).ToList();

        Assert.Equal(texts.Count, texts.Distinct().Count());
    }

    /// <summary>
    /// This replaces a case that asserted every decision contains the ORCHESTRATION id — which was
    /// true, and pinned the defect: a member decision naming only "crm-2" satisfied it, so fixing the
    /// wording would have required deleting a passing test. A test that cements a defect is worse than
    /// no test. What actually matters is that the owner can tell WHICH close this is about.
    /// </summary>
    [Fact]
    public void EveryDecisionIdentifiesWhatWasClosed()
    {
        foreach (var outcome in Enum.GetValues<CloseTapOutcomes>())
        {
            Assert.Contains("crm-2", CloseConfirmationPrompt_Builder.Describe_Decision("crm-2", ORCHESTRATION, outcome));
            Assert.Contains("imp-2", CloseConfirmationPrompt_Builder.Describe_Decision("crm-2", MEMBER, outcome));
        }
    }

    static readonly IParkedCloseRequest SIBLING = ParkedCloseRequest_Factory.Create_ForSibling(
        SpawnSiblingRequest_Factory.Create(
            "ai-orchestrator-7", "AI-Orch · limits rework", "Rework the usage-limit pause per window", 14,
            "C:/repos/AIOrchestrator-limits", "two jobs you want to steer separately", "request.json"),
        ParkedCloseRequest_Reader.SIBLING_REQUESTER_DESCRIPTION,
        "parked.json");

    /// <summary>
    /// THE SIBLING PROMPT IS SPEC 2026-09-23 §2.1 STEP 3, LINE FOR LINE: who asks — by the display name
    /// the engine passes, not the file's description — the new topic's name, the job, and why. It comes
    /// through <see cref="CloseConfirmationPrompt_Builder.Build"/>, the one route the engine uses
    /// (pre-flight ruling H), and it is real from Task 8 because the per-tick ask sweep reaches it with no
    /// tap (ruling A).
    /// </summary>
    [Fact]
    public void TheSiblingPrompt_NamesWhoAsks_TheNewTopic_TheJob_AndWhy()
    {
        Assert.Equal(
            "🔗 AI-Orch · settings work wants a sibling session for a parallel job\n"
            + "New topic: AI-Orch · limits rework\n"
            + "Job: Rework the usage-limit pause per window\n"
            + "Why: two jobs you want to steer separately",
            CloseConfirmationPrompt_Builder.Build(SIBLING, null, "AI-Orch · settings work"));
    }

    /// <summary>An unnamed requester is named by its id — never by the file's description of it.</summary>
    [Fact]
    public void TheSiblingPrompt_OfAnUnnamedRequester_UsesItsId()
    {
        Assert.StartsWith("🔗 ai-orchestrator-7 wants a sibling session", CloseConfirmationPrompt_Builder.Build(SIBLING, null));
    }

    [Fact]
    public void TheSiblingButtons_StartIt_OrKeepOneSession()
    {
        Assert.Equal(("✅ Start it", "✋ Keep one session"), CloseConfirmationPrompt_Builder.Build_ButtonLabels(ParkedCloseKinds.Sibling));
    }

    /// <summary>
    /// A SIBLING CLOSES NOTHING, and every sentence around its tap says so: the journal line, the
    /// declined and lapsed notices, the General line, the toast, and the edit that replaces the prompt.
    /// Each of these fell through to a close or neutral default before the kind existed — the family's
    /// history (a promotion announced as a close) is why each is asserted here rather than trusted.
    /// </summary>
    [Fact]
    public void EverySiblingSentence_NamesTheSibling_AndNeverSaysClose()
    {
        List<string> sentences =
        [
            CloseConfirmationPrompt_Builder.Describe_AskedFor(SIBLING),
            CloseConfirmationPrompt_Builder.Describe_AskedFor_ToGeneral(SIBLING, SIBLING.OrchId),
            .. Enum.GetValues<CloseTapOutcomes>().Select(outcome => CloseConfirmationPrompt_Builder.Describe_Decision(SIBLING.OrchId, SIBLING, outcome)),
        ];

        foreach (var sentence in sentences)
        {
            Assert.Contains("AI-Orch · limits rework", sentence);
            Assert.DoesNotContain("close", sentence, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("ai-orchestrator-7", CloseConfirmationPrompt_Builder.Describe_AskedFor_ToGeneral(SIBLING, SIBLING.OrchId));
        Assert.Equal("nothing was started", CloseConfirmationPrompt_Builder.Describe_NothingDone(ParkedCloseKinds.Sibling));
        Assert.Equal("starting…", CloseConfirmationPrompt_Builder.Build_TapToast(ParkedCloseKinds.Sibling, confirms: true));
        Assert.Equal("kept as one session", CloseConfirmationPrompt_Builder.Build_TapToast(ParkedCloseKinds.Sibling, confirms: false));
    }

    /// <summary>
    /// Pre-flight ruling E's wording: the header asks the question the prompt asked, a confirm says
    /// started, a decline says one session stays — and a failure claims neither.
    /// </summary>
    [Fact]
    public void TheSiblingDecision_SaysStarted_KeptAsOne_OrNeither()
    {
        string Decide_Sibling(CloseTapOutcomes outcome) => CloseConfirmationPrompt_Builder.Describe_Decision(SIBLING.OrchId, SIBLING, outcome, CloseTapOutcome_Decider.SIBLING_STARTED);

        Assert.StartsWith("🔗 Start sibling 'AI-Orch · limits rework'?", Decide_Sibling(CloseTapOutcomes.Closed));
        Assert.Contains("✅ Started — you confirmed.", Decide_Sibling(CloseTapOutcomes.Closed));
        Assert.Contains("✋ Kept as one session", Decide_Sibling(CloseTapOutcomes.Declined));

        var uncertain = Decide_Sibling(CloseTapOutcomes.Uncertain);
        Assert.DoesNotContain("✅", uncertain);
        Assert.Contains("General topic", uncertain);
    }

    /// <summary>
    /// RULING E, THE HALF THE OUTCOME CANNOT CARRY: a sibling refused at the tap completes cleanly —
    /// outcome Closed — having started nothing. Only the "started" label may render "✅"; any other label,
    /// or none, says NOT started and names what happened instead.
    /// </summary>
    [Theory]
    [InlineData("worktree-shared")]
    [InlineData("unexecuted")]
    [InlineData(null)]
    public void AConfirmedSiblingThatDidNotStart_NeverReadsStarted(string? archiveLabel)
    {
        var text = CloseConfirmationPrompt_Builder.Describe_Decision(SIBLING.OrchId, SIBLING, CloseTapOutcomes.Closed, archiveLabel);

        Assert.DoesNotContain("✅", text);
        Assert.Contains("Not started", text);
        Assert.StartsWith("🔗 Start sibling 'AI-Orch · limits rework'?", text);
        Assert.DoesNotContain("close", text, StringComparison.OrdinalIgnoreCase);

        if (archiveLabel != null)
            Assert.Contains(archiveLabel, text);
    }

    /// <summary>§7.3: a yes tapped during a usage-limit pause is HELD — the prompt says when it starts, and claims nothing yet.</summary>
    [Fact]
    public void TheSiblingHeldForAPause_SaysWhenItStarts_AndClaimsNothing()
    {
        var text = CloseConfirmationPrompt_Builder.Describe_SiblingHeldForPause(SIBLING);

        Assert.StartsWith("🔗 Start sibling 'AI-Orch · limits rework'?", text);
        Assert.Contains("usage-limit pause", text);
        Assert.Contains("asked again", text);
        Assert.DoesNotContain("✅", text);
        Assert.DoesNotContain("close", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("usage-limit pause", CloseConfirmationPrompt_Builder.SIBLING_HELD_FOR_PAUSE_TOAST);
    }

    static string Decide(CloseTapOutcomes outcome)
    {
        return CloseConfirmationPrompt_Builder.Describe_Decision("crm-2", ORCHESTRATION, outcome);
    }
}
