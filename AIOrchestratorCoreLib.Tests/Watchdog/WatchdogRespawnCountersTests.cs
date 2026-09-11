using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Watchdog.SessionWatchdog;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Watchdog;

/// <summary>
/// The crash-loop counters the watchdog PERSISTS across an app restart (see
/// <see cref="ISessionWatchdog.Get_ConsecutiveRespawns"/>) — the counter is what turns "a session
/// died" into "a session cannot start", and it used to be reset to zero by every app restart, which
/// is the one event most likely to be happening while a machine-wide cause is crash-looping every
/// session at once.
///
/// Constructed exactly like <see cref="BasicOrchestrationWatchdogTests"/> — same real store, real
/// launcher, real log, only the process spawner faked — so this drives the real watchdog rather
/// than a stand-in.
/// </summary>
public class WatchdogRespawnCountersTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISessionWatchdog _watchdog;

    public WatchdogRespawnCountersTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-watchdog-counters-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        var paths = SupervisionPaths_Factory.Create(_tempRoot);
        var store = OrchestrationSessionStore_Factory.Create(paths);
        var spawner = new RecordingSpawner_Fake();
        var log = OrchestrationLog_Factory.Create(paths);

        // ONE provider for both, matching the sibling watchdog tests. The watchdog gained its
        // configProvider parameter on the integration side while this file was being written on
        // stage/3-durable-bridge, so the 4-argument call it shipped with is a merge artefact rather
        // than a disagreement about the API.
        var configProvider = OrchestratorConfigProvider_Factory.Create(paths);

        var launcher = OrchestrationLauncher_Factory.Create(paths, configProvider, store, spawner, log);

        _watchdog = SessionWatchdog_Factory.Create(paths, configProvider, store, launcher, log);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void AFreshWatchdog_HasNoConsecutiveRespawns()
    {
        Assert.Empty(_watchdog.Get_ConsecutiveRespawns());
    }

    [Fact]
    public void Restore_PutsCountersBack_AndGetReturnsThem()
    {
        _watchdog.Restore_ConsecutiveRespawns(new Dictionary<string, int> { ["sup:orch-1"] = 2, ["imp:orch-1/imp-1"] = 1 });

        var restored = _watchdog.Get_ConsecutiveRespawns();

        Assert.Equal(2, restored["sup:orch-1"]);
        Assert.Equal(1, restored["imp:orch-1/imp-1"]);
    }

    /// <summary>
    /// A restored ZERO carries no information — the slot is not failing — and keeping it would
    /// only keep a spent key alive in the persisted file for ever, since nothing ever removes a
    /// key that is present with a zero count.
    /// </summary>
    [Fact]
    public void ARestoredZero_IsDropped_RatherThanKept()
    {
        _watchdog.Restore_ConsecutiveRespawns(new Dictionary<string, int> { ["sup:orch-1"] = 0 });

        Assert.Empty(_watchdog.Get_ConsecutiveRespawns());
    }

    /// <summary>
    /// Restore is ADDITIVE, not a replace: it is called once at startup with whatever was
    /// persisted, and must not erase any counter the watchdog has already accumulated in this
    /// process's lifetime (e.g. a slot that had already failed a check before the restore ran).
    /// </summary>
    [Fact]
    public void Restore_IsAdditive_AndDoesNotWipeCountersAlreadyHeld()
    {
        _watchdog.Restore_ConsecutiveRespawns(new Dictionary<string, int> { ["sup:orch-1"] = 1 });
        _watchdog.Restore_ConsecutiveRespawns(new Dictionary<string, int> { ["sup:orch-2"] = 1 });

        var restored = _watchdog.Get_ConsecutiveRespawns();

        Assert.Equal(1, restored["sup:orch-1"]);
        Assert.Equal(1, restored["sup:orch-2"]);
    }

    /// <summary>
    /// Get MUST return a COPY. If it returned the watchdog's own dictionary, a caller mutating the
    /// result (the bridge serializing it, say) could silently reset or corrupt a live crash-loop
    /// counter without the watchdog ever knowing its own state had changed.
    /// </summary>
    [Fact]
    public void Get_ReturnsACopy_MutatingItDoesNotChangeTheWatchdogsOwnState()
    {
        _watchdog.Restore_ConsecutiveRespawns(new Dictionary<string, int> { ["sup:orch-1"] = 1 });

        var firstRead = _watchdog.Get_ConsecutiveRespawns();
        ((Dictionary<string, int>)firstRead)["sup:orch-1"] = 999;
        ((Dictionary<string, int>)firstRead)["sup:orch-2"] = 5;

        var secondRead = _watchdog.Get_ConsecutiveRespawns();

        Assert.Equal(1, secondRead["sup:orch-1"]);
        Assert.False(secondRead.ContainsKey("sup:orch-2"));
    }
}
