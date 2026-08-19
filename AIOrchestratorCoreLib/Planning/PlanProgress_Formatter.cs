using AIOrchestratorCoreLib.Planning.PlanProgress;

namespace AIOrchestratorCoreLib.Planning;

/// <summary>
/// The one wording for "how far along is this" — "57/76 done (75%) · 2 running · 1 task blocked".
/// Shared by /progress, /status and the periodic push so the three can never quote different
/// figures for the same ledger.
/// </summary>
public static class PlanProgress_Formatter
{
    public static string Describe_Counts(IPlanProgress progress)
    {
        var blockedPart = Describe_Blocked(progress);
        var runningPart = progress.InProgress > 0 ? $" · {progress.InProgress} running" : "";

        // The dropped count travels WITH the percentage, always. A marker that removes weight from
        // the denominator is a delete key unless it is visible: 100% could otherwise be reached by
        // dropping the remainder, and a falsely high number is worse than the falsely low one this
        // fixes — the owner distrusts the low one, which is the safe direction to be wrong in.
        var notDoingPart = progress.NotDoing > 0 ? $" · {progress.NotDoing} not doing" : "";

        return $"{progress.Done}/{progress.Total} done{Describe_Percent(progress)}{runningPart}{blockedPart}{notDoingPart}";
    }

    /// <summary>
    /// The blocked segment, and the ONE place the owner is told a block is theirs.
    ///
    /// "task blocked", never a bare "BLOCKED" (owner's call, 2026-08-19): the member roster printed
    /// directly UNDER this count says BLOCKED ON OWNER for a SESSION, so one word carried two
    /// meanings in a single message and the owner read a blocked ledger LINE as a stuck session.
    ///
    /// PASSIVE BY CONSTRUCTION, which was the owner's hard condition: *"I don't want it to continue
    /// spontaneously prompting or to have another annoying loop pop up."* This adds no send path, no
    /// trigger and no cadence — it is more words on a status they already receive, so there is
    /// nothing here that CAN fire twice. The morning this was asked for was spent removing a loop.
    ///
    /// Severity is arithmetic, never a list count: `Total - Done - Blocked` is what can still move.
    /// Reading `OpenTasks.Count` instead would answer 0 for any caller that passes the counts without
    /// the lists — which three test builders do, and which would have made "nothing else can move"
    /// the answer for a perfectly healthy ledger.
    /// </summary>
    static string Describe_Blocked(IPlanProgress progress)
    {
        if (progress.Blocked == 0)
            return "";

        var segment = $" · {progress.Blocked} task{(progress.Blocked == 1 ? "" : "s")} blocked";

        if (progress.BlockedOnOwner == 0)
            return segment;

        var severity = progress.Total - progress.Done - progress.Blocked > 0
            ? "the rest continues"
            : "nothing else can move";

        // Collapsed when every blocked line is theirs, because "1 task blocked · 1 needs you" says
        // the same number twice on a phone screen.
        return progress.BlockedOnOwner == progress.Blocked
            ? $"{segment}, needs you — {severity}"
            : $"{segment} · {progress.BlockedOnOwner} needs you — {severity}";
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
    ///
    /// NAMED Describe_Ledger, not Describe_Remaining. The old name was accurate for a renderer that
    /// showed unfinished work alone, and survived one directive past the point where it described
    /// the output — a stale name outlives the reader who knows it is stale.
    /// </summary>
    public static string Describe_Ledger(IPlanProgress progress)
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
    ///
    /// AND IT WAS NOT THE WHOLE LEDGER UNTIL 2026-08-13, which is the point worth keeping. It looped
    /// four of the five states and dropped every `[-]` row, because there was no fifth bucket to loop
    /// — the parser counted those lines without keeping their words. So the command whose entire
    /// purpose is to omit nothing was omitting rows, silently, under a comment that said otherwise.
    ///
    /// That is the same failure as `NoDoneLineIsEverPrinted`, which documented its rule as
    /// STRUCTURALLY unbreakable — true when written, false once /tasks needed `DoneTasks` — and so
    /// went unexamined for exactly as long as it read like a guarantee. A rule or a contract stated as
    /// impossible is one nobody re-reads, and both of these were wrong in the comment long before
    /// anybody noticed in the behaviour.
    ///
    /// Reading `Lines` fixes both at once: every state is present, in the file's order, and the
    /// second derivation of a sequence from five buckets is GONE rather than extended by a fifth loop.
    /// One reading of the ledger, two vocabularies — /progress prints the ledger's own markers, this
    /// prints the indented ones it has always used.
    /// </summary>
    public static string Describe_EveryLine(IPlanProgress progress)
    {
        if (progress.Lines.Count == 0)
            return "the ledger is empty";

        return string.Join('\n', progress.Lines.Select(line => $"  {Describe_FullFormPrefix(line.Marker)} {line.Text}"));
    }

    /// <summary>
    /// This view's own prefix for a ledger marker. It is NOT the `[x]` vocabulary /progress uses, and
    /// keeping them different is deliberate: the two commands answer different questions off one
    /// parse, and two renderings that converge to the same string have silently become one command.
    ///
    /// OPEN is the only marker that changes: a space cannot be a prefix. Every other marker is its
    /// own, INCLUDING one this method has never heard of — an unknown marker shows as itself rather
    /// than falling through to `·`, which would render it as an open task and misreport the ledger to
    /// hide a mapping gap. The parser can only emit five, so that branch is unreachable today; it is
    /// written this way so that adding a sixth cannot quietly mean "open" here.
    /// </summary>
    static string Describe_FullFormPrefix(string marker)
    {
        return marker == " " ? "·" : marker;
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
