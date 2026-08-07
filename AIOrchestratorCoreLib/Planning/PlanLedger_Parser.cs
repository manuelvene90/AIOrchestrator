using System.Text.RegularExpressions;
using AIOrchestratorCoreLib.Planning.PlanProgress;

namespace AIOrchestratorCoreLib.Planning;

/// <summary>
/// Parses a PLAN.md task ledger (maintained by the orchestration's supervisor) into progress
/// counts. Line convention, one task per line:
///   - [ ] open      - [>] in progress      - [x] done      - [!] blocked
/// Anything that is not a task line (headers, notes) is ignored. Returns null when the text has
/// no task lines at all — the card then simply shows no progress bar.
/// </summary>
public static partial class PlanLedger_Parser
{
    [GeneratedRegex(@"^\s*-\s*\[(x|X| |>|!)\]\s*(.*)$", RegexOptions.Compiled)]
    private static partial Regex TaskLine_Regex();

    public static IPlanProgress? Parse_OrNull(string planText)
    {
        if (string.IsNullOrWhiteSpace(planText))
            return null;

        var done = 0;
        var inProgress = 0;
        var blocked = 0;
        var open = 0;
        string? firstInProgressText = null;
        string? firstOpenText = null;

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
                    done++;
                    break;
                }
                case ">":
                {
                    inProgress++;
                    firstInProgressText ??= taskText;
                    break;
                }
                case "!":
                {
                    blocked++;
                    break;
                }
                case " ":
                {
                    open++;
                    firstOpenText ??= taskText;
                    break;
                }
                default:
                {
                    throw new Exception($"Unhandled task marker '{marker}' — the regex and this switch disagree");
                }
            }
        }

        var total = done + inProgress + blocked + open;

        if (total == 0)
            return null;

        return PlanProgress_Factory.Create(done, inProgress, blocked, total, firstInProgressText ?? firstOpenText);
    }
}
