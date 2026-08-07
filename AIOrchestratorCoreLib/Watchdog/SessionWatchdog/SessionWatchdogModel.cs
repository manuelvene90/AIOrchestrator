using System.Diagnostics;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Watchdog.SessionWatchdog;

internal sealed class SessionWatchdogModel(
    ISupervisionPaths paths,
    IOrchestrationSessionStore store,
    IOrchestrationLauncher launcher,
    IOrchestrationLog log) : ISessionWatchdog
{
    /// <summary>Grace between respawns of the same slot — covers shell startup + pid-file write.</summary>
    const int RESPAWN_BACKOFF_SECONDS = 45;

    /// <summary>
    /// Grace after ANY spawn (launcher-initiated included) before a missing pid file counts as
    /// dead. Without this, the tick right after a launcher spawn saw no pid file yet and spawned
    /// a DUPLICATE (the launcher never claims the watchdog's own backoff slot).
    /// </summary>
    const int SPAWN_GRACE_SECONDS = 90;

    static readonly IReadOnlySet<string> SHELL_PROCESS_NAMES = new HashSet<string> { "powershell", "pwsh" };

    readonly ISupervisionPaths _paths = paths;
    readonly IOrchestrationSessionStore _store = store;
    readonly IOrchestrationLauncher _launcher = launcher;
    readonly IOrchestrationLog _log = log;
    /// <summary>Consecutive respawns of a slot with NO observed liveness in between — 3 = crash loop.</summary>
    const int CRASH_LOOP_THRESHOLD = 3;

    readonly Dictionary<string, DateTime> _lastRespawnUtc = [];
    readonly Dictionary<string, int> _consecutiveRespawns = [];
    readonly List<(string OrchId, string AlertText)> _pendingCrashLoopAlerts = [];

    public void Check_AndRestart_DeadSessions()
    {
        Check_GeneralSupervisor();

        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null)
                continue;

            Check_OrchestrationSupervisor(session);
            Check_Communicator(session);

            foreach (var member in session.Members)
            {
                if (member.ClosedUtc == null)
                    Check_Implementer(session.OrchId, member.MemberId, member.SpawnedUtc);
            }
        }
    }

    static bool Is_WithinSpawnGrace(DateTime? spawnedUtc)
    {
        return spawnedUtc != null && (DateTime.UtcNow - spawnedUtc.Value).TotalSeconds < SPAWN_GRACE_SECONDS;
    }

    public IReadOnlyList<(string OrchId, string AlertText)> Take_PendingCrashLoopAlerts()
    {
        if (_pendingCrashLoopAlerts.Count == 0)
            return [];

        List<(string OrchId, string AlertText)> drained = [.. _pendingCrashLoopAlerts];
        _pendingCrashLoopAlerts.Clear();
        return drained;
    }

    void Check_GeneralSupervisor()
    {
        if (Is_SessionAlive(_paths.GeneralPidFile))
        {
            _consecutiveRespawns.Remove("general");
            return;
        }

        if (!Try_ClaimRespawnSlot("general"))
            return;

        Register_Respawn("general", ChannelDiscovery.GENERAL_ORCH_ID, "general supervisor");
        _log.Log_Warning(ChannelDiscovery.GENERAL_ORCH_ID, "General supervisor not running — spawning it");
        _launcher.Spawn_GeneralSupervisor();
    }

    void Check_OrchestrationSupervisor(Sessions.OrchestrationSession.IOrchestrationSession session)
    {
        if (Is_WithinSpawnGrace(session.SupervisorSpawnedUtc))
            return;

        if (Is_SessionAlive(_paths.Get_SupervisorPidFile(session.OrchId)))
        {
            _consecutiveRespawns.Remove($"sup:{session.OrchId}");
            return;
        }

        if (!Try_ClaimRespawnSlot($"sup:{session.OrchId}"))
            return;

        Register_Respawn($"sup:{session.OrchId}", session.OrchId, "supervisor");
        _log.Log_Warning(session.OrchId, "Supervisor session not running — respawning (it resumes from the channels)");
        _launcher.Respawn_Supervisor(session.OrchId);
    }

    void Check_Communicator(Sessions.OrchestrationSession.IOrchestrationSession session)
    {
        if (Is_WithinSpawnGrace(session.CommunicatorSpawnedUtc))
            return;

        if (Is_SessionAlive(_paths.Get_CommunicatorPidFile(session.OrchId)))
        {
            _consecutiveRespawns.Remove($"com:{session.OrchId}");
            return;
        }

        if (!Try_ClaimRespawnSlot($"com:{session.OrchId}"))
            return;

        Register_Respawn($"com:{session.OrchId}", session.OrchId, "communicator");
        _log.Log_Warning(session.OrchId, "Communicator session not running — respawning (it resumes from the channels)");
        _launcher.Respawn_Communicator(session.OrchId);
    }

    void Check_Implementer(string orchId, string memberId, DateTime? spawnedUtc)
    {
        if (Is_WithinSpawnGrace(spawnedUtc))
            return;

        if (Is_SessionAlive(_paths.Get_ImplementerPidFile(orchId, memberId)))
        {
            _consecutiveRespawns.Remove($"imp:{orchId}/{memberId}");
            return;
        }

        if (!Try_ClaimRespawnSlot($"imp:{orchId}/{memberId}"))
            return;

        Register_Respawn($"imp:{orchId}/{memberId}", orchId, memberId);
        _log.Log_Warning(orchId, $"Implementer '{memberId}' session not running — respawning (it resumes from its channel)");
        _launcher.Respawn_Implementer(orchId, memberId);
    }

    /// <summary>Counts respawns since the slot was last seen ALIVE; at the threshold, queues one escalation.</summary>
    void Register_Respawn(string slotKey, string orchId, string sessionLabel)
    {
        _consecutiveRespawns.TryGetValue(slotKey, out var count);
        count++;
        _consecutiveRespawns[slotKey] = count;

        if (count != CRASH_LOOP_THRESHOLD)
            return;

        var alertText = $"⚠️ {sessionLabel} of '{orchId}' is CRASH-LOOPING — {count} respawns without coming alive. Check its terminal and orchestrator.log.jsonl.";
        _pendingCrashLoopAlerts.Add((orchId, alertText));
        _log.Log_Error(orchId, alertText, null);
    }

    bool Try_ClaimRespawnSlot(string slotKey)
    {
        var now = DateTime.UtcNow;

        if (_lastRespawnUtc.TryGetValue(slotKey, out var last) && (now - last).TotalSeconds < RESPAWN_BACKOFF_SECONDS)
            return false;

        _lastRespawnUtc[slotKey] = now;
        return true;
    }

    static bool Is_SessionAlive(string pidFilePath)
    {
        try
        {
            if (!File.Exists(pidFilePath))
                return false;

            var pidText = File.ReadAllText(pidFilePath).Trim();

            if (!int.TryParse(pidText, out var pid))
                return false;

            var process = Process.GetProcessById(pid);

            if (process.HasExited)
                return false;

            // Guard against Windows pid recycling: the pid must still be a PowerShell shell.
            return SHELL_PROCESS_NAMES.Contains(process.ProcessName.ToLowerInvariant());
        }
        catch
        {
            return false;
        }
    }
}
