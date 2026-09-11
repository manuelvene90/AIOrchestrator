namespace AIOrchestratorCoreLib.Planning.PlanBackend;

/// <summary>
/// One upstream request this orchestration has taken on, and how far its round trip has got.
/// </summary>
/// <param name="RequestId">The upstream identifier — the key that makes ingestion happen once.</param>
/// <param name="LedgerRowRef">The ledger line's text, as written into PLAN.md.</param>
/// <param name="OwnerRequestNumber">Its row in the OWNER REQUESTS table, for a human following the trail.</param>
/// <param name="AcknowledgedUtc">When the backend was told the request became a ledger line. Null while that call is still owed — and it is RETRIED while it is null, which is the only thing that makes this field real.</param>
/// <param name="ClosedReportedUtc">When the backend was told the line reached [x]. Null while it is still owed.</param>
public sealed record TrackedPlanRequest(
    string RequestId,
    string LedgerRowRef,
    int OwnerRequestNumber,
    DateTime? AcknowledgedUtc,
    DateTime? ClosedReportedUtc);

/// <summary>
/// WHAT THIS ORCHESTRATION HAS ALREADY SAID UPSTREAM — the app's half of the idempotence contract.
///
/// <para>
/// It is persisted rather than remembered because the alternative fails in the ordinary case, not an
/// exotic one: the app is restarted daily, and a memory-only record would re-ingest every request and
/// re-report every closed line on the next launch. Same class as the resume/`--continue` defect the
/// general supervisor was made stateless to avoid — a boot that re-runs a completed gesture.
/// </para>
/// </summary>
/// <param name="Requests">Every request ever ingested here, in ingestion order. Append-only.</param>
/// <param name="OrchestrationClosedReportedUtc">When the closure of the whole orchestration was reported, or null.</param>
/// <param name="AppPlanWriteStampUtc">
/// The plan file's own last-write time immediately after the APP last rewrote it.
/// <para>
/// It exists because <see cref="LedgerHealth_Tracker.Is_LedgerBehind"/> is a pure mtime comparison, and
/// an app-authored ingestion bumps that mtime — which deleted <c>.ledger-behind</c> and released a
/// supervisor whose turn-end block was raised by an owner message it had still not written down. The
/// app was paying the session's debt on its behalf, silently, defeating the one enforcement
/// `supervisor.md` promises ("you cannot end a turn while it is unpaid").
/// </para>
/// </param>
public sealed record PlanBackendState(
    IReadOnlyList<TrackedPlanRequest> Requests,
    DateTime? OrchestrationClosedReportedUtc,
    DateTime? AppPlanWriteStampUtc)
{
    public static PlanBackendState Empty()
    {
        return new PlanBackendState([], null, null);
    }

    public bool Knows(string requestId)
    {
        return Requests.Any(request => request.RequestId == requestId);
    }

    /// <summary>Whether another request is already tracked against this exact ledger line.</summary>
    public bool Tracks_LedgerRow(string ledgerRowRef, string exceptRequestId)
    {
        return Requests.Any(request =>
            request.RequestId != exceptRequestId
            && string.Equals(request.LedgerRowRef, ledgerRowRef, StringComparison.Ordinal));
    }

    /// <summary>Replaces the entry with the same id IN PLACE, so the file keeps ingestion order.</summary>
    public PlanBackendState With(TrackedPlanRequest tracked)
    {
        List<TrackedPlanRequest> requests = [.. Requests];

        for (var index = 0; index < requests.Count; index++)
        {
            if (requests[index].RequestId != tracked.RequestId)
                continue;

            requests[index] = tracked;

            return this with { Requests = requests };
        }

        requests.Add(tracked);

        return this with { Requests = requests };
    }
}
