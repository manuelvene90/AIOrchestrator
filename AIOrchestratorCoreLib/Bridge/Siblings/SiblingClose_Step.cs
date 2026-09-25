using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Storage;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Bridge.Siblings;

/// <summary>
/// WHAT THE SURVIVING SIBLINGS ARE TOLD WHEN ONE CLOSES (spec 2026-09-23 §2.3, §7.5): one <c>[agent]</c>
/// entry each, naming the closed sibling's unfinished lines — the engine's close post-step, computed here so
/// <c>Execute_Close</c> gets a call and an append, not logic (code-conventions' move-out rule).
///
/// <para>
/// IT RETURNS WORDS AND APPENDS NOTHING, the <see cref="SiblingBirth_Step"/> precedent:
/// <c>Append_OrchestrationAppEntry</c> raises engine activity, so the append stays the engine's, and this
/// stays testable without one.
/// </para>
/// <para>
/// WHY A SURVIVOR NEEDS IT AT ALL. Membership is derived (§3.1), so the next tick drops the closed sibling
/// from every <c>.siblings</c> by itself — but a file that stops naming a sibling says nothing about the work
/// it left. A survivor that was waiting on an ANSWER from it would wait for ever; one whose job borders it
/// needs to know which lines nobody owns any more. So the notice names them, read through the one ledger
/// parse (<see cref="PlanLedger_Parser.Parse_OrNull"/>) and the one "what is left" filter
/// (<see cref="PlanProgress_Formatter.Describe_Unfinished"/>, what <c>/left</c> prints) — decision 12.
/// </para>
/// <para>
/// NO OWNER AUDIENCE. The owner just tapped the close, so a push about it is decision 15's noise; the
/// engine appends each notice as <c>Agent</c>.
/// </para>
/// </summary>
public static class SiblingClose_Step
{
    /// <summary>
    /// One notice per OPEN sibling of <paramref name="closed"/> in <paramref name="sessions"/>; none when
    /// <paramref name="closed"/> was never linked, and none when it was the last open member (§7.5: "nothing
    /// extra happens"). <paramref name="closed"/> is the snapshot taken before the close — its endeavour id is
    /// what is needed, and the store's copy is no different in that field.
    /// </summary>
    public static IReadOnlyList<(string OrchId, string Subject, string Body)> Build_SurvivorNotices(
        ISupervisionPaths paths,
        IReadOnlyList<IOrchestrationSession> sessions,
        IOrchestrationSession closed)
    {
        var survivors = EndeavourMembers_Resolver.Resolve_OpenSiblings(sessions, closed);

        if (survivors.Count == 0)
            return [];

        // Read ONCE, whatever the number of survivors: every one of them is told the same lines.
        var notice = SiblingNotice_Wording.Describe_SiblingClosed(closed.DisplayName ?? closed.OrchId, Describe_Unfinished(paths, closed.OrchId));

        return [.. survivors.Select(survivor => (survivor.OrchId, notice.Subject, notice.Body))];
    }

    /// <summary>
    /// <c>none</c> when there is no ledger to read — a missing PLAN.md is "no known unfinished line", and
    /// <see cref="PlanProgress_Formatter.Describe_Unfinished"/> only speaks about a ledger that parsed.
    /// </summary>
    static string Describe_Unfinished(ISupervisionPaths paths, string orchId)
    {
        var progress = PlanLedger_Parser.Parse_OrNull(Safe_FileReader.Read_AllText_OrEmpty(paths.Get_PlanFile(orchId)));

        return progress == null ? SiblingNotice_Wording.NO_UNFINISHED_LINES : PlanProgress_Formatter.Describe_Unfinished(progress);
    }
}
