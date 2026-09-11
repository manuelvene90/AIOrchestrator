using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions;
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

    /// <summary>
    /// Three tiers: the owner's set-model for the orchestration, then the model the supervisor asked
    /// for when it requested the member, then the config default. The member's model is on the
    /// record, so a respawn keeps the size the task was given; the owner's override still wins.
    /// </summary>
    [Fact]
    public void Add_Member_SpawnsOnTheRequestedModel_KeepsItAcrossRespawn_AndYieldsToTheOwnersOverride()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _spawner.SpawnedCommands.Clear();

        var withMember = _launcher.Add_Member(session.OrchId, MemberKinds.Implementer, "sonnet");
        var memberId = withMember.Members[^1].MemberId;

        Assert.Equal("sonnet", withMember.Members[^1].Model);
        Assert.Contains("sonnet", Join(_spawner.SpawnedCommands[^1]));

        // Reloaded from disk, not from memory: the model survives the session file.
        Assert.Equal("sonnet", _store.Get_Session(session.OrchId).Members.Single(m => m.MemberId == memberId).Model);

        _spawner.SpawnedCommands.Clear();
        _launcher.Respawn_Implementer(session.OrchId, memberId);
        Assert.Contains("sonnet", Join(_spawner.SpawnedCommands[^1]));

        _store.Set_ImplementerModelOverride(session.OrchId, "opus");
        _spawner.SpawnedCommands.Clear();
        _launcher.Respawn_Implementer(session.OrchId, memberId);

        var command = Join(_spawner.SpawnedCommands[^1]);
        Assert.Contains("opus", command);
        Assert.DoesNotContain("sonnet", command);
    }

    /// <summary>
    /// THE REVIEWER NO LONGER RIDES THE IMPLEMENTER'S DEFAULT (owner 2026-09-09). Pinned at the
    /// launcher because that is the point of effect: the config split is worth nothing if the spawn
    /// still reaches for one key for every member that is not a supervisor. Three distinct models so
    /// no assertion can pass by coincidence.
    /// </summary>
    [Fact]
    public void Start_Orchestration_SpawnsTheReviewerOnTheReviewerModel_NotTheImplementersOne()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"supervisorModel":"opus","implementerModel":"sonnet","reviewerModel":"haiku"}""");

        var session = _launcher.Start_Orchestration("Repo", _tempRepo);

        // Spawn order is supervisor, imp-1, rev-1 (Start_Orchestration).
        Assert.Equal(3, _spawner.SpawnedCommands.Count);

        var implementer = Join(_spawner.SpawnedCommands[1]);
        var reviewer = Join(_spawner.SpawnedCommands[2]);

        Assert.Contains("--model sonnet", implementer);
        Assert.Contains("--model haiku", reviewer);

        Assert.Equal("rev-1", session.Members[^1].MemberId);
    }

    /// <summary>
    /// ...and with no reviewerModel key, the reviewer keeps taking the implementer's — the ladder
    /// that makes this change invisible on an existing box. A solo takes the same route, through the
    /// same call, which is why one config covers both.
    /// </summary>
    [Fact]
    public void WithNoReviewerModelKey_TheReviewerStillTakesTheImplementerModel()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"implementerModel":"haiku"}""");

        _launcher.Start_Orchestration("Repo", _tempRepo);

        Assert.Contains("--model haiku", Join(_spawner.SpawnedCommands[2]));
    }

    /// <summary>
    /// AN EMPTY KEY IS ABSENT AT THE POINT OF EFFECT, which is where it had to be pinned: the defect
    /// was invisible in the config object and only showed on the command line. Proven 2026-09-10,
    /// before the fix, on <c>{"implementerModel":"sonnet","reviewerModel":""}</c> — <c>??</c> does not
    /// catch the empty string, and <see cref="Spawning.SpawnCommand_Builder"/> adds <c>--model</c>
    /// only for a non-whitespace value, so the reviewer was spawned with NO model flag at all: the
    /// CLI's own default, neither the ladder's answer nor the app's, and nothing anywhere said so.
    /// After the fix, the empty key is absent everywhere in the ladder, so the reviewer takes the
    /// implementer's model exactly as an unset reviewerModel would.
    /// </summary>
    [Fact]
    public void WithAnEmptyReviewerModel_TheReviewerTakesTheLadder_RatherThanNoModelFlagAtAll()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"implementerModel":"sonnet","reviewerModel":""}""");

        _launcher.Start_Orchestration("Repo", _tempRepo);

        var reviewer = Join(_spawner.SpawnedCommands[2]);

        Assert.Contains("--model sonnet", reviewer);
    }

    /// <summary>A basic orchestration's one session takes the solo default, by the same one reader.</summary>
    [Fact]
    public void Start_BasicOrchestration_SpawnsTheSoloOnTheSoloModel()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"implementerModel":"sonnet","soloModel":"opus"}""");

        _launcher.Start_BasicOrchestration("Repo", _tempRepo);

        Assert.Contains("--model opus", Join(_spawner.SpawnedCommands[^1]));
    }

    /// <summary>The claude invocation travels base64-encoded inside the terminal script; read it decoded.</summary>
    static string Join(ISpawnCommand command)
    {
        return AIOrchestratorCoreLib.Spawning.SpawnCommand_Builder.Decode_SessionScript(command);
    }

    [Fact]
    public void Start_Orchestration_StoresNullPids_NeverTheSpawnerPid()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);

        // Supervisor + the pre-spawned imp-1 and rev-1. NO communicator — the role is retired.
        Assert.Equal(3, _spawner.SpawnedCommands.Count);
        Assert.Null(session.CommunicatorSpawnedUtc);
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
