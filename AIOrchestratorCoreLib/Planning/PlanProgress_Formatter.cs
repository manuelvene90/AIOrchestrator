using AIOrchestratorCoreLib.Planning.PlanProgress;

namespace AIOrchestratorCoreLib.Planning;

/// <summary>
/// The one wording for "how far along is this" — "57/76 done (75%) · 2 running · 1 BLOCKED".
/// Shared by /progress, /status and the periodic push so the three can never quote different
/// figures for the same ledger.
/// </summary>
public static class PlanProgress_Formatter
{
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

    /// <summary>Open lines shown by name before the rest become a count.</summary>
    public const int NEXT_TASKS_SHOWN = 3;

    /// <summary>
    /// How many lines of one KIND are worth naming individually. Beyond this the kind becomes a
    /// number, because a list that long stops being a list and becomes a wall.
    /// </summary>
    public const int DETAIL_CAP = 6;

    /// <summary>
    /// What is LEFT, on a phone. The owner: "the progress command is way too detailed" — shown 593
    /// of 683 lines and then the finished ones, one after another.
    ///
    /// DETAIL IS A FUNCTION OF COUNT, NOT OF CATEGORY, and that is the correction. The previous
    /// version printed every running and blocked line in full "because they are few and they are the
    /// actionable ones" — an assumption written against a ledger with two of them. Their real ledger
    /// has 53 running, where naming each one is 53 lines of noise; four blocked, where each line is
    /// the thing they can act on. So a kind is named while it is small enough to read and collapses
    /// to a count when it is not, which is one rule rather than a special case per category.
    ///
    /// A DONE line is never printed under any circumstance. That was the whole complaint.
    /// </summary>
    public static string Describe_Remaining(IPlanProgress progress)
    {
        List<string> lines = [];

        Add_Kind(lines, "in progress", progress.InProgressTasks);
        Add_Kind(lines, "blocked", progress.BlockedTasks);

        if (progress.OpenTasks.Count > 0)
            lines.Add($"next          {Describe_Next(progress.OpenTasks)}");

        if (lines.Count == 0)
            return "nothing left — every line is done or dropped";

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Named while few, counted while many. The count is ALWAYS shown — it is the part the owner
    /// asked for — and the names follow only when there are few enough to read on a phone.
    /// </summary>
    static void Add_Kind(List<string> lines, string label, IReadOnlyList<string> tasks)
    {
        if (tasks.Count == 0)
            return;

        if (tasks.Count > DETAIL_CAP)
        {
            lines.Add($"{label,-13} {tasks.Count}");
            return;
        }

        lines.Add($"{label,-13} {tasks.Count} — {string.Join(" · ", tasks)}");
    }

    /// <summary>
    /// The next few by name, then a count. Their ledger had 33 open behind the three shown; naming
    /// all of them is the message they said they would not read.
    /// </summary>
    static string Describe_Next(IReadOnlyList<string> openTasks)
    {
        var shown = string.Join(" · ", openTasks.Take(NEXT_TASKS_SHOWN));
        var hidden = openTasks.Count - NEXT_TASKS_SHOWN;

        return hidden > 0 ? $"{shown} · +{hidden} more" : shown;
    }

    /// <summary>
    /// THE WHOLE LEDGER, line by line, done included — what /progress printed before it was made to
    /// fit a phone. The owner asked to KEEP this level of detail, not to lose it: "keep the current
    /// level of detail in a NEW command."
    ///
    /// Both shapes come off ONE parse. A second command reading the ledger its own way is how two
    /// answers to one question start disagreeing, which is the hazard this repo has paid for
    /// repeatedly — the renderings differ, the reading does not.
    ///
    /// Deliberately NOT capped. It exists because somebody asked for everything, and a "full" view
    /// that silently truncates is worse than either shape on its own.
    /// </summary>
    public static string Describe_EveryLine(IPlanProgress progress)
    {
        List<string> lines = [];

        foreach (var task in progress.InProgressTasks)
            lines.Add($"  > {task}");

        foreach (var task in progress.BlockedTasks)
            lines.Add($"  ! {task}");

        foreach (var task in progress.OpenTasks)
            lines.Add($"  · {task}");

        foreach (var task in progress.DoneTasks)
            lines.Add($"  x {task}");

        if (lines.Count == 0)
            return "the ledger is empty";

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
    ///
    /// PUBLIC because the Telegram status line shows the same figure in a different wording. Two
    /// surfaces quoting the same ledger must never disagree, and the way that is guaranteed is one
    /// arithmetic — not two that currently round the same way.
    /// </summary>
    public static int Percent(IPlanProgress progress)
    {
        if (progress.Total <= 0)
            return 0;

        return progress.Done * 100 / progress.Total;
    }

    static string Describe_Percent(IPlanProgress progress)
    {
        if (progress.Total <= 0)
            return "";

        return $" ({Percent(progress)}%)";
    }
}
