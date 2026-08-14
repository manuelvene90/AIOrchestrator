using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Sessions;

/// <summary>
/// rev-5 F6: writing a pid RE-OPENED a closed member, permanently.
///
/// `Set_MemberPid` rebuilt the member through the three-argument factory overload, which forwards
/// `closedUtc: null` — and `Respawn_Implementer`'s FIRST store write is exactly that call. So a
/// watchdog tick holding a `Load_All` snapshot from before a close, reaching that member after its
/// process died, resurrected it: open for ever, with nothing to heal it.
///
/// THE CODEBASE ALREADY KNEW. `Store_MemberTruePid_IfStillOpen` guards the LATER write with "a member
/// closed during the sync window must stay closed". The guard was applied to one write and not the
/// other, which is the same shape as every finding this branch has produced: the class was understood
/// and one instance of it was missed.
///
/// Promotion is simply the first flow that closes a member of a still-running orchestration from a
/// different loop, so it is the first to meet it.
/// </summary>
public class ClosedMemberStaysClosedTests : IDisposable
{
    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly RecordingSpawner_Fake _spawner;
    readonly IOrchestrationLauncher _launcher;

    public ClosedMemberStaysClosedTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-closedmember-tests-{Guid.NewGuid():N}");
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
    /// THE STORE WRITE ITSELF. Setting a pid is not a statement about whether a member is open, and
    /// this is asserted at the store rather than through the watchdog because every caller of it —
    /// present and future — depends on the same thing.
    /// </summary>
    [Fact]
    public void WritingAPidDoesNotReopenAClosedMember()
    {
        var full = _launcher.Start_Orchestration("repo", _tempRepo);

        _store.Close_Member(full.OrchId, "imp-1");

        _store.Set_MemberPid(full.OrchId, "imp-1", 12345);

        var member = Assert.Single(_store.Get_Session(full.OrchId).Members, m => m.MemberId == "imp-1");

        Assert.NotNull(member.ClosedUtc);
        Assert.Equal(12345, member.Pid);
    }

    /// <summary>
    /// AND THE BEHAVIOUR HALF: a respawn asked for a member that has since been closed opens no
    /// terminal. The store fix keeps the roster honest; this stops the app spawning a session it has
    /// already retired — which the owner would meet as a solo alive beside the supervisor that
    /// replaced it.
    ///
    /// Re-read inside the respawn rather than trusted from the caller, because the caller is a
    /// watchdog tick whose snapshot is exactly what went stale.
    /// </summary>
    [Fact]
    public void AClosedMemberIsNotRespawned()
    {
        var full = _launcher.Start_Orchestration("repo", _tempRepo);

        _store.Close_Member(full.OrchId, "imp-1");
        _spawner.SpawnedCommands.Clear();

        _launcher.Respawn_Implementer(full.OrchId, "imp-1");

        Assert.Empty(_spawner.SpawnedCommands);
        Assert.NotNull(Assert.Single(_store.Get_Session(full.OrchId).Members, m => m.MemberId == "imp-1").ClosedUtc);
    }

    /// <summary>
    /// A LIVE member still respawns — the case that would break if the guard were written as "never
    /// respawn", and the reason the watchdog exists at all.
    /// </summary>
    [Fact]
    public void ALiveMemberStillRespawns()
    {
        var full = _launcher.Start_Orchestration("repo", _tempRepo);

        _spawner.SpawnedCommands.Clear();
        _launcher.Respawn_Implementer(full.OrchId, "imp-1");

        Assert.NotEmpty(_spawner.SpawnedCommands);
    }
}
