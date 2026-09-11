using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanBackend;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning.PlanBackend;

/// <summary>
/// THE DEFAULT BACKEND IS TODAY, UNCHANGED — the one property the whole seam is judged on. An
/// installation that configures nothing must not gain a file, lose a file, or see one character of
/// its PLAN.md move.
///
/// Production never even reaches the step with this backend — <see cref="PlanBackendSync_Decider"/>
/// returns false for it first, which its own tests pin. This is the layer below that: even if the step
/// IS reached, it does nothing. Both are asserted because they are two different ways for the claim to
/// be false.
/// </summary>
public class PlanMdBackendIsTodayTests : IDisposable
{
    const string PLAN = """
        # PLAN — repo (orch-1)

        - [x] the thing the owner asked for
        - [>] the second thing

        ## PARKED — found, not asked for

        - the tailer's retry count is unbounded — imp-2, 14:20

        """;

    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public PlanMdBackendIsTodayTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-planbackend-default-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempRoot, "orch-1"));
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        File.WriteAllText(_paths.Get_PlanFile("orch-1"), PLAN);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>It has nothing to hand over: an empty list is what makes the step a no-op end to end.</summary>
    [Fact]
    public void ItHandsOverNoWork()
    {
        Assert.Empty(new PlanMdBackend().List_ApprovedRequests("orch-1"));
    }

    /// <summary>
    /// The plan is byte-identical after a sync, and no state file appeared beside it. Both halves are
    /// asserted: a step that wrote an empty state file every tick would pass the first alone while
    /// putting a new file into every orchestration folder in the system.
    /// </summary>
    [Fact]
    public void ASyncWithTheDefaultBackendTouchesNothing()
    {
        var outcome = PlanBackend_Step.Sync(
            new PlanMdBackend(),
            _paths,
            "orch-1",
            "orch-1",
            isClosed: false,
            () => new PlanRowEvidence(DateTime.UtcNow, null, null),
            DateTime.Now);

        Assert.False(outcome.DidAnything);
        Assert.Null(outcome.Failure);
        Assert.Equal(PLAN, File.ReadAllText(_paths.Get_PlanFile("orch-1")));
        Assert.False(File.Exists(_paths.Get_PlanBackendStateFile("orch-1")));
    }

    /// <summary>
    /// And the ledger still parses to the same figures — the check that would catch a writer that
    /// "changed nothing" by rewriting the file into an equivalent-looking shape.
    /// </summary>
    [Fact]
    public void TheBarReadsTheSameFiguresAfterASync()
    {
        var before = PlanLedger_Parser.Parse_OrNull(File.ReadAllText(_paths.Get_PlanFile("orch-1")));

        PlanBackend_Step.Sync(
            new PlanMdBackend(),
            _paths,
            "orch-1",
            "orch-1",
            isClosed: false,
            () => new PlanRowEvidence(DateTime.UtcNow, null, null),
            DateTime.Now);

        var after = PlanLedger_Parser.Parse_OrNull(File.ReadAllText(_paths.Get_PlanFile("orch-1")));

        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equal(before!.Total, after!.Total);
        Assert.Equal(before.Done, after.Done);
        Assert.Equal(2, after.Total);
    }

    /// <summary>A closed orchestration the backend never heard of is not announced to it.</summary>
    [Fact]
    public void AClosedOrchestrationWithNoTrackedRequestsIsNotReported()
    {
        var backend = new RecordingPlanBackend();

        var outcome = PlanBackend_Step.Sync(
            backend,
            _paths,
            "orch-1",
            "orch-1",
            isClosed: true,
            () => new PlanRowEvidence(DateTime.UtcNow, null, null),
            DateTime.Now);

        Assert.False(outcome.OrchestrationClosedReported);
        Assert.Empty(backend.OrchestrationsClosed);
    }
}
