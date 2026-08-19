using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanProgress;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning;

/// <summary>
/// `- [>]` CLAIMS SOMEONE IS WORKING ON IT. When nothing is running, that claim is false.
///
/// The rule this enforces was already written in both role commands, and a session broke it anyway
/// on 2026-08-14 — the same session that had written the rule into solo.md hours earlier — by
/// holding two finished deliverables at `- [>]` because their branches were not yet on master. The
/// owner refused the apology and asked for the guarantee: *"it absolutely must be guaranteed that it
/// won't be messed up in the future by other sessions either."*
///
/// THE DETECTOR NEVER READS THE LINE'S TEXT. A "waiting on the merge" phrase list would catch that
/// evening's wording and miss the next one, while reading as though it covered them — the failure
/// mode this repo already paid for with AGENT_COACHING_SUBJECTS, which is why app entries route on a
/// tag now. The invariant is observable instead: nothing mid-turn for IDLE_MINUTES, therefore
/// nothing is in progress.
/// </summary>
public class AnInProgressLineNobodyIsWorkingOnTests
{
    const string PLAN = """
        # PLAN

        - [x] the merged one
        - [>] the solo's status line reads orange
        - [>] ledger enforcement reaches every session
        - [ ] not started
        """;

    static IPlanProgress? Parse() => PlanLedger_Parser.Parse_OrNull(PLAN);

    /// <summary>A ledger written inline, for the cases that need a shape the shared fixture does not have.</summary>
    static IPlanProgress? ParseLines(params string[] lines)
    {
        return PlanLedger_Parser.Parse_OrNull(string.Join("\n", lines));
    }

    /// <summary>The exact case the owner watched: two finished lines held open while nothing ran.</summary>
    [Fact]
    public void QuietForLongEnoughMakesEveryInProgressLineAFalseClaim()
    {
        var unworked = StaleInProgress_Detector.Find_UnworkedInProgressLines(
            Parse(),
            anySessionWorking: false,
            quietFor: TimeSpan.FromMinutes(StaleInProgress_Detector.IDLE_MINUTES));

        Assert.Equal(2, unworked.Count);
        Assert.Contains("the solo's status line reads orange", unworked);
        Assert.Contains("ledger enforcement reaches every session", unworked);

        // Not the other markers: `[ ]` has never claimed anyone is on it, and `[x]` is finished.
        Assert.DoesNotContain("not started", unworked);
        Assert.DoesNotContain("the merged one", unworked);
    }

    /// <summary>
    /// A SESSION MID-TURN MAKES THE CLAIM TRUE, and this is the half that stops the guard being a
    /// nag. Somebody IS working on it, which is exactly what `[>]` says.
    /// </summary>
    [Fact]
    public void NothingIsFlaggedWhileASessionIsWorking()
    {
        Assert.Empty(StaleInProgress_Detector.Find_UnworkedInProgressLines(
            Parse(),
            anySessionWorking: true,
            quietFor: TimeSpan.FromHours(3)));
    }

    /// <summary>
    /// AND THE QUIET HAS TO BE REAL. A session between turns is not idle in any meaningful sense; a
    /// detector that fired on those would be noise, and noise is what gets ignored — which is how the
    /// ledger reached this state in the first place.
    /// </summary>
    [Fact]
    public void ABriefPauseIsNotIdleness()
    {
        Assert.Empty(StaleInProgress_Detector.Find_UnworkedInProgressLines(
            Parse(),
            anySessionWorking: false,
            quietFor: TimeSpan.FromMinutes(StaleInProgress_Detector.IDLE_MINUTES - 1)));
    }

    /// <summary>
    /// UNMEASURED IS NOT IDLE. The clock starts on the first tick that finds the orchestration quiet,
    /// so before that there is no answer — and a null must not be read as "quiet for ever", which
    /// would flag every orchestration on the app's first tick after a restart.
    /// </summary>
    [Fact]
    public void AnUnmeasuredQuietFlagsNothing()
    {
        Assert.Empty(StaleInProgress_Detector.Find_UnworkedInProgressLines(
            Parse(),
            anySessionWorking: false,
            quietFor: null));
    }

