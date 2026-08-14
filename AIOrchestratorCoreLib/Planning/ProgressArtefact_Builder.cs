using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Planning.PlanProgress;

namespace AIOrchestratorCoreLib.Planning;

/// <summary>
/// The ledger reading, precomputed for a renderer that must not parse PLAN.md itself — today the
/// supervisor's Claude Code status line, which is PowerShell and lives outside this process.
///
/// IT CARRIES THE RENDERED STRING, not just the numbers, and that is the whole point of the file.
/// Sharing numbers keeps two renderers in step only for as long as somebody remembers to change both;
/// sharing the finished text means there is only ONE renderer, so the terminal cannot disagree with
/// the owner's phone even if the arithmetic changes later. The `[-] not doing` rule comes along for
/// free rather than being re-implemented in a shell script and kept in step by hand.
///
/// The numbers are here too, for a narrow terminal that wants a compact spelling — but as a FALLBACK,
/// never as the source of the sentence. In particular <see cref="PlanProgress_Formatter.Percent"/> is
/// TRUNCATED rather than rounded, deliberately, so that 75 of 76 tasks cannot read as 100%: a status
/// line computing `done * 100 / total` in PowerShell would round the other way and quietly claim a
/// finished orchestration.
/// </summary>
public static class ProgressArtefact_Builder
{
    public static string Build_Json(IPlanProgress progress)
    {
        var root = new JsonObject
        {
            ["text"] = PlanProgress_Formatter.Describe_Counts(progress),
            ["done"] = progress.Done,
            ["total"] = progress.Total,
            ["percent"] = PlanProgress_Formatter.Percent(progress),
            ["notDoing"] = progress.NotDoing,
        };

        return root.ToJsonString(JsonWriting.INDENTED);
    }
}
