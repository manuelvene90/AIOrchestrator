using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.Spawning.SessionSpawner;
using AIOrchestratorCoreLib.Spawning.SpawnCommand;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Launching;

/// <summary>
/// Guards the true-pid contract: session.json must NEVER carry the wt.exe delegator pid
/// Process.Start returns (a supervisor once read it, concluded a LIVE implementer was dead, and
/// retired it mid-work). The stored pid is null while spawning, then the pid the shell wrote into
/// its pid file.
/// </summary>
public class OrchestrationLauncherTests : IDisposable
{
    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly RecordingSpawner_Fake _spawner;
    readonly IOrchestrationLauncher _launcher;

    public OrchestrationLauncherTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-launcher-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _spawner = new RecordingSpawner_Fake();

        _launcher = OrchestrationLauncher_Factory.Create(
            _paths,
            OrchestratorConfigProvider_Factory.Create(_paths),
            _store,
            _spawner,
            OrchestrationLog_Factory.Create(_paths));
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void Start_Orchestration_StoresNullPids_NeverTheSpawnerPid()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);

        // Supervisor + communicator + the pre-spawned imp-1.
        Assert.Equal(3, _spawner.SpawnedCommands.Count);
        Assert.NotNull(session.CommunicatorSpawnedUtc);
        Assert.Null(session.SupervisorPid);
        Assert.Null(session.Members[0].Pid);
        Assert.NotNull(session.SupervisorSpawnedUtc);
        Assert.NotNull(session.Members[0].SpawnedUtc);
    }

    [Fact]
    public void TruePids_AreSyncedFromPidFiles_OnceTheShellsWriteThem()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var orchId = session.OrchId;

        // Simulate the spawned shells writing their own $PID (what really happens ~1 s in).
        File.WriteAllText(_paths.Get_SupervisorPidFile(orchId), "12345");
        File.WriteAllText(_paths.Get_ImplementerPidFile(orchId, "imp-1"), "23456");

        var synced = Wait_Until(() =>
        {
            var current = _store.Get_Session(orchId);
            return current.SupervisorPid == 12345 && current.Members[0].Pid == 23456;
        });

        Assert.True(synced, "true pids from the pid files were never synced into session.json");
    }

    [Fact]
    public void Respawn_Supervisor_DeletesTheStalePidFile_BeforeSpawning()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var pidFile = _paths.Get_SupervisorPidFile(session.OrchId);

        // A previous session's pid file must never be read as the NEW session's pid.
        File.WriteAllText(pidFile, "999");

        _launcher.Respawn_Supervisor(session.OrchId);

        Assert.False(File.Exists(pidFile));
        Assert.Null(_store.Get_Session(session.OrchId).SupervisorPid);
    }

    static bool Wait_Until(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
                return true;

            Thread.Sleep(100);
        }

        return false;
    }
}

/// <summary>Returns the delegator-style pid a real wt.exe spawn would — the value that must never land in session.json.</summary>
internal sealed class RecordingSpawner_Fake : ISessionSpawner
{
    public List<ISpawnCommand> SpawnedCommands { get; } = [];

    public int? Spawn(ISpawnCommand command)
    {
        SpawnedCommands.Add(command);
        return 77777;
    }
}
