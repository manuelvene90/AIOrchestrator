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
            PlanProgress_Formatter.Describe_Remaining(progress),
            PlanProgress_Formatter.Describe_EveryLine(progress));
    }

    /// <summary>The whole point of the long form: it shows what the short one refuses to.</summary>
    [Fact]
    public void OnlyTheFullFormShowsDoneLines()
    {
        var progress = Parse("- [x] the delivered thing", "- [ ] the open one")!;

        Assert.DoesNotContain("the delivered thing", PlanProgress_Formatter.Describe_Remaining(progress));
        Assert.Contains("the delivered thing", PlanProgress_Formatter.Describe_EveryLine(progress));
    }

    /// <summary>
    /// And the whole point of the short one: it truncates where the long one does not. Asserted on a
    /// ledger big enough that the difference cannot be an accident of ordering.
    /// </summary>
    [Fact]
    public void OnlyTheShortFormTruncates()
    {
        List<string> lines = [];

        for (var index = 0; index < 40; index++)
            lines.Add($"- [ ] open {index}");

        var progress = Parse([.. lines])!;

        Assert.DoesNotContain("open 39", PlanProgress_Formatter.Describe_Remaining(progress));
        Assert.Contains("open 39", PlanProgress_Formatter.Describe_EveryLine(progress));
    }

    static AIOrchestratorCoreLib.Planning.PlanProgress.IPlanProgress? Parse(params string[] lines)
    {
        return PlanLedger_Parser.Parse_OrNull(string.Join('\n', lines));
    }
}
