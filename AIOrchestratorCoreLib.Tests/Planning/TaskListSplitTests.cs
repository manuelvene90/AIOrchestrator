using AIOrchestratorCoreLib.Planning;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning;

/// <summary>
/// /progress and /tasks are one parse, two renderings — and since 2026-08-13 they carry the SAME
/// rows, in the SAME order. **Only the vocabulary separates them**: /progress prints the ledger's own
/// markers (`[x] row`), /tasks the indented ones it has always used (`  x row`).
///
/// ORDER WAS IN THAT SENTENCE UNTIL rev-4 CAUGHT IT, one commit after rev-3 caught the sentence
/// before it. Both renderings read `progress.Lines` in the same direction (`PlanProgress_Formatter`
/// :64 and :100), so order has separated nothing since `Describe_EveryLine` stopped walking the five
/// buckets — and the test named `TheFullFormShowsDroppedRowsAndKeepsTheLedgersOrder`, forty lines
/// below, says so in its own name.
///
/// THE FIRST VERSION SAID SOMETHING ELSE FALSE AND rev-3 CAUGHT THAT, and the file disproved it twice
/// forty lines below. It claimed /progress answers "what is LEFT" while /tasks shows everything, and
/// that the short form exists because 683 lines do not fit a phone — the capping model the owner
/// overruled ("I want to see all the rows, it must not be truncated"). A maintainer opening this file
/// to learn what the split guarantees reads the top first, so a stale summary here re-teaches the
/// rule the branch removed.
///
/// It is the third instance on this branch of prose outliving the code beneath it, and it was left
/// standing four lines above a commit that existed to close a false claim in the same file. Nothing
/// checks a comment.
///
/// The test that matters is still that the two can never be the same string. If they collapse into
/// one shape the split has silently undone itself, and nothing else in the system would notice — the
/// commands would both still answer, both still be correct-looking, and one of the two questions
/// would simply stop being answerable.
/// </summary>
public class TaskListSplitTests
{
    [Fact]
    public void TheTwoRenderingsAreNeverTheSameString()
    {
        var progress = Parse(
            "- [x] the delivered thing",
            "- [>] the running one",
            "- [!] the blocked one — blocked on: a decision",
            "- [ ] the open one")!;

        Assert.NotEqual(
            PlanProgress_Formatter.Describe_Ledger(progress),
            PlanProgress_Formatter.Describe_EveryLine(progress));
    }

    /// <summary>
    /// BOTH FORMS SHOW DONE LINES NOW — the owner's directive of 2026-08-13 — so what separates them
    /// is no longer WHICH rows they carry. This case used to assert that only /tasks showed a done
    /// line, and it is kept, inverted, because the two commands answering the same question in
    /// different words is now the live risk rather than a hypothetical one.
    ///
    /// What still separates them is the VOCABULARY, and only that: /progress prints the ledger's own
    /// markers, /tasks its indented ones. **This sentence used to say "ORDER and VOCABULARY" and that
    /// /tasks "groups by state"** — untrue from the moment `Describe_EveryLine` started reading
    /// `Lines` instead of walking the five buckets, which happened in the very commit that wrote it.
    /// The assertions directly below print both renderings in identical row order and disprove it.
    /// </summary>
    [Fact]
    public void BothFormsShowDoneLines_AndStillReadDifferently()
    {
        var progress = Parse("- [x] the delivered thing", "- [ ] the open one")!;

        Assert.Contains("the delivered thing", PlanProgress_Formatter.Describe_Ledger(progress));
        Assert.Contains("the delivered thing", PlanProgress_Formatter.Describe_EveryLine(progress));

        Assert.Equal("[x] the delivered thing\n[ ] the open one", PlanProgress_Formatter.Describe_Ledger(progress));
        Assert.Equal("  x the delivered thing\n  · the open one", PlanProgress_Formatter.Describe_EveryLine(progress));
    }

    /// <summary>
    /// /tasks USED TO DROP EVERY DROPPED ROW, while its own comment called it "THE WHOLE LEDGER, line
    /// by line". It looped four of the five buckets — there was no fifth to loop, because the parser
    /// only counted `[-]` lines — so the command that exists to show everything was the one omitting
    /// rows silently. That is not a missing feature; it is a comment that was not true.
    ///
    /// It also picks up the ledger's own ORDER from the same fix, since it now reads the same one
    /// list /progress does rather than re-deriving a sequence from five buckets. Two commands, two
    /// vocabularies, ONE reading — which is one reading fewer than before.
    /// </summary>
    [Fact]
    public void TheFullFormShowsDroppedRowsAndKeepsTheLedgersOrder()
    {
        var progress = Parse(
            "- [x] the delivered thing",
            "- [-] the superseded thing",
            "- [ ] the open one",
            "- [!] the blocked one — blocked on: a decision")!;

        Assert.Equal(
            string.Join('\n', new[]
            {
                "  x the delivered thing",
                "  - the superseded thing",
                "  · the open one",
                "  ! the blocked one — blocked on: a decision",
            }),
            PlanProgress_Formatter.Describe_EveryLine(progress));
    }

    /// <summary>
    /// NEITHER FORM TRUNCATES ANY MORE. The short one used to, and that was its whole point: "it
    /// truncates where the long one does not". The owner removed the cap — "I want to see all the
    /// rows, it must not be truncated" — so the 40th open line is in both.
    ///
    /// Kept rather than deleted because the property it guards is now the opposite one and still
    /// needs a guard: a renderer that quietly reintroduces a limit reddens here.
    /// </summary>
    [Fact]
    public void NeitherFormTruncates()
    {
        List<string> lines = [];

        for (var index = 0; index < 40; index++)
            lines.Add($"- [ ] open {index}");

        var progress = Parse([.. lines])!;

        Assert.Contains("open 39", PlanProgress_Formatter.Describe_Ledger(progress));
        Assert.Contains("open 39", PlanProgress_Formatter.Describe_EveryLine(progress));

        // THE COUNT ON BOTH, which this case claimed and only half did. `Contains("open 39")` is
        // satisfied by a TAIL cap — `Lines.TakeLast(8)` keeps row 39 and drops thirty-two others —
        // so the full form was guarded by an assertion a truncating renderer passes. The other two
        // fixtures in this file are 4 and 2 rows, small enough for an 8-row cap to be invisible
        // there too, so nothing anywhere caught it.
        //
        // A guard whose docstring claims coverage it does not have is worse than no guard: the
        // maintainer auditing it ticks it off.
        Assert.Equal(40, PlanProgress_Formatter.Describe_Ledger(progress).Split('\n').Length);
        Assert.Equal(40, PlanProgress_Formatter.Describe_EveryLine(progress).Split('\n').Length);
    }

    static AIOrchestratorCoreLib.Planning.PlanProgress.IPlanProgress? Parse(params string[] lines)
    {
        return PlanLedger_Parser.Parse_OrNull(string.Join('\n', lines));
    }
}
