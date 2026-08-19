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
        Assert.Contains("1 task blocked", PlanProgress_Formatter.Describe_Counts(progress));
    }

    /// <summary>Additive: every ledger written before this marker existed parses exactly as it did.</summary>
    [Fact]
    public void ALedgerWithoutTheMarker_IsUnchanged()
    {
        var progress = Parse("- [x] a", "- [>] b", "- [ ] c", "- [!] d");

        Assert.NotNull(progress);
        Assert.Equal(4, progress.Total);
        Assert.Equal(0, progress.NotDoing);
        Assert.Equal("1/4 done (25%) · 1 running · 1 task blocked", PlanProgress_Formatter.Describe_Counts(progress));
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

    /// <summary>
    /// The owner's own ledger, in numbers: 53 running, 2 blocked, 36 open — 91 lines, 91 printed.
    ///
    /// This docstring used to end "detail is a function of COUNT, not of category", which was the
    /// rule that collapsed the 53 to a number and named the 2. See the comment in the body for what
    /// replaced it and what survived of it.
    /// </summary>
    [Fact]
    public void ALedgerOfNinetyOneLinesPrintsNinetyOneLines()
    {
        List<string> lines = [];

        for (var index = 0; index < 53; index++)
            lines.Add($"- [>] running {index}");

        lines.Add("- [!] migrate the state file — blocked on: your call about the 125% scaling");
        lines.Add("- [!] the second blocker — blocked on: a decision");

        for (var index = 0; index < 36; index++)
            lines.Add($"- [ ] open {index}");

        var ledger = PlanProgress_Formatter.Describe_Ledger(Parse([.. lines])!);

        // REVERSED BY THE OWNER, 2026-08-13. This case used to assert the opposite of every line
        // below: the 53 collapsed to "in progress   53", the open ones showed three names and
        // "+33 more", and the whole thing was three lines. That was the answer to "too detailed",
        // and the owner has since ruled that hiding rows hides the ledger author's failure to group
        // them: "I want to see all the rows, it must not be truncated."
        //
        // The complaint the old shape answered is still real and still theirs — a 91-line ledger IS
        // unreadable on a phone. It is now answered by writing a 7-8 line ledger, upstream of here,
        // which is the supervisor's job. This function's job is to not lie about what the ledger says.
        Assert.Equal(91, ledger.Split('\n').Length);

        Assert.Contains("[>] running 0", ledger);
        Assert.Contains("[>] running 52", ledger);
        Assert.Contains("[!] migrate the state file — blocked on: your call about the 125% scaling", ledger);
        Assert.Contains("[ ] open 35", ledger);
        Assert.DoesNotContain("more", ledger);
    }

    /// <summary>
    /// A DONE line IS printed, since the owner's directive of 2026-08-13: "the done rows must not be
    /// hidden."
    ///
    /// This test used to assert the exact opposite, and its docstring explained that the rule was
    /// STRUCTURAL — the parser discarded the words of a done line, so the formatter could not have
    /// printed one however it was written. That stopped being true when /tasks needed the full ledger
    /// and `DoneTasks` was collected; the guarantee had already quietly become a choice by the time
    /// the owner reversed it. Worth recording, because a rule described as impossible is one nobody
    /// re-checks.
    /// </summary>
    [Fact]
    public void EveryStateIsPrinted_DoneIncluded()
    {
        var ledger = PlanProgress_Formatter.Describe_Ledger(Parse(
            "- [x] the finished thing",
            "- [>] the running one",
            "- [ ] the open one",
            "- [-] the dropped one")!);

        Assert.Equal(
            "[x] the finished thing\n[>] the running one\n[ ] the open one\n[-] the dropped one",
            ledger);
    }

    /// <summary>
    /// A ledger whose every line is done or dropped now PRINTS those lines instead of saying there is
    /// nothing left — they are what there is to see, and the count line above already says 100%.
    /// The "nothing left" wording only ever fitted a renderer that showed unfinished work alone.
    /// </summary>
    [Fact]
    public void AFinishedLedgerStillShowsItsRows()
    {
        Assert.Equal("[x] a\n[-] b", PlanProgress_Formatter.Describe_Ledger(Parse("- [x] a", "- [-] b")!));
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
