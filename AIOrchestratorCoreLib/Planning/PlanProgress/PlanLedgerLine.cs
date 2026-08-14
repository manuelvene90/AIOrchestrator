namespace AIOrchestratorCoreLib.Planning.PlanProgress;

/// <summary>
/// One task line of a PLAN.md ledger: the marker it carried and the words after it.
///
/// The marker is stored NORMALISED — `- [X] foo` and `- [x] foo` both arrive here as `x`. A ledger is
/// written by hand by whichever agent holds the pen, so the same state reaches the parser spelled
/// more than one way; normalising at the parse means every surface that prints a marker prints the
/// same vocabulary without each of them having to remember to fold the case.
/// </summary>
public readonly record struct PlanLedgerLine(string Marker, string Text);
