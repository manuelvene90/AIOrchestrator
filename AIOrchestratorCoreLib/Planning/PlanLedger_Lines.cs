using AIOrchestratorCoreLib.Planning.PlanProgress;

namespace AIOrchestratorCoreLib.Planning;

/// <summary>
/// WHICH LINE IS WHICH, once. A ledger line is identified by its TEXT and only the TOP-LEVEL ones
/// count as ledger lines for anything that reports movement.
///
/// <para>
/// Both halves were already written down twice, in prose, in two different files — and the second
/// copy was written by code that cited the first as the reason the two must agree
/// (<see cref="LedgerTransition_Detector"/>, and the closure check in
/// <see cref="PlanBackend.PlanBackend_Step"/>). Two readers of one file that decide "same line?"
/// separately is exactly how a marker list ends up with four entries in one place and six in another.
/// </para>
/// <para>
/// TEXT RATHER THAN POSITION, because a ledger is hand-written and lines get inserted above others
/// constantly; an index-based identity reports everything below an insertion as changed. TOP LEVEL
/// ONLY, because sub-tasks are a different altitude — announcing each one turns a stage with eleven
/// pieces into eleven messages.
/// </para>
/// </summary>
public static class PlanLedger_Lines
{
    public static IEnumerable<PlanLedgerLine> Top_Level(IPlanProgress progress)
    {
        return progress.Lines.Where(line => !line.IsSubTask);
    }

    /// <summary>The top-level line with exactly this text, or null. Ordinal — a ledger is not prose to be folded.</summary>
    public static PlanLedgerLine? Find_TopLevel_OrNull(IPlanProgress progress, string text)
    {
        foreach (var line in Top_Level(progress))
        {
            if (string.Equals(line.Text, text, StringComparison.Ordinal))
                return line;
        }

        return null;
    }

    public static bool Has_TopLevel(IPlanProgress progress, string text)
    {
        return Find_TopLevel_OrNull(progress, text) != null;
    }
}
