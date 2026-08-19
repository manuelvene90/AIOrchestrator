using System.Text.RegularExpressions;
using AIOrchestratorCoreLib.Planning.PlanProgress;

namespace AIOrchestratorCoreLib.Planning;

/// <summary>
/// Parses a PLAN.md task ledger (maintained by the orchestration's supervisor) into progress
/// counts. Line convention, one task per line:
///   - [ ] open   - [>] in progress   - [x] done   - [!] blocked   - [?] blocked on the owner
///   - [-] not doing
/// Anything that is not a task line (headers, notes) is ignored. Returns null when the text has
/// no task lines at all — the card then simply shows no progress bar.
///
/// "Not doing" is the marker that makes 100% reachable. Before it, only "done" removed weight from
/// the denominator, so a line that was superseded or parked stayed counted as unfinished forever and
/// every orchestration ended below 100% — the owner's complaint was that "no session has ever
/// finished at 100%, as if the denominator were larger than it should be". It is additive: no
/// existing ledger contains one, so every file parses exactly as it did.
///
/// NOT EVERY TASK LINE IN THE FILE IS A LEDGER LINE. The sections named by
/// <see cref="PlanLedger_Sections"/> — PARKED and OWNER REQUESTS — are skipped, so a discovery
/// nobody asked for cannot move the owner's bar by being written down. Read that class for why the
/// boundary is enforced here rather than left to the role commands.
/// </summary>
public static partial class PlanLedger_Parser
{
    [GeneratedRegex(@"^\s*-\s*\[(x|X| |>|!|\?|-)\]\s*(.*)$", RegexOptions.Compiled)]
    private static partial Regex TaskLine_Regex();

    public static IPlanProgress? Parse_OrNull(string planText)
    {
        if (string.IsNullOrWhiteSpace(planText))
            return null;

        var notDoing = 0;

        List<string> doneTasks = [];
        List<string> inProgressTasks = [];
        List<string> blockedTasks = [];

        // A SUBSET of blockedTasks, counted separately — `[?]` lines are in both.
        var blockedOnOwner = 0;
        List<string> openTasks = [];

        // THE LEDGER AS WRITTEN, alongside the buckets — /progress prints one line per ledger line in
        // FILE ORDER, and the buckets cannot answer that: they are five lists, and interleaving them
        // back into a document's order is not possible once the order has been thrown away.
        List<PlanLedgerLine> lines = [];

        // WHICH SECTION WE ARE IN, because a task line under PARKED is not owed work. It is a plain
        // flag rather than a heading stack: the boundary is "this section or not", and every heading
        // level ends the previous section for that question.
        var inNonLedgerSection = false;

        foreach (var rawLine in planText.Split('\n'))
        {
            if (PlanLedger_Sections.Is_Heading(rawLine))
                inNonLedgerSection = PlanLedger_Sections.Opens_NonLedgerSection(rawLine);

            var match = TaskLine_Regex().Match(rawLine.TrimEnd('\r'));

            if (!match.Success || inNonLedgerSection)
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
                case "?":
                {
                    // BLOCKED, and blocked specifically ON THE OWNER. It joins blockedTasks as well
                    // as its own count: it is blocked by every measure that already existed, and a
                    // reader asking "how many lines cannot move" must not get a smaller number
                    // because the supervisor was more specific about why.
                    blockedTasks.Add(taskText);
                    blockedOnOwner++;
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

            // AFTER the switch, so only a marker the switch recognises is ever recorded as a line —
            // the unhandled case throws rather than reaching this.
            //
            // NORMALISED ONCE, HERE: `[X]` and `[x]` are one state written by two hands, and the
            // switch above already folds them. Printing the raw capture would put both spellings in
            // front of the owner inside a single message.
            lines.Add(new PlanLedgerLine(marker == "X" ? "x" : marker, taskText));
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
            doneTasks,
            lines,
            blockedOnOwner);
    }
}
