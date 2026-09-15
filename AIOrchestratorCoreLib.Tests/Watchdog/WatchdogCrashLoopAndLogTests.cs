using System.Diagnostics;
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Kit;
using AIOrchestratorCoreLib.Kit.PluginGate;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using AIOrchestratorCoreLib.Watchdog.SessionWatchdog;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Watchdog;

/// <summary>
/// THE APP-START LOG OF 2026-09-14, which the owner called a disaster, and it was three defects at once.
///
/// The kit was missing, so the host refused every spawn at 14:20 and 14:26. Once it was installed the
/// general supervisor, strategy-lab-25's solo and ai-orchestrator-26's solo all came up cleanly, and
/// all three were announced as CRASH-LOOPING — "3 respawns without coming alive":
///
/// 1. A REFUSED spawn counted. No process was started, so there was nothing to come alive; the
///    refusal has its own error line already. Two refusals plus the first real start made three.
/// 2. A LIVE session was never SEEN live inside the 90-second spawn grace, because the grace returned
///    before the liveness check. ai-orchestrator-26's solo was restarted at 14:29:31 and the app was
///    restarted ~100 s later, so its count was carried, restored, and tipped over at 14:31:12.
/// 3. Every start of the app printed an orange "Implementer 'solo-1' session not running — respawning
///    (a solo resumes its own conversation if …)" beside the launcher's own "Solo 'solo-1' session
///    spawned — resuming conversation …": the wrong kind, a paragraph of design notes, and a warning
///    for the one death the app caused itself, by killing every session on exit.
///
/// Same construction as <see cref="BasicOrchestrationWatchdogTests"/> — real store, launcher, paths;
/// only the process spawner is faked, and the log is recorded so the lines themselves are asserted.
/// </summary>
public class WatchdogCrashLoopAndLogTests : IDisposable
{
    const string SOLO_ID = "solo-1";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly RecordingLog _log = new();
    readonly List<Process> _shells = [];

    public WatchdogCrashLoopAndLogTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-watchdog-log-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        _store = OrchestrationSessionStore_Factory.Create(_paths);
    }

    public void Dispose()
    {
        foreach (var shell in _shells)
        {
            try { shell.Kill(entireProcessTree: true); } catch { }
            shell.Dispose();
        }

        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// DEFECT 1. The slot already carries two failed respawns; the third attempt is REFUSED by the kit
    /// gate, so no session was started and there is nothing that failed to come alive. The counter
    /// must not move and no crash-loop alert may be raised.
    /// </summary>
    [Fact]
    public void ARefusedSpawn_DoesNotCountTowardsACrashLoop()
    {
        var refusing = PluginGate_Factory.Create();
        refusing.Record(PluginVerdicts.NotInstalled, "kit not installed (test)");

        var (watchdog, orchId) = Create_WithBasicOrchestration(refusing);
        var slot = $"imp:{orchId}/{SOLO_ID}";

        Age_MemberSpawn(orchId);
        watchdog.Restore_ConsecutiveRespawns(new Dictionary<string, int> { [slot] = 2 });

        watchdog.Check_AndRestart_DeadSessions();

        Assert.Equal(2, watchdog.Get_ConsecutiveRespawns()[slot]);
        Assert.Empty(watchdog.Take_PendingCrashLoopAlerts());
        Assert.DoesNotContain(_log.Entries, entry => entry.Message.Contains("CRASH-LOOPING"));
    }

    /// <summary>
    /// THE CONTROL for defect 1, because "no alert" has two routes to it: the same slot, the same two
    /// prior failures, a gate that ALLOWS — and the third respawn must raise the alert. Without this,
    /// the test above would pass just as well if the watchdog never counted at all.
    /// </summary>
    [Fact]
    public void AnAllowedSpawn_StillCountsTowardsACrashLoop()
    {
        var (watchdog, orchId) = Create_WithBasicOrchestration(PluginGate_Factory.Create_Allowing());
        var slot = $"imp:{orchId}/{SOLO_ID}";

        Age_MemberSpawn(orchId);
        watchdog.Restore_ConsecutiveRespawns(new Dictionary<string, int> { [slot] = 2 });

        watchdog.Check_AndRestart_DeadSessions();

        Assert.Equal(3, watchdog.Get_ConsecutiveRespawns()[slot]);
        Assert.Single(watchdog.Take_PendingCrashLoopAlerts());
    }

    /// <summary>
    /// DEFECT 2. The session was spawned seconds ago — inside the grace — and it IS alive: its pid
    /// file names a running shell. Being alive is exactly what clears the counter, and it must be
    /// observed whether or not the grace is still running; otherwise an app restart inside the grace
    /// carries a healthy slot's count forward.
    /// </summary>
    [RequiresLiveShellFact]
    public void ALiveSessionInsideTheSpawnGrace_ClearsItsCounter()
    {
        var (watchdog, orchId) = Create_WithBasicOrchestration(PluginGate_Factory.Create_Allowing());
        var slot = $"imp:{orchId}/{SOLO_ID}";

        Write_PidFile_OfALiveShell(_paths.Get_ImplementerPidFile(orchId, SOLO_ID));
        watchdog.Restore_ConsecutiveRespawns(new Dictionary<string, int> { [slot] = 2 });

        watchdog.Check_AndRestart_DeadSessions();

        Assert.False(watchdog.Get_ConsecutiveRespawns().ContainsKey(slot));
    }

    /// <summary>
    /// DEFECT 3, the app-start half. Every session is down when the app starts, because the app killed
    /// them on exit. Restoring them is the design, not a fault, so the first pass says nothing of its
    /// own: the launcher's "Solo 'solo-1' session spawned — …" line is the record. The respawn itself
    /// still happens — asserted, so this cannot pass by the watchdog doing nothing.
    /// </summary>
    [Fact]
    public void TheFirstPass_RestoresSessionsWithoutAWarning()
    {
        var spawner = new RecordingSpawner_Fake();
        var (watchdog, orchId) = Create_WithBasicOrchestration(PluginGate_Factory.Create_Allowing(), spawner);

        Age_MemberSpawn(orchId);
        spawner.SpawnedCommands.Clear();

        watchdog.Check_AndRestart_DeadSessions();

        Assert.NotEmpty(spawner.SpawnedCommands);
        Assert.DoesNotContain(_log.Entries, entry => entry.Level == LogLevels.Warning && entry.Message.Contains("not running"));
        Assert.Contains(_log.Entries, entry => entry.Message.StartsWith($"Solo '{SOLO_ID}' session spawned"));
    }

    /// <summary>
    /// DEFECT 3, the running half. A session that dies WHILE the app runs is a fault worth a warning —
    /// named by its real kind, and without the paragraph of design notes.
    /// </summary>
    [Fact]
    public void ADeathWhileRunning_IsWarnedWithTheMembersRealKind()
    {
        var (watchdog, orchId) = Create_WithBasicOrchestration(PluginGate_Factory.Create_Allowing());

        // First pass inside the grace: nothing is respawned, and the app-start pass is spent.
        watchdog.Check_AndRestart_DeadSessions();

        Age_MemberSpawn(orchId);
        watchdog.Check_AndRestart_DeadSessions();

        var warning = Assert.Single(_log.Entries, entry => entry.Level == LogLevels.Warning && entry.Message.Contains(SOLO_ID));
        Assert.Equal($"Solo '{SOLO_ID}' session not running — respawning", warning.Message);
    }

    (ISessionWatchdog Watchdog, string OrchId) Create_WithBasicOrchestration(IPluginGate gate, RecordingSpawner_Fake? spawner = null)
    {
        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, spawner ?? new RecordingSpawner_Fake(), _log, gate);
        var session = launcher.Start_BasicOrchestration("repo", _tempRepo);

        _log.Entries.Clear();

        return (SessionWatchdog_Factory.Create(_paths, configProvider, _store, launcher, _log), session.OrchId);
    }

    /// <summary>Past the 90-second spawn grace, so a missing pid file counts as dead.</summary>
    void Age_MemberSpawn(string orchId)
    {
        var file = _paths.Get_SessionFile(orchId);
        var root = JsonNode.Parse(File.ReadAllText(file))!.AsObject();

        foreach (var member in root["members"]!.AsArray())
            member!["spawnedUtc"] = DateTime.UtcNow.AddHours(-2).ToString("O");

        File.WriteAllText(file, root.ToJsonString());
    }

    /// <summary>
    /// The watchdog only believes a pid that is a PowerShell shell (Windows recycles pids), so the
    /// fixture has to be one. A machine that has neither shell cannot run this test, and says so
    /// rather than passing on a pid that never meant anything (decision 20).
    /// </summary>
    void Write_PidFile_OfALiveShell(string pidFile)
    {
        foreach (var shell in new[] { "powershell", "pwsh" })
        {
            try
            {
                var process = Process.Start(new ProcessStartInfo(shell, "-NoProfile -Command Start-Sleep -Seconds 60")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                })!;

                _shells.Add(process);
                Directory.CreateDirectory(Path.GetDirectoryName(pidFile)!);
                File.WriteAllText(pidFile, process.Id.ToString());
                return;
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }

        throw new InvalidOperationException("Neither powershell nor pwsh could be started — this test needs a live shell pid and cannot run here.");
    }

    sealed class RecordingLog : IOrchestrationLog
    {
        public List<(LogLevels Level, string Message)> Entries { get; } = [];

        public void Log_Info(string orchId, string message) => Entries.Add((LogLevels.Info, message));
        public void Log_Warning(string orchId, string message) => Entries.Add((LogLevels.Warning, message));
        public void Log_Error(string orchId, string message, Exception? exception) => Entries.Add((LogLevels.Error, message));

        public event Action<IOrchestrationLogEntry>? EntryLogged { add { } remove { } }
    }
}
