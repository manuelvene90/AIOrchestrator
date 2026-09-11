namespace AIOrchestratorCoreLib.Planning.PlanBackend;

/// <summary>
/// THE PLAN'S OTHER END — where an orchestration's work comes FROM and where its completion goes
/// BACK TO, when that place is not this machine.
///
/// <para>
/// PLAN.md STAYS THE EXECUTION LEDGER. This is not a replacement for it and cannot become one: the
/// sections, the markers, the turn-end hook and the owner's progress bar are unchanged, and a
/// backend that vanishes leaves an orchestration that still runs exactly as it did. What a backend
/// adds is a synchronisation of ONE section — <c>## OWNER REQUESTS</c>, the table of what the owner
/// asked for — in both directions: an approved request upstream becomes a row here, and a row
/// finishing here is reported upstream. <c>## PARKED</c> is never synchronised: a discovery nobody
/// asked for is local by definition (decision 22).
/// </para>
/// <para>
/// THE DEFAULT IMPLEMENTATION DOES NOTHING, and that is the contract's most important property. See
/// <see cref="PlanMdBackend"/>: it lists no requests and reports nothing, so an installation that
/// configures no backend behaves byte for byte as it did before this interface existed.
/// </para>
/// <para>
/// IDEMPOTENCE IS DEFENDED ON BOTH SIDES, and the app's half is not symmetric. It persists what it
/// has already ingested and already reported (<see cref="PlanBackendState_Store"/>), so a restart, a
/// re-read of the same ledger, or the same transition seen on two consecutive passes produce ONE
/// call. For a CLOSURE the record is written after the call, so a process killed in between retries
/// it; for an ACKNOWLEDGEMENT the record is written before the call — a row in the owner's plan must
/// never be duplicated — and the retry is explicit: a tracked request with no acknowledgement stamp
/// is acknowledged again on the next pass. Either way an implementation must tolerate a repeated
/// requestId or row reference: at-least-once is what the wire guarantees, exactly-once is what the
/// two sides together deliver.
/// </para>
/// <para>
/// IMPLEMENTATIONS LIVE OUTSIDE THIS REPOSITORY. One is loaded by name from config.json (see
/// <see cref="PlanBackend_Loader"/>), so nothing here knows what a particular planning system is
/// called, speaks its protocol, or depends on it being reachable. Every method must therefore be
/// safe to call on a tick loop: a slow or throwing implementation costs its orchestration this
/// tick's synchronisation and nothing else.
/// </para>
/// </summary>
public interface IPlanBackend
{
    /// <summary>
    /// Requests that are APPROVED upstream and do not yet exist in this orchestration's plan — the
    /// only inbound gesture. Readiness is the upstream system's judgement, not ours: an
    /// implementation reading a "ready" flag must read every blocker alongside it (a row can be
    /// flagged ready and still be held by an unmet condition), because a request handed over too
    /// early becomes a ledger line nobody can finish.
    /// </summary>
    IReadOnlyList<ApprovedPlanRequest> List_ApprovedRequests(string orchId);

    /// <summary>
    /// The request has become part of this orchestration's plan, at <paramref name="ledgerRowRef"/>
    /// — the ledger line's own text, which is how this codebase identifies a ledger line everywhere
    /// else (<see cref="LedgerTransition_Detector"/> matches on text, never on position).
    /// </summary>
    void Acknowledge_Request(string orchId, string requestId, string ledgerRowRef);

    /// <summary>
    /// That ledger line is now <c>[x]</c>. It is a REPORT, not a closure: "merged is not verified"
    /// holds across the boundary too, so an implementation submits the evidence for whoever decides
    /// upstream rather than closing the row itself.
    ///
    /// <para>
    /// It carries the REQUEST ID as well as the row reference. The app is holding both at the call
    /// site, and an upstream system addresses its own rows by id — leaving it to reverse-map a
    /// free-text ledger line back to an issue key was the one thing in this contract an adapter could
    /// not have worked around.
    /// </para>
    /// </summary>
    void Report_RowClosed(string orchId, string requestId, string ledgerRowRef, PlanRowEvidence evidence);

    /// <summary>The orchestration is closed. Sent once, with a one-line reading of its final ledger.</summary>
    void Report_OrchestrationClosed(string orchId, string summary);
}
