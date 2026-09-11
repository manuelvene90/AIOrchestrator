using AIOrchestratorCoreLib.Planning.PlanBackend;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning.PlanBackend;

/// <summary>
/// THE FILE THAT PREVENTS DUPLICATE CALLS MUST NOT LOSE ITS MEMORY OVER ONE BAD FIELD. The first
/// version wrapped the whole parse in a single catch, so a single `"ownerRequestNumber": "1"` — an
/// older writer, a hand-edit, a future version — returned an EMPTY state: every request forgotten and
/// the orchestration-closed stamp with it, so the next pass re-acknowledged, re-reported and
/// re-announced everything upstream. Three duplicate calls out of one typo.
/// </summary>
public class PlanBackendStateStoreTests : IDisposable
{
    const string ORCH_ID = "orch-1";

    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public PlanBackendStateStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-planbackend-state-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempRoot, ORCH_ID));
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    static PlanBackendState With_Two()
    {
        return PlanBackendState.Empty()
            .With(new TrackedPlanRequest("FIN-1", "first", 1, DateTime.UtcNow, null))
            .With(new TrackedPlanRequest("FIN-2", "second", 2, null, null));
    }

    [Fact]
    public void ItRoundTripsRequestsInOrderWithTheirStamps()
    {
        var written = With_Two() with { OrchestrationClosedReportedUtc = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc) };

        PlanBackendState_Store.Write(_paths, ORCH_ID, written);

        var read = PlanBackendState_Store.Read(_paths, ORCH_ID);

        Assert.Equal(["FIN-1", "FIN-2"], read.Requests.Select(request => request.RequestId));
        Assert.Equal(2, read.Requests[1].OwnerRequestNumber);
        Assert.NotNull(read.Requests[0].AcknowledgedUtc);
        Assert.Equal(DateTimeKind.Utc, read.Requests[0].AcknowledgedUtc!.Value.Kind);
        Assert.Null(read.Requests[1].AcknowledgedUtc);
        Assert.Equal(written.OrchestrationClosedReportedUtc, read.OrchestrationClosedReportedUtc);
    }

    /// <summary>Updating an entry keeps its POSITION — the file is the ingestion order, not a churn log.</summary>
    [Fact]
    public void UpdatingAnEntryDoesNotReorderTheFile()
    {
        var state = With_Two();
        var updated = state.With(state.Requests[0] with { ClosedReportedUtc = DateTime.UtcNow });

        Assert.Equal(["FIN-1", "FIN-2"], updated.Requests.Select(request => request.RequestId));
    }

    /// <summary>ONE bad entry is dropped; the others, and the closed stamp, survive.</summary>
    [Fact]
    public void AForeignTypeCostsOneEntryRatherThanTheWholeFile()
    {
        File.WriteAllText(_paths.Get_PlanBackendStateFile(ORCH_ID), """
            {
              "requests": [
                { "requestId": "FIN-1", "ledgerRowRef": "first", "ownerRequestNumber": "1", "acknowledgedUtc": null, "closedReportedUtc": null },
                { "requestId": "FIN-2", "ledgerRowRef": "second", "ownerRequestNumber": 2, "acknowledgedUtc": null, "closedReportedUtc": null },
                { "requestId": 12345, "ledgerRowRef": "third" }
              ],
              "orchestrationClosedReportedUtc": "2026-09-06T12:00:00.0000000Z"
            }
            """);

        var read = PlanBackendState_Store.Read(_paths, ORCH_ID);

        // The string-typed number is read rather than thrown away; the entry with a numeric id is the
        // one that cannot be trusted, and only it is dropped.
        Assert.Equal(["FIN-1", "FIN-2"], read.Requests.Select(request => request.RequestId));
        Assert.Equal(1, read.Requests[0].OwnerRequestNumber);
        Assert.NotNull(read.OrchestrationClosedReportedUtc);
    }

    /// <summary>A file that is not JSON at all still reads as empty — the last resort, not the first.</summary>
    [Fact]
    public void RubbishReadsAsEmpty()
    {
        File.WriteAllText(_paths.Get_PlanBackendStateFile(ORCH_ID), "not json at all");

        Assert.Empty(PlanBackendState_Store.Read(_paths, ORCH_ID).Requests);
    }

    [Fact]
    public void NoFileReadsAsEmpty()
    {
        Assert.Empty(PlanBackendState_Store.Read(_paths, ORCH_ID).Requests);
        Assert.Null(PlanBackendState_Store.Read(_paths, ORCH_ID).AppPlanWriteStampUtc);
    }

    /// <summary>
    /// The app recognising its own write is what stops an ingestion paying the supervisor's ledger debt.
    /// Exact mtime equality, because the stamp is READ BACK from the file after writing it — no
    /// tolerance window is needed and a window would swallow a real session write.
    /// </summary>
    [Fact]
    public void ItRecognisesThePlanWriteItRecorded()
    {
        var planFile = _paths.Get_PlanFile(ORCH_ID);

        File.WriteAllText(planFile, "# PLAN\n");

        var stamp = File.GetLastWriteTimeUtc(planFile);

        PlanBackendState_Store.Write(_paths, ORCH_ID, PlanBackendState.Empty() with { AppPlanWriteStampUtc = stamp });

        Assert.True(PlanBackendState_Store.Wrote_ThePlan_Itself(_paths, ORCH_ID, stamp));
        Assert.False(PlanBackendState_Store.Wrote_ThePlan_Itself(_paths, ORCH_ID, stamp.AddTicks(1)));
    }
}
