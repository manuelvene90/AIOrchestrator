namespace AIOrchestratorCoreLib.Planning.PlanProgress;

/// <summary>
/// One task line of a PLAN.md ledger: the marker it carried and the words after it.
///
/// The marker is stored NORMALISED — `- [X] foo` and `- [x] foo` both arrive here as `x`. A ledger is
/// written by hand by whichever agent holds the pen, so the same state reaches the parser spelled
/// more than one way; normalising at the parse means every surface that prints a marker prints the
/// same vocabulary without each of them having to remember to fold the case.
/// </summary>
/// <param name="IsSubTask">
/// Whether the line was INDENTED under another. The ledgers already carry this hierarchy — a stage
/// at column 0 with its pieces indented beneath it — and the parser threw the indentation away, so
/// every surface printed one flat list of everything. The owner asked for the two altitudes back
/// (2026-08-19): /progress and /left as "a bird's eye view", /tasks going "into individual small
/// sub-tasks".
///
/// Optional so the existing constructions keep compiling and keep meaning what they meant: a line
/// nobody indented is a top-level line.
/// </param>
public readonly record struct PlanLedgerLine(string Marker, string Text, bool IsSubTask = false);
