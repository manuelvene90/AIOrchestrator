using System.Text;
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Running.SessionRunner;
using AIOrchestratorCoreLib.Sessions;
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

    /// <summary>
    /// THE ROLE DEFAULT MOVED FROM THE BUILDER TO THE CONFIG (2026-09-12, spec §6.4). The emitted
    /// command line is byte-identical under `classic`, which is the point: what changed is WHERE the
    /// xhigh is decided, so a brother on `quiet` can have none without a rebuild. The launcher is the
    /// place because it is the one that already holds both the per-orchestration override and the
    /// config provider; the builder holds neither and had to be handed a constant.
    /// </summary>
    [Fact]
    public void Spawn_TakesTheRoleEffortFromTheConfig_NotFromTheCommandBuilder()
    {
        _launcher.Start_Orchestration("Repo", _tempRepo);

        Assert.Contains("--effort xhigh ", SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[0]));
        Assert.DoesNotContain("--effort", SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[1]));
        Assert.DoesNotContain("--effort", SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[2]));
    }

    /// <summary>
    /// The stored effort override BEATS the role default — and with none stored, the role default the
    /// CONFIG resolves is what a fresh orchestration spawns with (xhigh for the supervisor under
    /// `classic`, nothing for the members), no longer a constant inside SpawnCommand_Builder.
    /// The expected value is a LITERAL here rather than a catalogue read: this case is about the
    /// launcher passing through what the config said, and re-deriving the expectation from the same
    /// source the production code reads would make it assert nothing.
    /// </summary>
    [Fact]
    public void Respawn_PassesTheStoredEffortOverride_ToTheSupervisorAndToEveryMemberKind()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var orchId = session.OrchId;

        // Supervisor + imp-1 + rev-1, none with an override yet: the supervisor gets its ROLE
        // DEFAULT (xhigh, owner directive 2026-09-09), the members no flag at all.
        Assert.Equal(3, _spawner.SpawnedCommands.Count);
        Assert.Contains("--effort xhigh ", SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[0]));
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
        Assert.Contains("--effort xhigh ", resetScript);
        Assert.DoesNotContain("medium", resetScript);
    }

    /// <summary>
    /// THE COMMUNICATOR AND THE GENERAL SUPERVISOR REACH THE COMMAND LINE TOO (fix round 1, 2026-09-12).
    /// The launcher resolved <c>effort.communicator</c> and <c>effort.general</c> onto the launch from
    /// the day the block existed, and the terminal runner then dropped both on the floor: neither
    /// <see cref="SpawnCommand_Builder.Build_ForCommunicator"/> nor
    /// <see cref="SpawnCommand_Builder.Build_ForGeneralSupervisor"/> took an effort at all, so an owner
    /// who wrote the keys got no flag, no warning and no log line — the silent discard CLAUDE.md
    /// decision 21 refuses. Both shipped presets leave these two roles null, which is exactly why this
    /// needs a config that STATES them: a default of null is a second route to green.
    /// </summary>
    [Fact]
    public void Spawn_CarriesTheCommunicatorAndGeneralRoleEfforts_OntoTheirCommandLines()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"effort":{"communicator":"max","general":"high"}}""");

        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _spawner.SpawnedCommands.Clear();

        _launcher.Respawn_Communicator(session.OrchId);
        _launcher.Spawn_GeneralSupervisor();

        Assert.Contains($"--effort max {SpawnCommand_Builder.CLAUDE_LAUNCH_FLAGS} '/communicator {session.OrchId}'", SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[0]));
        Assert.Contains($"--effort high {SpawnCommand_Builder.CLAUDE_LAUNCH_FLAGS} '/general-supervisor'", SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[1]));
    }

    /// <summary>
    /// A BLANK OVERRIDE IS ABSENT, so the ROLE DEFAULT still applies (fix round 1, 2026-09-12). The
    /// deleted <c>SpawnCommand_Builder.Resolve_Effort_OrDefault</c> said so with
    /// <c>IsNullOrWhiteSpace</c>; the launcher's <c>??</c> catches null only, so a hand-edited
    /// <c>"supervisorEffortOverride": ""</c> — which <c>SessionJson_Serializer.Get_String_OrNull</c>
    /// returns verbatim — used to give the supervisor its xhigh and now gave it no flag at all. The
    /// deciding reason is not reachability but AGREEMENT: <c>SessionScoped_Reader</c> maps these
    /// same two overrides with blank-is-absent, and two descriptions of one precedence disagreeing on
    /// one input is the drift CLAUDE.md decision 12 forbids.
    ///
    /// <para>
    /// TWO GUARDS AGAINST A SECOND ROUTE TO GREEN (decision 20). The blank is read back from the store
    /// before the respawn, so a store or serializer that normalised it away could not let this case
    /// pass without ever putting a blank in front of the launcher. And the implementer's role default
    /// is STATED in config.json rather than left at the catalogue's null: asserting "no flag" for it
    /// would pass either way, because <c>Build_ClaudeInvocation</c> also suppresses a blank — the
    /// blank has to fall through to a level that is VISIBLE on the line for the assertion to pin the
    /// launcher's reading of it.
    /// </para>
    /// </summary>
    [Fact]
    public void ABlankEffortOverride_IsAbsent_SoTheRoleDefaultStillApplies()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"effort":{"implementer":"low"}}""");

        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var orchId = session.OrchId;

        _store.Set_SupervisorEffortOverride(orchId, "");
        _store.Set_ImplementerEffortOverride(orchId, "   ");

        var stored = _store.Get_Session(orchId);
        Assert.Equal("", stored.SupervisorEffortOverride);
        Assert.Equal("   ", stored.ImplementerEffortOverride);

        _spawner.SpawnedCommands.Clear();

        _launcher.Respawn_Supervisor(orchId);
        _launcher.Respawn_Implementer(orchId, "imp-1");

        // The supervisor's role default is xhigh (classic, which an `effort` block naming only the
        // implementer leaves standing); the implementer's is the `low` this config states.
        Assert.Contains($"--effort xhigh {SpawnCommand_Builder.CLAUDE_LAUNCH_FLAGS} '/supervisor {orchId}'", SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[0]));
        Assert.Contains($"--effort low {SpawnCommand_Builder.CLAUDE_LAUNCH_FLAGS} '/implementer {orchId}/imp-1'", SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[1]));
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

    [Fact]
    public void ASupervisorConfiguredForThePrintRunner_IsNeverResumedByTheLauncher()
    {
        // The bridge-driven runners keep their own ResumeModes (print-session.json); the launcher's
        // --resume is the TERMINAL runner's only. Writing a probe file for a print supervisor must not
        // make the launcher hand a resume id to a runner that ignores it.
        var (launcher, runner) = Launcher_WithRunnerFor(SessionRoles.Supervisor, SessionRunners.Print);
        var session = launcher.Start_Orchestration("Repo", _tempRepo);

        Write_ProbeFile(
            Path.Combine(_paths.Get_OrchestrationFolder(session.OrchId), UsageTotals_Reader.SESSION_USAGE_FILE),
            SUPERVISOR_SESSION_ID,
            Write_Transcript(SUPERVISOR_SESSION_ID));

        launcher.Respawn_Supervisor(session.OrchId);

        Assert.Equal(SessionRoles.Supervisor, runner.Launches[^1].Role);
        Assert.Null(runner.Launches[^1].ResumeSessionId);
    }

    /// <summary>
    /// A launcher whose <paramref name="role"/> is configured for <paramref name="kind"/>, over a
    /// runner that RECORDS the launch instead of starting anything. The launch is where a
    /// bridge-driven decision becomes observable at all: that runner never builds a command line, so
    /// there is no script to decode and the assertion has to be on what the runner was handed.
    /// </summary>
    (IOrchestrationLauncher Launcher, RecordingRunner_Fake Runner) Launcher_WithRunnerFor(SessionRoles role, SessionRunners kind)
    {
        var config = new JsonObject
        {
            ["repos"] = new JsonArray(),
            ["runners"] = new JsonObject
            {
                [SessionRole_Names.Get_ConfigKey(role)] = new JsonObject { ["runner"] = SessionRunner_Names.Get_Word(kind) },
            },
        };

        File.WriteAllText(_paths.ConfigFile, config.ToJsonString());

        var runner = new RecordingRunner_Fake(kind);

        var launcher = OrchestrationLauncher_Factory.Create(
            _paths,
            OrchestratorConfigProvider_Factory.Create(_paths),
            _store,
            [SessionRunner_Factory.Create_Terminal(_spawner), runner],
            OrchestrationLog_Factory.Create(_paths));

        return (launcher, runner);
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

/// <summary>
/// Records the launch it is handed instead of starting anything. The SEAM's own input is what a
/// bridge-driven decision can be asserted on: those runners build no command line, so the script
/// the other cases decode does not exist for them.
/// </summary>
internal sealed class RecordingRunner_Fake(SessionRunners kind) : ISessionRunner
{
    public SessionRunners Kind { get; } = kind;
    public List<ISessionLaunch> Launches { get; } = [];

    public void Start(ISessionLaunch launch)
    {
        Launches.Add(launch);
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
