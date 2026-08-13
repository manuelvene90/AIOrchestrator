using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Launching;

/// <summary>
/// rev-5 F3, F4 and F5 are ONE defect seen three ways: **a precondition read once, and a flag with no
/// way back.**
///
/// `SupervisorSpawnedUtc` is stamped BEFORE the spawn is attempted — deliberately, so no watchdog tick
/// sees "no pid file and no grace" and double-spawns — and it merges as a plain coalesce with no
/// `wasSet` escape hatch, so it is write-once and sticky. Nothing clears it, including the close
/// paths. The only shape check was at park time, and the parked window runs to twelve hours.
///
/// So: a promotion confirmed hours later executed on a stale precondition (F3); a `set-model` aimed at
/// a basic orchestration flipped its shape for ever and made every later promotion refuse itself (F4);
/// and a spawn that threw left the shape flipped with the solo still running, in a state the retry
/// path called "already a crew" (F5).
///
/// These cases pin the INVARIANT — what a promotion does given the state at the moment it acts —
/// rather than the three symptoms, because the symptoms are just three doors into the same room.
/// </summary>
public class PromotionLifecycleTests : IDisposable
{
    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly RecordingSpawner_Fake _spawner;
    readonly IOrchestrationLauncher _launcher;

    public PromotionLifecycleTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-promo-life-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _spawner = new RecordingSpawner_Fake();

        _launcher = OrchestrationLauncher_Factory.Create(
            _paths, OrchestratorConfigProvider_Factory.Create(_paths), _store, _spawner, OrchestrationLog_Factory.Create(_paths));
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// THE SECOND TAP DOES NOTHING. Two parked requests both pass the park-time check while neither
    /// has executed, so without a re-read at the moment of effect the second confirmation spawns a
    /// SECOND supervisor — and `Respawn_Supervisor` does not terminate the incumbent: it nulls the
    /// stored pid and clears the pid FILE, while both `Kill_AllSessions` and
    /// `Kill_OrchestrationSessions` enumerate pid files. The orphan would outlive the orchestration's
    /// close AND the app's exit, still appending to owner-channel.md.
    /// </summary>
    [Fact]
    public void PromotingTwiceDoesNotSpawnASecondSupervisor()
    {
        var basic = _launcher.Start_BasicOrchestration("repo", _tempRepo);

        _launcher.Promote_ToFullCrew(basic.OrchId);

        _spawner.SpawnedCommands.Clear();

        var again = _launcher.Promote_ToFullCrew(basic.OrchId);

        Assert.Empty(_spawner.SpawnedCommands);
        Assert.Single(again.Members, member => member.MemberId == "imp-1");
    }

    /// <summary>
    /// A HALF-PROMOTED ORCHESTRATION IS FINISHED, NOT REFUSED — the state F5 leaves behind when the
    /// spawn throws after the stamp. The old rule called it "already a crew" and refused the retry
    /// that its own failure message had asked for.
    ///
    /// Reproduced at the state rather than by forcing a throw: stamped as a crew, solo still live,
    /// which is exactly what a failed spawn leaves.
    /// </summary>
    [Fact]
    public void AHalfPromotedOrchestrationIsFinishedRatherThanRefused()
    {
        var basic = _launcher.Start_BasicOrchestration("repo", _tempRepo);

        // The stamp lands, as it does before any spawn attempt — and the solo is still running.
        _store.Set_SupervisorPid(basic.OrchId, null);

        Assert.Equal(
            PromotionReadiness.Incomplete,
            OrchestrationShape.Decide_PromotionReadiness(_store.Get_Session(basic.OrchId).SupervisorSpawnedUtc, hasLiveSolo: true));

        var finished = _launcher.Promote_ToFullCrew(basic.OrchId);

        // The solo is closed and an implementer exists — the promotion completed.
        Assert.All(finished.Members.Where(m => MemberKind_Ids.Resolve_Kind(m.MemberId) == MemberKinds.Solo), m => Assert.NotNull(m.ClosedUtc));
        Assert.Contains(finished.Members, m => m.MemberId == "imp-1" && m.ClosedUtc == null);
    }

    /// <summary>
    /// AND FINISHING IT DOES NOT ADD A SECOND IMPLEMENTER. An idempotent completion that adds a
    /// session every time it runs is not idempotent, it is just slower to notice.
    /// </summary>
    [Fact]
    public void FinishingAPromotionTwiceLeavesOneImplementer()
    {
        var basic = _launcher.Start_BasicOrchestration("repo", _tempRepo);

        _launcher.Promote_ToFullCrew(basic.OrchId);
        _launcher.Promote_ToFullCrew(basic.OrchId);
        var final = _launcher.Promote_ToFullCrew(basic.OrchId);

        Assert.Single(final.Members, member => MemberKind_Ids.Resolve_Kind(member.MemberId) == MemberKinds.Implementer);
    }

    /// <summary>
    /// THE REPO IS CHECKED BEFORE THE STAMP, which is F5's named trigger. `Start_Orchestration` and
    /// `Start_BasicOrchestration` both validate it; promotion did not, so a moved or renamed folder
    /// flipped the shape and then failed — leaving a "crew" holding one live solo.
    ///
    /// Asserted on the STATE afterwards, not just the throw: a guard that refuses AND flips is the
    /// defect with a message.
    /// </summary>
    [Fact]
    public void AMissingRepoRefusesWithoutFlippingTheShape()
    {
        var basic = _launcher.Start_BasicOrchestration("repo", _tempRepo);

        Directory.Delete(_tempRepo, recursive: true);

        Assert.Throws<Exception>(() => _launcher.Promote_ToFullCrew(basic.OrchId));

        var after = _store.Get_Session(basic.OrchId);

        Assert.True(OrchestrationShape.Is_BasicOrchestration(after.SupervisorSpawnedUtc));
        Assert.All(after.Members, member => Assert.Null(member.ClosedUtc));
    }

    /// <summary>The four states of the rule, so no branch rests on one example.</summary>
    [Theory]
    [InlineData(false, true, PromotionReadiness.Ready)]
    [InlineData(false, false, PromotionReadiness.NothingToPromote)]
    [InlineData(true, true, PromotionReadiness.Incomplete)]
    [InlineData(true, false, PromotionReadiness.AlreadyACrew)]
    public void TheReadinessRule(bool stamped, bool hasLiveSolo, PromotionReadiness expected)
    {
        Assert.Equal(expected, OrchestrationShape.Decide_PromotionReadiness(stamped ? DateTime.UtcNow : null, hasLiveSolo));
    }
}
