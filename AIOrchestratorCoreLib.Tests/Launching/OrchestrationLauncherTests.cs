using System.Text;
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.Spawning;
using AIOrchestratorCoreLib.Spawning.SessionSpawner;
using AIOrchestratorCoreLib.Spawning.SpawnCommand;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Usage;
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

    /// <summary>
    /// The stored effort override is the ONLY source of `--effort` — there is no config default,
    /// so a fresh orchestration spawns without the flag and a respawn after the owner set one
    /// carries it: the supervisor's own, and the implementer-side one for every member kind.
    /// </summary>
    [Fact]
    public void Respawn_PassesTheStoredEffortOverride_ToTheSupervisorAndToEveryMemberKind()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var orchId = session.OrchId;

        // Supervisor + imp-1 + rev-1, none with an override yet: the supervisor gets its ROLE
        // DEFAULT (xhigh, owner directive 2026-09-09), the members no flag at all.
        Assert.Equal(3, _spawner.SpawnedCommands.Count);
        Assert.Contains($"--effort {SpawnCommand_Builder.SUPERVISION_EFFORT_LEVEL} ", SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[0]));
        Assert.DoesNotContain("--effort", SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[1]));
        Assert.DoesNotContain("--effort", SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[2]));

        _store.Set_SupervisorEffortOverride(orchId, "medium");
        _store.Set_ImplementerEffortOverride(orchId, "low");
        _spawner.SpawnedCommands.Clear();

        _launcher.Respawn_Supervisor(orchId);
        _launcher.Respawn_Implementer(orchId, "imp-1");
        _launcher.Respawn_Implementer(orchId, "rev-1");

        var supervisorScript = SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[0]);
        var implementerScript = SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[1]);
        var reviewerScript = SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[2]);

        Assert.Contains($"--effort medium {SpawnCommand_Builder.CLAUDE_LAUNCH_FLAGS} '/supervisor {orchId}'", supervisorScript);
        Assert.Contains($"--effort low {SpawnCommand_Builder.CLAUDE_LAUNCH_FLAGS} '/implementer {orchId}/imp-1'", implementerScript);
        Assert.Contains($"--effort low {SpawnCommand_Builder.CLAUDE_LAUNCH_FLAGS} {SpawnCommand_Builder.REVIEWER_LAUNCH_FLAGS} -- '/reviewer {orchId}/rev-1'", reviewerScript);

        // And a reset goes back to the ROLE DEFAULT, rather than leaving the last value baked in.
        _store.Set_SupervisorEffortOverride(orchId, null);
        _spawner.SpawnedCommands.Clear();

        _launcher.Respawn_Supervisor(orchId);

        var resetScript = SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[0]);
        Assert.Contains($"--effort {SpawnCommand_Builder.SUPERVISION_EFFORT_LEVEL} ", resetScript);
        Assert.DoesNotContain("medium", resetScript);
    }

    const string SUPERVISOR_SESSION_ID = "11111111-2222-4333-8444-555555555555";
    const string SOLO_SESSION_ID = "66666666-7777-4888-8999-aaaaaaaaaaaa";

    /// <summary>
    /// Owner request 2026-09-10: a restarted supervisor continues its OWN conversation. The first
    /// spawn has no probe file (the statusline has never rendered) and starts fresh; once the
    /// session's probe names a transcript that exists, every respawn — watchdog, /model, /effort,
    /// app restart — passes that id to `claude --resume`. The id is read BEFORE the spawn: the new
    /// process overwrites the probe on its first render.
    /// </summary>
    [Fact]
    public void Respawn_Supervisor_ResumesItsOwnConversation_OnceItsProbeFileNamesALiveTranscript()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var orchId = session.OrchId;

        Assert.DoesNotContain("--resume", SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[0]));

        Write_ProbeFile(
            Path.Combine(_paths.Get_OrchestrationFolder(orchId), UsageTotals_Reader.SESSION_USAGE_FILE),
            SUPERVISOR_SESSION_ID,
            Write_Transcript(SUPERVISOR_SESSION_ID));
        _spawner.SpawnedCommands.Clear();

        _launcher.Respawn_Supervisor(orchId);

        var script = SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[0]);
        Assert.Contains($"claude --resume {SUPERVISOR_SESSION_ID} ", script);
        Assert.Contains($"{SpawnCommand_Builder.CLAUDE_LAUNCH_FLAGS} '/supervisor {orchId}'", script);
    }

    /// <summary>
    /// The solo is the other role the owner named, and its probe lives in ITS member folder. An
    /// implementer is NOT resumed even when its probe names a live transcript: the owner asked for
    /// solo and supervisor, and implementers keep re-entering through their role command.
    /// </summary>
    [Fact]
    public void Respawn_Solo_ResumesItsOwnConversation_AndAnImplementerNeverDoes()
    {
        var basic = _launcher.Start_BasicOrchestration("Repo", _tempRepo);
        var crew = _launcher.Start_Orchestration("Repo", _tempRepo);

        Write_ProbeFile(
            Path.Combine(_paths.Get_ImplementerFolder(basic.OrchId, "solo-1"), UsageTotals_Reader.SESSION_USAGE_FILE),
            SOLO_SESSION_ID,
            Write_Transcript(SOLO_SESSION_ID));
        Write_ProbeFile(
            Path.Combine(_paths.Get_ImplementerFolder(crew.OrchId, "imp-1"), UsageTotals_Reader.SESSION_USAGE_FILE),
            SUPERVISOR_SESSION_ID,
            Write_Transcript(SUPERVISOR_SESSION_ID));
        _spawner.SpawnedCommands.Clear();

        _launcher.Respawn_Implementer(basic.OrchId, "solo-1");
        _launcher.Respawn_Implementer(crew.OrchId, "imp-1");

        var soloScript = SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[0]);
        var implementerScript = SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[1]);

        Assert.Contains($"claude --resume {SOLO_SESSION_ID} ", soloScript);
        Assert.Contains($"{SpawnCommand_Builder.CLAUDE_LAUNCH_FLAGS} '/solo {basic.OrchId}'", soloScript);
        Assert.DoesNotContain("--resume", implementerScript);
    }

    /// <summary>
    /// `claude --resume` of an id whose transcript is gone prints "No conversation found" and exits,
    /// and the watchdog would respawn it into the same wall. A stale probe therefore means FRESH.
    /// </summary>
    [Fact]
    public void Respawn_Supervisor_StartsFresh_WhenTheTranscriptTheProbeNamesIsGone()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var orchId = session.OrchId;

        Write_ProbeFile(
            Path.Combine(_paths.Get_OrchestrationFolder(orchId), UsageTotals_Reader.SESSION_USAGE_FILE),
            SUPERVISOR_SESSION_ID,
            Path.Combine(_tempRoot, "projects", "gone.jsonl"));
        _spawner.SpawnedCommands.Clear();

        _launcher.Respawn_Supervisor(orchId);

        var script = SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[0]);
        Assert.DoesNotContain("--resume", script);
        Assert.Contains($"{SpawnCommand_Builder.CLAUDE_LAUNCH_FLAGS} '/supervisor {orchId}'", script);
    }

    string Write_Transcript(string sessionId)
    {
        var transcript = Path.Combine(_tempRoot, "projects", $"{sessionId}.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(transcript) ?? throw new Exception($"Transcript path '{transcript}' has no directory"));
        File.WriteAllText(transcript, """{"type":"summary","summary":"a conversation"}""" + "\n");
        return transcript;
    }

    /// <summary>The live probe shape trimmed to what the respawn reads, written with the BOM the statusline's Set-Content leaves.</summary>
    static void Write_ProbeFile(string probeFile, string sessionId, string transcriptPath)
    {
        var payload = new JsonObject
        {
            ["session_id"] = sessionId,
            ["transcript_path"] = transcriptPath,
            ["model"] = new JsonObject { ["id"] = "claude-fable-5-1", ["display_name"] = "Fable 5.1" },
        };

        File.WriteAllText(probeFile, payload.ToJsonString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
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
