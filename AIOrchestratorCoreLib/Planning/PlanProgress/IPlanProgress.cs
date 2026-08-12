namespace AIOrchestratorCoreLib.Planning.PlanProgress;

/// <summary>
/// Parsed state of an orchestration's PLAN.md task ledger — the structured answer to "how far
/// along is this long task?" that freeform channel prose cannot give.
/// </summary>
public interface IPlanProgress
{
    int Done { get; }
    int InProgress { get; }
    int Blocked { get; }

    /// <summary>
    /// Lines decided AGAINST — superseded, or parked for good. They are excluded from
    /// <see cref="Total"/> entirely, which is what makes 100% reachable: before this existed, only
    /// "done" removed weight, so anything dropped stayed counted as unfinished forever and no
    /// orchestration ever ended at 100% (owner, 2026-08-12: "as if the denominator were larger than
    /// it should be").
    ///
    /// It is reported alongside the percentage everywhere, and that is not decoration. A marker that
    /// removes weight is a delete key unless it is visible: 100% could otherwise be reached by
    /// dropping the remainder, and a number that is falsely HIGH is worse than the falsely low one
    /// being fixed — the owner currently distrusts the low number, which is the safe direction to be
    /// wrong in.
    /// </summary>
    int NotDoing { get; }

    /// <summary>What is owed plus what was delivered. Excludes <see cref="NotDoing"/>.</summary>
    int Total { get; }

    /// <summary>The first in-progress task's text, else the first open one — "what's happening now".</summary>
    string? CurrentTaskText { get; }

    /// <summary>Still owed, in ledger order — the answer to "what's left".</summary>
    IReadOnlyList<string> InProgressTasks { get; }

    IReadOnlyList<string> BlockedTasks { get; }

    IReadOnlyList<string> OpenTasks { get; }

    /// <summary>
    /// Delivered, in ledger order. Collected only because /tasks renders the full ledger — the
    /// short /progress must never print one, and until this existed that was guaranteed by the
    /// parser discarding the words rather than by anything choosing not to show them.
    /// </summary>
    IReadOnlyList<string> DoneTasks { get; }
}
