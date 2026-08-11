using AIOrchestratorCoreLib.Planning;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning;

/// <summary>
/// The owner's complaint, verbatim: "What matters is that the percentage reaches 100% when work is
/// done... no session has ever finished at 100%, as if the denominator were larger than it should
/// be."
///
/// They were right and the cause was structural: only "done" removed weight, so a line that was
/// superseded, parked or decided against stayed counted as unfinished forever. `- [-]` leaves the
/// denominator entirely, which makes 100% reachable and makes it MEAN "everything still on the list
/// is done".
///
/// The safeguard is as important as the marker: a dropped count travels with the percentage
/// everywhere, because a marker that removes weight is a delete key unless it is visible. 100% could
/// otherwise be reached by dropping the remainder — and a falsely HIGH number is worse than the
/// falsely low one being fixed, since the owner distrusts the low one, which is the safe direction
/// to be wrong in.
/// </summary>
public class LedgerNotDoingTests
{
    /// <summary>THE acceptance case: everything left is either delivered or decided against.</summary>
    [Fact]
    public void DroppedLinesLeaveTheDenominator_SoOneHundredPercentIsReachable()
    {
        var progress = Parse(
            "- [x] wire the tap handler",
            "- [x] anchor the ledger exemption",
            "- [-] rewrite the mirror loop — superseded by the tap guard");

        Assert.NotNull(progress);
        Assert.Equal(2, progress.Total);
        Assert.Equal(2, progress.Done);
        Assert.Equal(1, progress.NotDoing);
        Assert.Equal("2/2 done (100%) · 1 not doing", PlanProgress_Formatter.Describe_Counts(progress));
    }

    /// <summary>The safeguard. Drop the remainder and the count says so, in the same breath as 100%.</summary>
    [Fact]
    public void ADroppedCountIsAlwaysShownBesideThePercentage()
    {
        var progress = Parse("- [x] one real deliverable", "- [-] a", "- [-] b", "- [-] c");

        Assert.NotNull(progress);
        Assert.Contains("3 not doing", PlanProgress_Formatter.Describe_Counts(progress));
    }

    /// <summary>Blocked keeps its weight — it is a real state and someone is still waiting on it.</summary>
    [Fact]
    public void BlockedStillCounts()
    {
        var progress = Parse("- [x] done", "- [!] migrate the state file — blocked on: owner decision");

        Assert.NotNull(progress);
        Assert.Equal(2, progress.Total);
        Assert.Equal(1, progress.Blocked);
        Assert.Contains("1 BLOCKED", PlanProgress_Formatter.Describe_Counts(progress));
    }

    /// <summary>Additive: every ledger written before this marker existed parses exactly as it did.</summary>
    [Fact]
    public void ALedgerWithoutTheMarker_IsUnchanged()
    {
        var progress = Parse("- [x] a", "- [>] b", "- [ ] c", "- [!] d");

        Assert.NotNull(progress);
        Assert.Equal(4, progress.Total);
        Assert.Equal(0, progress.NotDoing);
        Assert.Equal("1/4 done (25%) · 1 running · 1 BLOCKED", PlanProgress_Formatter.Describe_Counts(progress));
    }

    /// <summary>
    /// The owner's first real ledger: 178/207 with 11 blocked at the end of a session. Deciding
    /// against the 18 that will never be done is what lets it land where it actually is.
    /// </summary>
    [Fact]
    public void TheOwnersEndOfSessionLedger_CanReachOneHundredPercent()
    {
        List<string> lines = [];

        for (var index = 0; index < 178; index++)
            lines.Add($"- [x] delivered {index}");

        for (var index = 0; index < 11; index++)
            lines.Add($"- [-] blocked and abandoned {index} — not doing this");

        for (var index = 0; index < 18; index++)
            lines.Add($"- [-] superseded {index}");

        var progress = Parse([.. lines]);

        Assert.NotNull(progress);
        Assert.Equal("178/178 done (100%) · 29 not doing", PlanProgress_Formatter.Describe_Counts(progress));
    }

    /// <summary>What is LEFT, and it has to fit a phone: 207 lines must not become 207 lines of text.</summary>
    [Fact]
    public void TheRemainingBlockIsCapped_AndSaysHowManyItHid()
    {
        List<string> lines = ["- [>] the running one", "- [!] the blocked one — blocked on: a decision"];

        for (var index = 0; index < 12; index++)
            lines.Add($"- [ ] open {index}");

        var remaining = PlanProgress_Formatter.Describe_Remaining(Parse([.. lines])!);

        Assert.Contains("> the running one", remaining);
        Assert.Contains("! the blocked one — blocked on: a decision", remaining);
        Assert.Contains("+7 more open", remaining);
        Assert.Equal(PlanProgress_Formatter.OPEN_TASKS_SHOWN + 3, remaining.Split('\n').Length);
    }

    [Fact]
    public void NothingLeft_SaysSo()
    {
        Assert.Equal("nothing left — every line is done or dropped", PlanProgress_Formatter.Describe_Remaining(Parse("- [x] a", "- [-] b")!));
    }

    /// <summary>The close names what it is ending mid-flight — it does not veto it.</summary>
    [Fact]
    public void UnresolvedLinesAreNamedForTheCloseConfirmation()
    {
        var unresolved = PlanProgress_Formatter.Describe_UnresolvedAtClose_OrNull(
            Parse("- [x] a", "- [>] b", "- [!] c", "- [ ] d", "- [ ] e"));

        Assert.Equal("4 line(s) neither done nor dropped — 1 running, 1 blocked, 2 open", unresolved);
    }

    /// <summary>A ledger that is finished or deliberately closed out says nothing at close time.</summary>
    [Fact]
    public void AResolvedLedgerAddsNothingToTheClosePrompt()
    {
        Assert.Null(PlanProgress_Formatter.Describe_UnresolvedAtClose_OrNull(Parse("- [x] a", "- [-] b")));
    }

    static AIOrchestratorCoreLib.Planning.PlanProgress.IPlanProgress? Parse(params string[] lines)
    {
        return PlanLedger_Parser.Parse_OrNull(string.Join('\n', lines));
    }
}
