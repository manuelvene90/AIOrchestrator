using AIOrchestratorCoreLib.Planning;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning;

/// <summary>
/// /progress answers "what is LEFT" and /tasks answers "show me everything" — one parse, two
/// renderings. The owner asked for both: the short form because 683 lines do not fit a phone, and
/// the long one because they explicitly said to KEEP the detail rather than lose it.
///
/// The test that matters is that the two can never be the same string. If they collapse into one
/// shape the split has silently undone itself, and nothing else in the system would notice — the
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
    /// What still separates them is ORDER and VOCABULARY: /progress prints the ledger as written,
    /// with the ledger's own markers; /tasks groups by state with its own prefixes.
    /// </summary>
    [Fact]
    public void BothFormsShowDoneLines_AndStillReadDifferently()
    {
        var progress = Parse("- [x] the delivered thing", "- [ ] the open one")!;

        Assert.Contains("the delivered thing", PlanProgress_Formatter.Describe_Ledger(progress));
        Assert.Contains("the delivered thing", PlanProgress_Formatter.Describe_EveryLine(progress));

        Assert.Equal("[x] the delivered thing\n[ ] the open one", PlanProgress_Formatter.Describe_Ledger(progress));
        Assert.Equal("  · the open one\n  x the delivered thing", PlanProgress_Formatter.Describe_EveryLine(progress));
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
        Assert.Equal(40, PlanProgress_Formatter.Describe_Ledger(progress).Split('\n').Length);
    }

    static AIOrchestratorCoreLib.Planning.PlanProgress.IPlanProgress? Parse(params string[] lines)
    {
        return PlanLedger_Parser.Parse_OrNull(string.Join('\n', lines));
    }
}
