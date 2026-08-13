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

    /// <summary>
    /// THE LEDGER, one line per line, each carrying the marker the supervisor wrote — in the file's
    /// own order, with nothing omitted, capped at nothing.
    ///
    /// TWO OWNER DIRECTIVES BUILT THIS, and the second overrules a rule that had stood since the
    /// first. Reading them in order matters, because the older comment here argued the opposite and a
    /// future reader will otherwise re-derive it:
    ///
    /// 2026-08-12 — "the progress command is way too detailed": it printed 593 of 683 lines, done
    /// ones included. The answer then was to collapse each KIND to a count once it grew past a
    /// handful, and never to print a done line at all.
    ///
    /// 2026-08-13 — that answer was itself the complaint. It rendered as PROSE, joining every task of
    /// a kind onto one line with ` · `, so four tasks became roughly fifteen wrapped visual lines on
    /// a phone: "I want it to be a list of the main macro tasks... let's say 7/8 max, and it should
    /// be in the [-], [>], [X] format." Then, correcting an 8-line cap read into that: "The done rows
    /// must not be hidden. I want to see all the rows, it must not be truncated. If all the tasks
    /// don't fit in 8/9 rows it means you haven't managed to group the tasks sufficiently into
    /// macrotasks."
    ///
    /// So 7/8 IS NOT A RENDERING LIMIT. It is how many macro tasks a ledger should have, and meeting
    /// it belongs to whoever writes PLAN.md. A truncating renderer would hide that author's failure
    /// to group — which is exactly what the owner does not want to be hidden from. THE WHY OF THE
    /// OLDER RULE SURVIVES INTACT: a ledger of hundreds is unreadable on a phone. The answer is a
    /// shorter LEDGER, not a shorter message.
    ///
    /// The ledger's own line structure was always one line per deliverable. Nothing here decides
    /// anything; it stopped flattening it on the way out.
    /// </summary>
    public static string Describe_Remaining(IPlanProgress progress)
    {
        if (progress.Lines.Count == 0)
            return "the ledger is empty";

        return string.Join('\n', progress.Lines.Select(line => $"[{line.Marker}] {line.Text}"));
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
