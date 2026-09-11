using AIOrchestratorCoreLib.Storage;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Planning.PlanBackend;

/// <summary>What one orchestration's synchronisation did this tick, for the log.</summary>
/// <param name="RequestsIngested">Approved upstream requests newly written into PLAN.md.</param>
/// <param name="RequestsAcknowledged">Acknowledgements that landed this tick, including retries of earlier failures.</param>
/// <param name="RowsReportedClosed">Ledger lines of upstream origin newly reported as [x].</param>
/// <param name="OrchestrationClosedReported">Whether the orchestration's own closure was reported this tick.</param>
/// <param name="Failure">
/// What could not be done, in words — a backend call that threw, a request that could not be written,
/// a plan that changed underneath. NEVER silence: a request dropped without a word is a request nobody
/// can go looking for (decision 21, on a component that cannot evaluate its predicate saying so).
/// </param>
public readonly record struct PlanBackendOutcome(
    int RequestsIngested,
    int RequestsAcknowledged,
    int RowsReportedClosed,
    bool OrchestrationClosedReported,
    string? Failure)
{
    public bool DidAnything => RequestsIngested > 0 || RequestsAcknowledged > 0 || RowsReportedClosed > 0 || OrchestrationClosedReported;
}

/// <summary>
/// ONE ORCHESTRATION'S ROUND TRIP WITH ITS PLAN BACKEND, one tick's worth: pull in what was approved
/// upstream, push out what has closed here.
///
/// <para>
/// A STEP RATHER THAN ENGINE CODE, for the reason this repository has written down twice already: the
/// bridge engine is <c>internal sealed</c> with a single entry point, so anything decided inside it is
/// unpinnable by construction (see <see cref="LedgerHealth_Step"/>, and
/// <see cref="PlanLedgerRows_Builder"/> on the same argument from the window's side). Everything that
/// can be got wrong is here, where the suite reaches it; the engine keeps the call and the loop.
/// </para>
/// <para>
/// THE WRITE ORDER, SAID AS IT IS. PLAN.md is written first, the state file immediately after, and the
/// backend told last — so an interruption can leave a request that is in the plan and NOT yet
/// acknowledged. That is why <see cref="TrackedPlanRequest.AcknowledgedUtc"/> exists and why the
/// ingestion pass RETRIES every tracked request whose stamp is still null: without that retry the
/// acknowledgement is lost for good, which is exactly what the first version did while its comment
/// claimed the opposite. A duplicated ROW in the owner's plan is the one thing this order cannot
/// produce, and the one thing worth being strict about.
/// </para>
/// <para>
/// IT WRITES PLAN.md ONLY WHEN THE FILE HAS NOT MOVED SINCE IT WAS READ. The session that owns that
/// file edits it continuously and the app rewrites it whole; without the check, a supervisor's save
/// landing in the millisecond between read and write is silently discarded — its `[x]` marks lost, the
/// bar going backwards. A mtime that moved means "not this tick", and the request is written on the
/// next one.
/// </para>
/// <para>
/// NOTHING HERE PARSES A CHANNEL. Evidence for a closed row is supplied by the caller as a delegate,
/// asked for at most ONCE per pass and only when there is actually a row to report.
/// </para>
/// </summary>
public static class PlanBackend_Step
{
    public static PlanBackendOutcome Sync(
        IPlanBackend backend,
        ISupervisionPaths paths,
        string orchId,
        string displayName,
        bool isClosed,
        Func<PlanRowEvidence> readEvidence,
        DateTime nowLocal)
    {
        var state = PlanBackendState_Store.Read(paths, orchId);

        if (isClosed)
            return Report_Closure(backend, paths, orchId, displayName, state);

        var planFile = paths.Get_PlanFile(orchId);

        // NO PLAN FILE, NO SYNCHRONISATION. Every orchestration is seeded with one at launch, so an
        // absent file means this folder is not ready (or not an orchestration at all) — and writing
        // one here would put a plan where the launcher decided there should not be one yet.
        if (!File.Exists(planFile))
            return new PlanBackendOutcome(0, 0, 0, false, null);

        List<string> failures = [];

        var (ingested, acknowledged) = Ingest_ApprovedRequests(backend, paths, orchId, planFile, ref state, nowLocal, failures);
        var closed = Report_ClosedRows(backend, paths, orchId, planFile, ref state, readEvidence, failures);

        return new PlanBackendOutcome(ingested, acknowledged, closed, false, Join_OrNull(failures));
    }

