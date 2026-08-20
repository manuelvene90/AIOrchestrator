using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanProgress;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning;

/// <summary>
/// FOLLOWING A SESSION WITHOUT ASKING IT (owner, 2026-08-20): *"It's quite difficult to follow the
/// sup's or solo's work. I think that I should receive a very short and fast message every time it
/// completes a member of the progress ledger and starts a new one."*
///
/// A transition happens ONCE, so it is told once. There is no timer here and no cadence — only a
/// comparison between two readings — which is what keeps a "tell me more" feature from becoming the
/// waterfall this repo spent a day removing.
/// </summary>
public class LedgerTransitionTests
{
    [Fact]
    public void AFinishedLineAndTheNextOneStartingAreBothReported()
    {
        var before = Parse("- [>] build stage B", "- [ ] build stage C");
        var after = Parse("- [x] build stage B", "- [>] build stage C");

        var moved = LedgerTransition_Detector.Compare(before, after);

        Assert.Equal(["build stage B"], moved.Finished);
        Assert.Equal(["build stage C"], moved.Started);
        Assert.Equal("✔ build stage B\n▶ build stage C", LedgerTransition_Wording.Describe(moved));
    }

    /// <summary>Nothing moved, nothing said — the state this sits in almost every tick.</summary>
    [Fact]
    public void AnUnchangedLedgerSaysNothing()
    {
        var ledger = Parse("- [>] build stage B", "- [ ] build stage C");

        Assert.False(LedgerTransition_Detector.Compare(ledger, ledger).IsWorthTelling);
    }

    /// <summary>
    /// A LINE THAT DID NOT EXIST BEFORE IS NOT A MOVEMENT. New lines appearing is the supervisor
    /// writing its plan, and the owner asked to hear about work moving, not about work being typed.
    /// Without this, a session drafting eight lines would fire eight messages.
    /// </summary>
    [Fact]
    public void NewlyWrittenLinesAreNotAnnounced()
    {
        var before = Parse("- [x] build stage B");
        var after = Parse("- [x] build stage B", "- [>] a brand new plan line", "- [ ] another");

        Assert.False(LedgerTransition_Detector.Compare(before, after).IsWorthTelling);
    }

    /// <summary>
    /// SUB-TASKS STAY QUIET. A stage with eleven pieces under it would otherwise be eleven messages —
    /// the waterfall by another name, at the one altitude /tasks already covers.
    /// </summary>
    [Fact]
    public void SubTasksDoNotRaiseAMessage()
    {
        var before = Parse("- [>] build stage B", "  - [>] B5a the refusal outcome");
        var after = Parse("- [>] build stage B", "  - [x] B5a the refusal outcome");

        Assert.False(LedgerTransition_Detector.Compare(before, after).IsWorthTelling);
    }

    /// <summary>
    /// Matched on the line's TEXT, never its position: ledgers are hand-written and a line inserted
    /// above another would otherwise report everything below it as having moved.
    /// </summary>
    [Fact]
    public void AnInsertionAboveDoesNotMoveTheLinesBelowIt()
    {
        var before = Parse("- [>] build stage B");
        var after = Parse("- [ ] inserted first", "- [>] build stage B");

        Assert.False(LedgerTransition_Detector.Compare(before, after).IsWorthTelling);
    }

    /// <summary>A missing reading is not a transition — first sight must announce nothing.</summary>
    [Fact]
    public void NoPreviousReadingMeansNothingToReport()
    {
        Assert.False(LedgerTransition_Detector.Compare(null, Parse("- [x] done")).IsWorthTelling);
    }

    [Fact]
    public void TheEndOfAnEndeavourIsRecognisedAndRecapped()
    {
        var finished = Parse("- [x] explore the tab controls", "- [x] drag-to-reorder", "- [-] dropped, superseded")!;

        Assert.True(LedgerTransition_Detector.Is_EndOfEndeavour(finished));

        var recap = LedgerTransition_Wording.Describe_Recap("Tear-off tabs", finished);

        Assert.Contains("Tear-off tabs — everything asked for is done. 2/2 · 1 not doing.", recap);
        Assert.Contains("✔ explore the tab controls", recap);
        Assert.Contains("✔ drag-to-reorder", recap);
    }

    /// <summary>Work still open is not an ending, whatever the percentage says.</summary>
    [Fact]
    public void AnOpenLineMeansItIsNotOver()
    {
        Assert.False(LedgerTransition_Detector.Is_EndOfEndeavour(Parse("- [x] done", "- [ ] not yet")));
        Assert.False(LedgerTransition_Detector.Is_EndOfEndeavour(Parse("- [x] done", "- [?] waiting on you")));
        Assert.False(LedgerTransition_Detector.Is_EndOfEndeavour(null));
    }

    static IPlanProgress? Parse(params string[] lines)
    {
        return PlanLedger_Parser.Parse_OrNull(string.Join("\n", lines));
    }
}
