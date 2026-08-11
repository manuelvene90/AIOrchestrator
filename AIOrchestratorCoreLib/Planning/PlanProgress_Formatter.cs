using AIOrchestratorCoreLib.Planning.PlanProgress;

namespace AIOrchestratorCoreLib.Planning;

/// <summary>
/// The one wording for "how far along is this" — "57/76 done (75%) · 2 running · 1 BLOCKED".
/// Shared by /progress, /status and the periodic push so the three can never quote different
/// figures for the same ledger.
/// </summary>
public static class PlanProgress_Formatter
{
    /// <summary>Lines of "what is left" shown in full before the rest are summarised.</summary>
    public const int OPEN_TASKS_SHOWN = 5;

    public static string Describe_Counts(IPlanProgress progress)
    {
        var blockedPart = progress.Blocked > 0 ? $" · {progress.Blocked} BLOCKED" : "";
        var runningPart = progress.InProgress > 0 ? $" · {progress.InProgress} running" : "";

        // The dropped count travels WITH the percentage, always. A marker that removes weight from
        // the denominator is a delete key unless it is visible: 100% could otherwise be reached by
        // dropping the remainder, and a falsely high number is worse than the falsely low one this
        // fixes — the owner distrusts the low one, which is the safe direction to be wrong in.
        var notDoingPart = progress.NotDoing > 0 ? $" · {progress.NotDoing} not doing" : "";

        return $"{progress.Done}/{progress.Total} done{Describe_Percent(progress)}{runningPart}{blockedPart}{notDoingPart}";
    }

    /// <summary>
    /// What is LEFT, which is the question the owner actually asks ("a slash command that lets me
    /// know what's left to do would be useful"). Running and blocked lines are shown in full — they
    /// are few and they are the actionable ones — and open lines are capped, because their ledger
    /// ran to 207 tasks and a message that does not fit a phone is a message they will not read.
    /// </summary>
    public static string Describe_Remaining(IPlanProgress progress)
    {
        List<string> lines = [];

        foreach (var task in progress.InProgressTasks)
            lines.Add($"  > {task}");

        foreach (var task in progress.BlockedTasks)
            lines.Add($"  ! {task}");

        foreach (var task in progress.OpenTasks.Take(OPEN_TASKS_SHOWN))
            lines.Add($"  · {task}");

        var hidden = progress.OpenTasks.Count - OPEN_TASKS_SHOWN;

        if (hidden > 0)
            lines.Add($"  +{hidden} more open");

        if (lines.Count == 0)
            return "nothing left — every line is done or dropped";

        return string.Join('\n', lines);
    }

    /// <summary>
    /// The lines that are neither delivered nor decided against, for the close confirmation. The
    /// owner is told what they are ending mid-flight — it does not block the close, because a ledger
    /// that can refuse to let an orchestration end is the tail wagging the dog.
    /// </summary>
    public static string? Describe_UnresolvedAtClose_OrNull(IPlanProgress? progress)
    {
        if (progress == null)
            return null;

        var unresolved = progress.InProgress + progress.Blocked + progress.OpenTasks.Count;

        if (unresolved == 0)
            return null;

        List<string> parts = [];

        if (progress.InProgress > 0)
            parts.Add($"{progress.InProgress} running");

        if (progress.Blocked > 0)
            parts.Add($"{progress.Blocked} blocked");

        if (progress.OpenTasks.Count > 0)
            parts.Add($"{progress.OpenTasks.Count} open");

        return $"{unresolved} line(s) neither done nor dropped — {string.Join(", ", parts)}";
    }

    /// <summary>
    /// Truncated, never rounded: 75 of 76 tasks must not read as "100%". The only way to see 100%
    /// is for every task to be done, which is the one case where the number has to be trusted.
    /// </summary>
    static string Describe_Percent(IPlanProgress progress)
    {
        if (progress.Total <= 0)
            return "";

        return $" ({progress.Done * 100 / progress.Total}%)";
    }
}
