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

    /// <summary>
    /// How many of <see cref="Blocked"/> are waiting on the OWNER — the `[?]` marker, a SUBSET of
    /// Blocked rather than a sibling of it, so every existing reader of Blocked stays correct.
    ///
    /// It exists because `[!]` was carrying two different things. `arb portfolio UX` showed a blocked
    /// line that was waiting on a REVIEWER while the owner read the count as something waiting on
    /// them, asked what had got stuck, and nothing had (2026-08-19). Only the supervisor knows which
    /// kind a block is, so the owner's ruling was that the supervisor SAYS which — rather than the
    /// app inferring it from the line's wording.
    /// </summary>
    int BlockedOnOwner { get; }

    IReadOnlyList<string> OpenTasks { get; }

    /// <summary>
    /// Delivered, in ledger order. Collected only because /tasks renders the full ledger.
    /// </summary>
    IReadOnlyList<string> DoneTasks { get; }

    /// <summary>
    /// EVERY task line, in the order the file wrote them, each carrying its own marker — the ledger
    /// as the supervisor typed it rather than as five buckets.
    ///
    /// The buckets above answer "how many, of which kind"; this answers "what does the ledger say",
    /// which is a different question and the one /progress asks since 2026-08-13. Grouping by kind on
    /// the way out reorders somebody's document, and the owner asked to see their rows.
    /// </summary>
    IReadOnlyList<PlanLedgerLine> Lines { get; }
}
