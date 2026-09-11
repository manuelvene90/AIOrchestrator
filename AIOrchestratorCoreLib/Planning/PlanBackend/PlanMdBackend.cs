namespace AIOrchestratorCoreLib.Planning.PlanBackend;

/// <summary>
/// THE DEFAULT, AND IT DOES NOTHING. PLAN.md is the whole plan: nothing upstream hands work in,
/// nothing downstream is told when a line closes, and the file on disk is the only truth there is.
///
/// <para>
/// A NULL OBJECT RATHER THAN A NULL. Every call site can then be written once, with no "if a backend
/// is configured" branch to get wrong — and the branch is the thing worth removing, because the
/// no-backend case is every installation that has not opted in, which is the one that must not
/// change. <see cref="List_ApprovedRequests"/> returning empty is what makes the synchronisation step
/// a no-op end to end: no requests means no writes to PLAN.md, and nothing tracked means nothing to
/// report closed.
/// </para>
/// <para>
/// NO INTERFACE/MODEL/FACTORY TRIPLE, deliberately. The triple exists so a collaborator can be
/// swapped and faked; here the INTERFACE is the seam and this class is its empty case — it holds no
/// state, takes no dependency, and has nothing a factory could decide.
/// </para>
/// </summary>
public sealed class PlanMdBackend : IPlanBackend
{
    public IReadOnlyList<ApprovedPlanRequest> List_ApprovedRequests(string orchId)
    {
        return [];
    }

    public void Acknowledge_Request(string orchId, string requestId, string ledgerRowRef)
    {
    }

    public void Report_RowClosed(string orchId, string requestId, string ledgerRowRef, PlanRowEvidence evidence)
    {
    }

    public void Report_OrchestrationClosed(string orchId, string summary)
    {
    }
}
