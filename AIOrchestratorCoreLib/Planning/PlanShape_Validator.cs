using System.Text.RegularExpressions;

namespace AIOrchestratorCoreLib.Planning;

/// <summary>
/// Catches ledger lines that CANNOT represent progress, at the moment they appear. A line like
/// "- [>] Remaining tasks 3-9 (gear, settings window, KB, PARTNERS #92)" collapses seven tasks
/// into one entry: from then on six real commits render as zero movement, however diligently the
/// ledger is updated. No amount of care fixes an unrepresentable line — only detection does.
/// </summary>
public static partial class PlanShape_Validator
{
    /// <summary>"tasks 3-9", "task 3 – 9", "items 4-11" — a range is many tasks wearing one checkbox.</summary>
    [GeneratedRegex(@"\b(tasks?|items?|steps?|points?)\s*\d+\s*[-–—]\s*\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex TaskRange_Regex();

    [GeneratedRegex(@"^\s*-\s*\[(x|X| |>|!)\]\s*(.*)$", RegexOptions.Compiled)]
    private static partial Regex TaskLine_Regex();

    /// <summary>Beyond this many separators one line is plainly carrying a list of deliverables.</summary>
    const int MAX_SEPARATORS_PER_TASK = 3;

    /// <summary>Returns one complaint per offending line, empty when the ledger's shape is sound.</summary>
    public static IReadOnlyList<string> Find_UnrepresentableLines(string planText)
    {
        List<string> complaints = [];

        if (string.IsNullOrWhiteSpace(planText))
            return complaints;

        foreach (var rawLine in planText.Split('\n'))
        {
            var match = TaskLine_Regex().Match(rawLine.TrimEnd('\r'));

            if (!match.Success)
                continue;

            var taskText = match.Groups[2].Value.Trim();

            if (taskText.Length == 0)
                continue;

            if (TaskRange_Regex().IsMatch(taskText))
            {
                complaints.Add($"'{Shorten(taskText)}' covers a RANGE of tasks in one line — split it, one line per task, or its progress can never be shown.");
                continue;
            }

            var separators = taskText.Count(character => character == ',' || character == ';');

            if (separators >= MAX_SEPARATORS_PER_TASK)
                complaints.Add($"'{Shorten(taskText)}' lists {separators + 1} deliverables in one line — split it, one line per task.");
        }

        return complaints;
    }

    static string Shorten(string text)
    {
        const int MAX_QUOTED_CHARS = 60;

        return text.Length <= MAX_QUOTED_CHARS ? text : $"{text[..MAX_QUOTED_CHARS]}…";
    }
}