    static (int Ingested, int Acknowledged) Ingest_ApprovedRequests(
        IPlanBackend backend,
        ISupervisionPaths paths,
        string orchId,
        string planFile,
        ref PlanBackendState state,
        DateTime nowLocal,
        List<string> failures)
    {
        IReadOnlyList<ApprovedPlanRequest> approved;

        try
        {
            approved = backend.List_ApprovedRequests(orchId);
        }
        catch (Exception ex)
        {
            failures.Add($"listing approved requests failed: {ex.Message}");

            return (0, 0);
        }

        var ingested = 0;
        var acknowledged = 0;

        foreach (var request in approved)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.RequestId))
            {
                failures.Add("an approved request arrived with no id, so nothing could be tracked against it");
                continue;
            }

            var tracked = state.Requests.FirstOrDefault(entry => entry.RequestId == request.RequestId);

            // ALREADY IN THE PLAN. The only thing that may still be owed is the acknowledgement, and it
            // is retried here — the state file is written before the call, so a throw or a crash leaves
            // exactly this shape and nothing else would ever send it.
            if (tracked != null)
            {
                if (tracked.AcknowledgedUtc == null && Try_Acknowledge(backend, paths, orchId, tracked, ref state, failures))
                    acknowledged++;

                continue;
            }

            var planText = Safe_FileReader.Read_AllText_OrEmpty(planFile);
            var stampAtRead = File.GetLastWriteTimeUtc(planFile);
            var write = PlanRequest_Writer.Write_Request(planText, request, nowLocal);

            if (!write.Trackable)
            {
                failures.Add(write.Refusal ?? $"request '{request.RequestId}' could not be written into the plan");
                continue;
            }

            // TWO UPSTREAM REQUESTS, ONE LEDGER LINE would make that line's single `[x]` report both of
            // them closed — two deliveries claimed upstream for one piece of work. The second one is
            // refused and named instead.
            if (state.Tracks_LedgerRow(write.LedgerRowRef, request.RequestId))
            {
                failures.Add($"request '{request.RequestId}' has the same title as one already tracked ('{write.LedgerRowRef}'), so it was not ingested");
                continue;
            }

            if (write.Changed && !Try_WritePlan(paths, orchId, planFile, stampAtRead, write.PlanText, ref state))
            {
                failures.Add($"PLAN.md changed while request '{request.RequestId}' was being written — retrying next tick");
                continue;
            }

            var entry = new TrackedPlanRequest(request.RequestId, write.LedgerRowRef, write.OwnerRequestNumber, null, null);

            state = state.With(entry);
            PlanBackendState_Store.Write(paths, orchId, state);

            ingested++;

            if (Try_Acknowledge(backend, paths, orchId, entry, ref state, failures))
                acknowledged++;
        }

        return (ingested, acknowledged);
    }

    static bool Try_Acknowledge(
        IPlanBackend backend,
        ISupervisionPaths paths,
        string orchId,
        TrackedPlanRequest tracked,
        ref PlanBackendState state,
        List<string> failures)
    {
        try
        {
            backend.Acknowledge_Request(orchId, tracked.RequestId, tracked.LedgerRowRef);
        }
        catch (Exception ex)
        {
            failures.Add($"acknowledging '{tracked.RequestId}' failed: {ex.Message}");

            return false;
        }

        state = state.With(tracked with { AcknowledgedUtc = DateTime.UtcNow });
        PlanBackendState_Store.Write(paths, orchId, state);

        return true;
    }

    static int Report_ClosedRows(
        IPlanBackend backend,
        ISupervisionPaths paths,
        string orchId,
        string planFile,
        ref PlanBackendState state,
        Func<PlanRowEvidence> readEvidence,
        List<string> failures)
    {
        var owed = state.Requests.Where(request => request.ClosedReportedUtc == null).ToList();

        if (owed.Count == 0)
            return 0;

        var progress = PlanLedger_Parser.Parse_OrNull(Safe_FileReader.Read_AllText_OrEmpty(planFile));

        if (progress == null)
            return 0;

        var reported = 0;

        // ASKED FOR ONCE PER PASS. Reading it per row re-parses the whole owner channel for every line
        // that happens to close in the same minute, and stamps each with a different observation time
        // for one observation.
        PlanRowEvidence? evidence = null;

        foreach (var request in owed)
        {
            // TOP-LEVEL, MATCHED ON TEXT — PlanLedger_Lines owns that rule, so this reader and
            // LedgerTransition_Detector cannot disagree about which line is which.
            var line = PlanLedger_Lines.Find_TopLevel_OrNull(progress, request.LedgerRowRef);

            if (line?.Marker != "x")
                continue;

            evidence ??= readEvidence();

            try
            {
                backend.Report_RowClosed(orchId, request.RequestId, request.LedgerRowRef, evidence);
            }
            catch (Exception ex)
            {
                failures.Add($"reporting '{request.LedgerRowRef}' closed failed: {ex.Message}");
                continue;
            }

            // RECORDED ONLY AFTER THE CALL RETURNED. A second sighting of the same transition — the
            // next tick, or the tick after a restart — finds this stamp and says nothing.
            state = state.With(request with { ClosedReportedUtc = DateTime.UtcNow });
            PlanBackendState_Store.Write(paths, orchId, state);

            reported++;

            Update_RowStatus_BestEffort(paths, orchId, planFile, request.RequestId, ref state);
        }

        return reported;
    }

    /// <summary>
    /// Rewrites the ingested row's status cell to say it is done and reported. BEST EFFORT, and after
    /// the report: the owner's board being right matters more than the table's wording, and a failed
    /// status edit must never cost or repeat the report.
    /// </summary>
    static void Update_RowStatus_BestEffort(
        ISupervisionPaths paths,
        string orchId,
        string planFile,
        string requestId,
        ref PlanBackendState state)
    {
        try
        {
            var planText = Safe_FileReader.Read_AllText_OrEmpty(planFile);
            var stampAtRead = File.GetLastWriteTimeUtc(planFile);

            var (updated, changed) = PlanRequest_Writer.Set_RowStatus(
                planText,
                requestId,
                PlanRequest_Writer.Describe_ReportedStatus(requestId));

            if (changed)
                Try_WritePlan(paths, orchId, planFile, stampAtRead, updated, ref state);
        }
        catch
        {
            // The report already landed; the table's wording is not worth a failed tick.
        }
    }

    /// <summary>
    /// Writes the plan through <see cref="PlanFile_GuardedWriter"/> and RECORDS THE RESULTING MTIME as
    /// the app's own, which is what keeps <see cref="LedgerHealth_Tracker.Is_LedgerBehind"/> honest —
    /// that check is a pure mtime comparison, so without the stamp an app-authored ingestion counted as
    /// the supervisor updating its ledger and deleted the flag blocking its turn end.
    /// </summary>
    static bool Try_WritePlan(
        ISupervisionPaths paths,
        string orchId,
        string planFile,
        DateTime stampAtRead,
        string planText,
        ref PlanBackendState state)
    {
        var stamp = PlanFile_GuardedWriter.Write_IfUnchanged(planFile, stampAtRead, planText);

        if (stamp == null)
            return false;

        state = state with { AppPlanWriteStampUtc = stamp };
        PlanBackendState_Store.Write(paths, orchId, state);

        return true;
    }

    static PlanBackendOutcome Report_Closure(
        IPlanBackend backend,
        ISupervisionPaths paths,
        string orchId,
        string displayName,
        PlanBackendState state)
    {
        // NEVER ENGAGED WITH THIS ORCHESTRATION, so there is nothing to close upstream. An orchestration
        // that ran entirely on PLAN.md must not announce itself to a backend it never spoke to.
        if (state.Requests.Count == 0 || state.OrchestrationClosedReportedUtc != null)
            return new PlanBackendOutcome(0, 0, 0, false, null);

        var progress = PlanLedger_Parser.Parse_OrNull(Safe_FileReader.Read_AllText_OrEmpty(paths.Get_PlanFile(orchId)));

        try
        {
            backend.Report_OrchestrationClosed(orchId, Describe_Summary(displayName, progress));
        }
        catch (Exception ex)
        {
            return new PlanBackendOutcome(0, 0, 0, false, $"reporting the orchestration closed failed: {ex.Message}");
        }

        PlanBackendState_Store.Write(paths, orchId, state with { OrchestrationClosedReportedUtc = DateTime.UtcNow });

        return new PlanBackendOutcome(0, 0, 0, true, null);
    }

    /// <summary>
    /// One line, and it says what the ledger SAYS rather than what the orchestration achieved — the app
    /// has no opinion on the second, and a summary that implied one would be the "merged is not
    /// verified" mistake in a sentence.
    /// </summary>
    public static string Describe_Summary(string displayName, PlanProgress.IPlanProgress? progress)
    {
        if (progress == null)
            return $"{displayName}: closed with no task ledger.";

        var dropped = progress.NotDoing > 0 ? $", {progress.NotDoing} not doing" : string.Empty;
        var open = progress.Total - progress.Done;
        var unfinished = open > 0 ? $", {open} still open" : string.Empty;

        return $"{displayName}: closed with {progress.Done}/{progress.Total} ledger lines done{dropped}{unfinished}.";
    }

    static string? Join_OrNull(List<string> failures)
    {
        return failures.Count == 0 ? null : string.Join("; ", failures);
    }
}
