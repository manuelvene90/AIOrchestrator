using System.Text.RegularExpressions;
using AIOrchestratorCoreLib.Planning.PlanProgress;

namespace AIOrchestratorCoreLib.Planning;

/// <summary>
/// Parses a PLAN.md task ledger (maintained by the orchestration's supervisor) into progress
/// counts. Line convention, one task per line:
///   - [ ] open      - [>] in progress      - [x] done      - [!] blocked      - [-] not doing
/// Anything that is not a task line (headers, notes) is ignored. Returns null when the text has
/// no task lines at all — the card then simply shows no progress bar.
///
/// "Not doing" is the marker that makes 100% reachable. Before it, only "done" removed weight from
/// the denominator, so a line that was superseded or parked stayed counted as unfinished forever and
/// every orchestration ended below 100% — the owner's complaint was that "no session has ever
/// finished at 100%, as if the denominator were larger than it should be". It is additive: no
/// existing ledger contains one, so every file parses exactly as it did.
/// </summary>
public static partial class PlanLedger_Parser
{
    [GeneratedRegex(@"^\s*-\s*\[(x|X| |>|!|-)\]\s*(.*)$", RegexOptions.Compiled)]
    private static partial Regex TaskLine_Regex();

    public static IPlanProgress? Parse_OrNull(string planText)
    {
        if (string.IsNullOrWhiteSpace(planText))
            return null;

        var notDoing = 0;

        List<string> doneTasks = [];
        List<string> inProgressTasks = [];
        List<string> blockedTasks = [];
        List<string> openTasks = [];

        foreach (var rawLine in planText.Split('\n'))
        {
            var match = TaskLine_Regex().Match(rawLine.TrimEnd('\r'));

            if (!match.Success)
                continue;

            var marker = match.Groups[1].Value;
            var taskText = match.Groups[2].Value.Trim();

            switch (marker)
            {
                case "x":
                case "X":
                {
                    doneTasks.Add(taskText);
                    break;
                }
                case ">":
                {
                    inProgressTasks.Add(taskText);
                    break;
                }
                case "!":
                {
                    blockedTasks.Add(taskText);
                    break;
                }
                case " ":
                {
                    openTasks.Add(taskText);
                    break;
                }
                case "-":
                {
                    // Deliberately NOT in the total: it is neither owed nor delivered.
                    notDoing++;
                    break;
                }
                default:
                {
                    throw new Exception($"Unhandled task marker '{marker}' — the regex and this switch disagree");
                }
            }
        }

        var total = doneTasks.Count + inProgressTasks.Count + blockedTasks.Count + openTasks.Count;

        // A ledger of nothing but dropped lines still has nothing to report progress ON.
        if (total == 0 && notDoing == 0)
            return null;

        return PlanProgress_Factory.Create(
            doneTasks.Count,
            inProgressTasks.Count,
            blockedTasks.Count,
            notDoing,
            total,
            inProgressTasks.FirstOrDefault() ?? openTasks.FirstOrDefault(),
            inProgressTasks,
            blockedTasks,
            openTasks,
            doneTasks);
    }
}
