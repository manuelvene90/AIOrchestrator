namespace AIOrchestratorCoreLib.Status;

/// <summary>
/// WHICH ROWS the owner's status message carries — the counts header, the supervisor's row when
/// there IS a supervisor, then one row per member.
///
/// It lives out here rather than in <c>BridgeEngineModel</c> for this file's usual reason: that class
/// is <c>internal sealed</c> with no <c>InternalsVisibleTo</c>, so a decision made inside it cannot be
/// exercised by the suite, and the decision below is one a later reader would be very likely to
/// "simplify" back. The engine keeps the reading of usage files and channels; this keeps the shape.
///
/// THE DECISION: a BASIC orchestration has no supervisor, so it gets no supervisor row. That row was
/// unconditional, and the probe behind it reads the ORCHESTRATION FOLDER's usage file — which a basic
/// orchestration never writes — so the status told the owner *"- supervisor: idle — waiting"* directly
/// above *"- solo-1: working now"*: a role that does not exist in that mode, reported as idle, next to
/// the session actually doing the work. Owner, 2026-08-14: *"the app didn't realize this is a 'solo'
/// session … It should be done in a way that it knows it's solo, otherwise I get confused."*
/// </summary>
public static class StatusRoster_Builder
{
    /// <param name="supervisorLine">
    /// What the supervisor's row would say. It is still computed by the caller when the orchestration
    /// is basic and simply dropped here — cheap (one usage-file read) and it keeps the CHOICE in one
    /// place rather than splitting it across a caller that decides whether to compute and a builder
    /// that decides whether to print.
    /// </param>
    /// <param name="isBasic">
    /// From <see cref="Sessions.OrchestrationShape.Is_BasicOrchestration"/>, never from a member-id
    /// scan — that class documents why the roster answers wrongly for a PROMOTED orchestration.
    /// </param>
    public static string Build(string countsLine, bool isBasic, string supervisorLine, IReadOnlyList<string> memberLines)
    {
        List<string> lines = [countsLine];

        if (!isBasic)
            lines.Add($"- supervisor: {supervisorLine}");

        lines.AddRange(memberLines);

        return string.Join('\n', lines);
    }
}
