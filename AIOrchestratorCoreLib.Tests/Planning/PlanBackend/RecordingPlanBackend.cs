using AIOrchestratorCoreLib.Planning.PlanBackend;

namespace AIOrchestratorCoreLib.Tests.Planning.PlanBackend;

/// <summary>
/// A SECOND BACKEND, WHICH IS THE ONLY WAY TO KNOW THE SEAM IS ONE. With just the default
/// implementation every test would be asserting that nothing happens — true of a working seam and of
/// a seam that is never consulted at all, which is exactly the pair the fixture-guard rule warns
/// about. This one records every call, so a test can pin what the app SAYS to a backend and not
/// merely that the app survives having one.
///
/// It also stands in for the adapters that live outside this repository: it is loaded the ordinary
/// way, through the interface, and knows nothing about PLAN.md.
/// </summary>
public sealed class RecordingPlanBackend : IPlanBackend
{
    public List<ApprovedPlanRequest> Approved { get; } = [];

    public List<(string OrchId, string RequestId, string LedgerRowRef)> Acknowledged { get; } = [];

    public List<(string OrchId, string RequestId, string LedgerRowRef, PlanRowEvidence Evidence)> Closed { get; } = [];

    public List<(string OrchId, string Summary)> OrchestrationsClosed { get; } = [];

    public int ListCalls { get; private set; }

    /// <summary>Set to make a call throw, so the retry-next-tick path can be pinned.</summary>
    public Exception? ThrowOnAcknowledge { get; set; }

    public Exception? ThrowOnReportClosed { get; set; }

    public IReadOnlyList<ApprovedPlanRequest> List_ApprovedRequests(string orchId)
    {
        ListCalls++;

        return Approved;
    }

    public void Acknowledge_Request(string orchId, string requestId, string ledgerRowRef)
    {
        if (ThrowOnAcknowledge != null)
            throw ThrowOnAcknowledge;

        Acknowledged.Add((orchId, requestId, ledgerRowRef));
    }

    public void Report_RowClosed(string orchId, string requestId, string ledgerRowRef, PlanRowEvidence evidence)
    {
        if (ThrowOnReportClosed != null)
            throw ThrowOnReportClosed;

        Closed.Add((orchId, requestId, ledgerRowRef, evidence));
    }

    public void Report_OrchestrationClosed(string orchId, string summary)
    {
        OrchestrationsClosed.Add((orchId, summary));
    }
}
