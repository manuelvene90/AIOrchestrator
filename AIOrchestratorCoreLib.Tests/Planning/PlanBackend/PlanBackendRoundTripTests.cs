using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanBackend;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning.PlanBackend;

/// <summary>
/// THE WHOLE ROUND TRIP, END TO END, against a second backend: two approved requests become two rows
/// and two ledger lines, one of those lines closes, and the closure is reported ONCE — however many
/// times the tick sees it.
///
/// <para>
/// "Once, however many times it is seen" is the requirement worth pinning hardest, because the failure
/// is invisible from inside the app: the second report costs nothing here and lands upstream as a
/// duplicate closure on somebody else's board. The app's guard is a persisted stamp, so the test
/// re-runs the sync exactly as the tick would rather than asserting on an in-memory field.
/// </para>
/// </summary>
public class PlanBackendRoundTripTests : IDisposable
{
    const string ORCH_ID = "repo-slug-1";

    const string SEEDED_PLAN = """
        # PLAN — repo (repo-slug-1)

        - [>] agree the direction with the owner

        ## PARKED — found, not asked for

        """;

    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;
    readonly RecordingPlanBackend _backend = new();

    public PlanBackendRoundTripTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-planbackend-trip-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempRoot, ORCH_ID));
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        File.WriteAllText(_paths.Get_PlanFile(ORCH_ID), SEEDED_PLAN);

        _backend.Approved.Add(new ApprovedPlanRequest("FIN-D-100", "add the export button", "I want an export button"));
        _backend.Approved.Add(new ApprovedPlanRequest("FIN-D-101", "cache the report query", "the report page is slow"));
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    PlanBackendOutcome Sync(bool isClosed = false)
    {
        return PlanBackend_Step.Sync(
            _backend,
            _paths,
            ORCH_ID,
            "repo-slug-1",
            isClosed,
            () => new PlanRowEvidence(DateTime.UtcNow, "owner-channel #12 FROM supervisor", "export button shipped"),
            DateTime.Now);
    }

    string Plan()
    {
        return File.ReadAllText(_paths.Get_PlanFile(ORCH_ID));
    }

    void Mark_Done(string ledgerRowRef)
    {
        File.WriteAllText(_paths.Get_PlanFile(ORCH_ID), Plan().Replace($"- [ ] {ledgerRowRef}", $"- [x] {ledgerRowRef}"));
    }

    /// <summary>Two approved requests, two rows in the table, two lines in the ledger, two acknowledgements.</summary>
    [Fact]
    public void TwoApprovedRequestsBecomeTwoRowsAndTwoLedgerLines()
    {
        var outcome = Sync();

        Assert.Equal(2, outcome.RequestsIngested);
        Assert.Null(outcome.Failure);

        var progress = PlanLedger_Parser.Parse_OrNull(Plan());

        Assert.Equal(3, progress!.Total);
        Assert.Contains(progress.Lines, line => line.Text == "add the export button");
        Assert.Contains(progress.Lines, line => line.Text == "cache the report query");

        Assert.Contains("| 1 |", Plan());
        Assert.Contains("| 2 |", Plan());
        Assert.Contains("I want an export button", Plan());

        Assert.Equal(
            [("FIN-D-100", "add the export button"), ("FIN-D-101", "cache the report query")],
            _backend.Acknowledged.Select(call => (call.RequestId, call.LedgerRowRef)));
    }

    /// <summary>A second tick with the same approved list writes nothing and says nothing.</summary>
    [Fact]
    public void ASecondTickIngestsNothing()
    {
        Sync();
        var planAfterFirst = Plan();

        var second = Sync();

        Assert.Equal(0, second.RequestsIngested);
        Assert.Equal(planAfterFirst, Plan());
        Assert.Equal(2, _backend.Acknowledged.Count);
    }

    /// <summary>
    /// THE ONE THAT MATTERS. The line closes, the transition is seen twice — the tick runs every
    /// minute and the ledger does not change back — and the backend is told once.
    /// </summary>
    [Fact]
    public void AClosedRowIsReportedOnceEvenWhenTheTransitionIsSeenTwice()
    {
        Sync();
        Mark_Done("add the export button");

        var first = Sync();
        var second = Sync();

        Assert.Equal(1, first.RowsReportedClosed);
        Assert.Equal(0, second.RowsReportedClosed);

        var call = Assert.Single(_backend.Closed);

        Assert.Equal(ORCH_ID, call.OrchId);
        Assert.Equal("add the export button", call.LedgerRowRef);

        // THE REQUEST ID TRAVELS WITH IT. An upstream system addresses its own rows by id; leaving it
        // to reverse-map a free-text ledger line back to an issue key is the one thing in this contract
        // an adapter could not have worked around.
        Assert.Equal("FIN-D-100", call.RequestId);
        Assert.Equal("owner-channel #12 FROM supervisor", call.Evidence.ChannelEntryRef);
    }

    /// <summary>
    /// And it survives a restart, because the stamp is on disk rather than in the process. A
    /// memory-only guard passes the test above and re-reports every closed line on the next launch —
    /// which is the ordinary case, not the exotic one: this app is restarted daily.
    /// </summary>
    [Fact]
    public void AndItIsStillReportedOnlyOnceAfterARestart()
    {
        Sync();
        Mark_Done("add the export button");
        Sync();

        // A "restart" is a fresh backend and a fresh step call — the only thing that carries over is
        // the state file, which is exactly the claim.
        var afterRestart = new RecordingPlanBackend();
        afterRestart.Approved.AddRange(_backend.Approved);

        PlanBackend_Step.Sync(
            afterRestart,
            _paths,
            ORCH_ID,
            "repo-slug-1",
            isClosed: false,
            () => new PlanRowEvidence(DateTime.UtcNow, null, null),
            DateTime.Now);

        Assert.Empty(afterRestart.Closed);
        Assert.Empty(afterRestart.Acknowledged);
    }

    /// <summary>
    /// The other line is still open, so it is not reported — the closure report follows the MARKER,
    /// not the fact that the request was ingested.
    /// </summary>
    [Fact]
    public void AnOpenRowIsNotReported()
    {
        Sync();
        Mark_Done("add the export button");
        Sync();

        Assert.DoesNotContain(_backend.Closed, call => call.LedgerRowRef == "cache the report query");
    }

    /// <summary>
    /// A throwing backend costs this tick and nothing more: nothing is recorded as reported, and the
    /// next tick sends it again. At-least-once is what the wire guarantees.
    /// </summary>
    [Fact]
    public void AFailedReportIsRetriedOnTheNextTick()
    {
        Sync();
        Mark_Done("add the export button");

        _backend.ThrowOnReportClosed = new InvalidOperationException("upstream unreachable");

        var failed = Sync();

        Assert.Equal(0, failed.RowsReportedClosed);
        Assert.Contains("upstream unreachable", failed.Failure);

        _backend.ThrowOnReportClosed = null;

        Assert.Equal(1, Sync().RowsReportedClosed);
        Assert.Single(_backend.Closed);
    }

    /// <summary>
    /// PARKED IS NEVER SYNCHRONISED — pinned on a TRACKED line, which is the only way this assertion
    /// means anything. Asserting that some unrelated parked item is not reported passes for a second
    /// reason (it was never a request), so it would stay green with the parser's section handling
    /// deleted. Here the line moved under PARKED is one the backend is genuinely waiting on: if the
    /// parser stopped skipping the section, this goes red.
    /// </summary>
    [Fact]
    public void ATrackedLineMovedUnderParkedIsNotReportedClosed()
    {
        Sync();

        // The supervisor decides it is not the endeavour after all and parks it, marked done.
        var parked = Plan()
            .Replace("- [ ] add the export button\n", string.Empty)
            .Replace(
                "## PARKED — found, not asked for",
                "## PARKED — found, not asked for\n\n- [x] add the export button");

        File.WriteAllText(_paths.Get_PlanFile(ORCH_ID), parked);

        var outcome = Sync();

        Assert.Equal(0, outcome.RowsReportedClosed);
        Assert.Empty(_backend.Closed);
    }

    /// <summary>
    /// AN ACKNOWLEDGEMENT THAT THREW IS RETRIED, and this is the affordance the first version built and
    /// never used: the state file is written before the call, so a throw leaves a request that is in the
    /// plan and unacknowledged for ever — the skip guard read "do I know this id", not "did the call
    /// land". Upstream never learned the request had been taken on.
    /// </summary>
    [Fact]
    public void AnAcknowledgementThatFailedIsRetriedOnTheNextTick()
    {
        _backend.ThrowOnAcknowledge = new InvalidOperationException("upstream unreachable");

        var first = Sync();

        Assert.Equal(2, first.RequestsIngested);
        Assert.Equal(0, first.RequestsAcknowledged);
        Assert.Contains("upstream unreachable", first.Failure);
        Assert.Empty(_backend.Acknowledged);

        _backend.ThrowOnAcknowledge = null;

        var second = Sync();

        Assert.Equal(0, second.RequestsIngested);
        Assert.Equal(2, second.RequestsAcknowledged);
        Assert.Equal(2, _backend.Acknowledged.Count);

        // And not a third time.
        Assert.Equal(0, Sync().RequestsAcknowledged);
    }

    /// <summary>
    /// TWO REQUESTS, ONE TITLE. Both would be tracked against the same ledger line, so its single `[x]`
    /// would report two deliveries upstream for one piece of work. The second is refused and named.
    /// </summary>
    [Fact]
    public void ASecondRequestWithTheSameTitleIsRefusedRatherThanSharingALine()
    {
        _backend.Approved.Add(new ApprovedPlanRequest("FIN-D-102", "add the export button", "the same thing again"));

        var outcome = Sync();

        Assert.Equal(2, outcome.RequestsIngested);
        Assert.Contains("same title as one already tracked", outcome.Failure);

        Mark_Done("add the export button");

        var closing = Sync();

        Assert.Equal(1, closing.RowsReportedClosed);
        Assert.Single(_backend.Closed);
    }

    /// <summary>
    /// A request naming a line that is ALREADY DONE is refused — attaching to it would report that
    /// request delivered, with evidence, for work finished before the request existed.
    /// </summary>
    [Fact]
    public void ARequestNamingAnAlreadyFinishedLineIsRefusedAndNamed()
    {
        Sync();
        Mark_Done("add the export button");
        Sync();

        _backend.Approved.Add(new ApprovedPlanRequest("FIN-D-103", "add the export button", "do it again"));

        var outcome = Sync();

        Assert.Equal(0, outcome.RequestsIngested);
        Assert.Contains("already exists in the ledger marked [x]", outcome.Failure);
        Assert.Single(_backend.Closed);
    }

    /// <summary>
    /// The row's status is rewritten when its line closes. Left at "not started" it answers "nobody is
    /// on this" at every check-in the supervisor runs over the whole table — the app manufacturing
    /// permanent false alarms on the one artefact that exists to catch neglected requests.
    /// </summary>
    [Fact]
    public void AClosedRowsTableStatusIsBroughtUpToDate()
    {
        Sync();
        Mark_Done("add the export button");
        Sync();

        var rows = Plan().Split('\n').Where(line => line.StartsWith("| ")).ToList();

        Assert.Contains(rows, row => row.Contains("I want an export button") && row.Contains("reported upstream"));
        Assert.Contains(rows, row => row.Contains("the report page is slow") && row.Contains("not started"));
    }

    /// <summary>
    /// THE APP'S OWN WRITE DOES NOT PAY THE SUPERVISOR'S LEDGER DEBT. `Is_LedgerBehind` is a pure mtime
    /// comparison, so an ingestion looked exactly like the supervisor updating its ledger and deleted
    /// the flag that blocks its turn end — the app paying a debt the session still owed, on the one
    /// enforcement supervisor.md promises.
    /// </summary>
    [Fact]
    public void AnIngestionDoesNotClearTheLedgerDebtFlag()
    {
        // A plan last written BEFORE the verdict is the debt: the supervisor answered an implementer
        // and has not recorded it.
        File.SetLastWriteTimeUtc(_paths.Get_PlanFile(ORCH_ID), DateTime.UtcNow.AddMinutes(-20));

        var verdictUtc = DateTime.UtcNow.AddMinutes(-10);

        Assert.True(AIOrchestratorCoreLib.Planning.LedgerHealth_Tracker.Is_LedgerBehind(_paths, ORCH_ID, verdictUtc));

        Sync();

        Assert.True(AIOrchestratorCoreLib.Planning.LedgerHealth_Tracker.Is_LedgerBehind(_paths, ORCH_ID, verdictUtc));

        // The session itself writing the plan DOES pay it — otherwise the fix would be a permanent block.
        File.WriteAllText(_paths.Get_PlanFile(ORCH_ID), Plan() + "\n- [ ] something the supervisor wrote\n");

        Assert.False(AIOrchestratorCoreLib.Planning.LedgerHealth_Tracker.Is_LedgerBehind(_paths, ORCH_ID, verdictUtc));
    }

    /// <summary>
    /// A PLAN THAT MOVED BETWEEN THE READ AND THE WRITE IS LEFT ALONE. The session that owns PLAN.md
    /// edits it continuously and the app rewrites it whole: without this, a supervisor's save landing in
    /// that window is silently discarded — its `[x]` marks lost and the bar going backwards.
    ///
    /// Pinned on the guard itself rather than through a sync, deliberately: no call leaves the process
    /// between the read and the write, so there is no hook a test could use to land inside that window
    /// — and a test that cannot reach the thing it names is worse than no test.
    /// </summary>
    [Fact]
    public void APlanThatChangedUnderneathIsNotOverwritten()
    {
        var planFile = _paths.Get_PlanFile(ORCH_ID);
        var stampAtRead = File.GetLastWriteTimeUtc(planFile);

        var supervisorText = Plan() + "\n- [x] something the supervisor just finished\n";

        File.WriteAllText(planFile, supervisorText);
        File.SetLastWriteTimeUtc(planFile, stampAtRead.AddSeconds(1));

        var refused = PlanFile_GuardedWriter.Write_IfUnchanged(planFile, stampAtRead, "the app's whole-file rewrite");

        Assert.Null(refused);
        Assert.Equal(supervisorText, File.ReadAllText(planFile));

        // And it does write when nothing moved — otherwise the guard would be a permanent refusal.
        var written = PlanFile_GuardedWriter.Write_IfUnchanged(planFile, File.GetLastWriteTimeUtc(planFile), "written");

        Assert.NotNull(written);
        Assert.Equal("written", File.ReadAllText(planFile));
    }

    /// <summary>
    /// The orchestration closes: one summary, once, and it reads what the LEDGER says rather than
    /// claiming anything was verified.
    /// </summary>
    [Fact]
    public void ClosingTheOrchestrationIsReportedOnce()
    {
        Sync();
        Mark_Done("add the export button");
        Sync();

        var closing = Sync(isClosed: true);
        var again = Sync(isClosed: true);

        Assert.True(closing.OrchestrationClosedReported);
        Assert.False(again.OrchestrationClosedReported);

        var call = Assert.Single(_backend.OrchestrationsClosed);

        Assert.Contains("1/3 ledger lines done", call.Summary);
        Assert.Contains("2 still open", call.Summary);
    }
}
