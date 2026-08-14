using AIOrchestratorCoreLib.GeneralSupervision.ParkedCloseRequest;
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
    /// </summary>
    [Fact]
    public void TheSubjectDescriptionNamesTheMemberButNotForAnOrchestration()
    {
        Assert.Equal("'imp-2'", CloseConfirmationPrompt_Builder.Describe_Subject(MEMBER));
        Assert.Equal("this orchestration", CloseConfirmationPrompt_Builder.Describe_Subject(ORCHESTRATION));
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
    /// wording falls back to the orchestration rather than guessing at a member.
    /// </summary>
    [Fact]
    public void WithNoReadableRequestTheWordingFallsBackToTheOrchestration()
    {
        var text = CloseConfirmationPrompt_Builder.Describe_Decision("crm-2", null, CloseTapOutcomes.NotAttempted);

        Assert.Contains("⚠️ Close 'crm-2'?", text);
        Assert.DoesNotContain("member", text);
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

        Assert.Contains("NOT closed", text);
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

    static string Decide(CloseTapOutcomes outcome)
    {
        return CloseConfirmationPrompt_Builder.Describe_Decision("crm-2", ORCHESTRATION, outcome);
    }
}