    /// <summary>A ledger that does not parse cannot be judged, and says nothing rather than guessing.</summary>
    [Fact]
    public void NoLedgerFlagsNothing()
    {
        Assert.Empty(StaleInProgress_Detector.Find_UnworkedInProgressLines(
            null,
            anySessionWorking: false,
            quietFor: TimeSpan.FromHours(3)));
    }

    /// <summary>
    /// WHAT THE SESSION IS TOLD names all three honest answers, and refuses to pick one. The app
    /// cannot know whether the work is finished; a guard that guessed would trade a bar that
    /// under-reports for one that lies. It also states the merge rule outright, because that is the
    /// specific belief that produced the false claim.
    /// </summary>
    [Fact]
    public void TheMessageOffersTheThreeHonestAnswersAndPicksNone()
    {
        var text = StaleInProgress_Detector.Describe(["the solo's status line reads orange"]);

        Assert.Contains("- [x]", text);
        Assert.Contains("- [!]", text);
        Assert.Contains("- [-]", text);
        Assert.Contains("waiting for the owner to merge is NOT a reason", text);

        // The offending line is quoted back — a complaint that does not say WHICH line is a nag.
        Assert.Contains("the solo's status line reads orange", text);
    }

    /// <summary>
    /// THE GAP THAT MATTERED (owner, 2026-08-19). The idle rule above resets whenever ANY session is
    /// mid-turn, so an orchestration whose supervisor works all day is never checked — and those are
    /// the ones whose ledgers drift furthest. `arb portfolio UX` read 117/134 on finished work, with
    /// eleven `[>]` lines the idle rule had never once looked at.
    /// </summary>
    [Fact]
    public void ALineUnchangedForOverAnHourIsFlaggedHoweverBusyTheSessionsAre()
    {
        var progress = ParseLines("- [>] build stage B", "- [>] rev-8 REVIEW, 16 findings, all dispatched");

        var now = new DateTime(2026, 8, 19, 18, 0, 0, DateTimeKind.Utc);

        var firstSeen = new Dictionary<string, DateTime>
        {
            ["build stage B"] = now.AddMinutes(-59),
            ["rev-8 REVIEW, 16 findings, all dispatched"] = now.AddMinutes(-61),
        };

        var flagged = StaleInProgress_Detector.Find_UnmovedInProgressLines(progress, firstSeen, now);

        Assert.Equal(["rev-8 REVIEW, 16 findings, all dispatched"], flagged);
    }

    /// <summary>
    /// A LINE FIRST SEEN THIS TICK IS NOT STALE. Without this, every line would be flagged the moment
    /// the app started, which is the false alarm that teaches an agent to ignore the whole guard.
    /// </summary>
    [Fact]
    public void ALineNotSeenBeforeIsNotFlagged()
    {
        var progress = ParseLines("- [>] build stage B");

        Assert.Empty(StaleInProgress_Detector.Find_UnmovedInProgressLines(progress, new Dictionary<string, DateTime>(), DateTime.UtcNow));
    }

    [Fact]
    public void NoLedgerMeansNothingToFlag()
    {
        Assert.Empty(StaleInProgress_Detector.Find_UnmovedInProgressLines(null, new Dictionary<string, DateTime>(), DateTime.UtcNow));
    }

    /// <summary>
    /// The age message must NOT claim nothing is running — that is false in the case it exists for,
    /// and an agent reading a guard that misdescribes its own trigger learns to discount it. It also
    /// names `- [?]`, because "waiting on the owner" now has its own marker.
    /// </summary>
    [Fact]
    public void TheAgeMessageDoesNotClaimNothingIsRunning()
    {
        var text = StaleInProgress_Detector.Describe_Unmoved(["rev-8 REVIEW, 16 findings, all dispatched"]);

        Assert.DoesNotContain("Nothing has been mid-turn", text);
        Assert.Contains("Sessions being busy is not the question", text);
        Assert.Contains("- [?]", text);
        Assert.Contains("rev-8 REVIEW, 16 findings, all dispatched", text);
    }

}
