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
    /// The tap did not take, and saying so is what keeps the FRESH prompt from arriving as a
    /// contradiction: the request is deliberately left parked, so the owner is asked again while a
    /// success message would still be on their screen.
    /// </summary>
    [Fact]
    public void ACloseThatWasNeverAttemptedSaysNothingChanged()
    {
        var text = Decide(CloseTapOutcomes.NotAttempted);

        Assert.Contains("NOT closed", text);
        Assert.Contains("nothing was changed", text);
        Assert.Contains("asked again", text);
        Assert.DoesNotContain("✅", text);
    }

    /// <summary>
    /// THE CASE THE CHANGE EXISTS FOR. The executor threw partway, so the orchestration may be marked
    /// closed with its sessions alive, and nothing will re-offer it. This must claim NEITHER outcome —
    /// "we do not know" rendered as success is how the owner ends up believing live sessions are dead,
    /// and rendered as failure it invites them to re-close something already half-closed.
    /// </summary>
    [Fact]
    public void AnUncertainCloseClaimsNeitherSuccessNorFailure()
    {
        var text = Decide(CloseTapOutcomes.Uncertain);

        Assert.Contains("did not complete", text);
        Assert.Contains("may still be running", text);
        Assert.Contains("check the app", text);

        Assert.DoesNotContain("✅", text);
        Assert.DoesNotContain("Closed — you confirmed", text);
        Assert.DoesNotContain("NOT closed", text);
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

    [Fact]
    public void EveryDecisionNamesTheOrchestration()
    {
        foreach (var outcome in Enum.GetValues<CloseTapOutcomes>())
            Assert.Contains("crm-2", CloseConfirmationPrompt_Builder.Describe_Decision("crm-2", outcome));
    }

    static string Decide(CloseTapOutcomes outcome)
    {
        return CloseConfirmationPrompt_Builder.Describe_Decision("crm-2", outcome);
    }
}
