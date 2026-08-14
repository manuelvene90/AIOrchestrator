using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanProgress;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning;

/// <summary>
/// The precomputed ledger reading the supervisor's terminal status line renders. The status line is
/// PowerShell in another process, so everything it needs has to be decided HERE — the point of the
/// file is that there is one renderer, not two that agree today.
/// </summary>
public class ProgressArtefactBuilderTests
{
    [Fact]
    public void Build_Json_CarriesTheRenderedSentence_NotJustTheNumbers()
    {
        // The status line prints this verbatim. If it ever had to assemble the sentence itself, the
        // wording would be duplicated in a shell script and would drift the first time either moved.
        Assert.Equal(
            PlanProgress_Formatter.Describe_Counts(Build(done: 57, total: 76, inProgress: 2, blocked: 1)),
            Read(Build(done: 57, total: 76, inProgress: 2, blocked: 1), "text"));
    }

    [Fact]
    public void Build_Json_CarriesTheRawNumbersForACompactSpelling()
    {
        var progress = Build(done: 57, total: 76, notDoing: 3);

        Assert.Equal("57", Read(progress, "done"));
        Assert.Equal("76", Read(progress, "total"));
        Assert.Equal("3", Read(progress, "notDoing"));
    }

    /// <summary>
    /// TRUNCATED, NEVER ROUNDED, and this is the case that has to hold: 75 of 76 is 98.68%, which
    /// rounds to 99 and floors to 98 — but the danger is the far end. A status line doing its own
    /// `done * 100 / total` in PowerShell rounds, and PowerShell rounds a whole-number division of
    /// 75/76 * 100 to 99 rather than truncating. The percentage must come from here or the terminal
    /// eventually claims a finished orchestration that is not finished.
    /// </summary>
    [Fact]
    public void Build_Json_TakesThePercentageFromTheOneArithmetic()
    {
        Assert.Equal("98", Read(Build(done: 75, total: 76), "percent"));
        Assert.Equal("100", Read(Build(done: 76, total: 76), "percent"));
        Assert.Equal(
            PlanProgress_Formatter.Percent(Build(done: 1, total: 3)).ToString(),
            Read(Build(done: 1, total: 3), "percent"));
    }

    /// <summary>
    /// The dropped lines leave the denominator, so 57/57 is 100% with three of them parked. The
    /// status line must not have to know that rule — it arrives already applied, in both the
    /// sentence and the numbers.
    /// </summary>
    [Fact]
    public void Build_Json_AppliesTheNotDoingRuleBeforeTheStatusLineSeesIt()
    {
        var progress = Build(done: 57, total: 57, notDoing: 3);

        Assert.Equal("100", Read(progress, "percent"));
        Assert.Equal("57", Read(progress, "total"));
        Assert.Contains("3 not doing", Read(progress, "text"));
    }

    [Fact]
    public void Build_Json_IsParseableJson()
    {
        // The renderer's own contract: anything that does not parse makes it fall back to the plain
        // line, so a build that emitted invalid JSON would fail silently and forever.
        Assert.NotNull(JsonNode.Parse(ProgressArtefact_Builder.Build_Json(Build(done: 1, total: 2))));
    }

    static string Read(IPlanProgress progress, string property)
    {
        var root = JsonNode.Parse(ProgressArtefact_Builder.Build_Json(progress));

        Assert.NotNull(root);

        return root![property]!.ToString();
    }

    static IPlanProgress Build(int done, int total, int inProgress = 0, int blocked = 0, int notDoing = 0)
    {
        var open = total - done - inProgress - blocked;

        return PlanProgress_Factory.Create(
            done,
            inProgress,
            blocked,
            notDoing,
            total,
            null,
            Names(inProgress, "running"),
            Names(blocked, "blocked"),
            Names(open, "open"),
            Names(done, "done"));
    }

    static IReadOnlyList<string> Names(int count, string label)
    {
        return count <= 0 ? [] : Enumerable.Range(1, count).Select(index => $"{label} {index}").ToList();
    }
}
