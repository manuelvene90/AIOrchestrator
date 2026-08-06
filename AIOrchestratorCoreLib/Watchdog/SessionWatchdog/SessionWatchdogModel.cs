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
    readonly Dictionary<string, DateTime> _lastRespawnUtc = [];

    public void Check_AndRestart_DeadSessions()
    {
        Check_GeneralSupervisor();

        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null)
                continue;

            Check_OrchestrationSupervisor(session);

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

    void Check_GeneralSupervisor()
    {
        if (Is_SessionAlive(_paths.GeneralPidFile))
            return;

        if (!Try_ClaimRespawnSlot("general"))
            return;

        _log.Log_Warning(ChannelDiscovery.GENERAL_ORCH_ID, "General supervisor not running — spawning it");
        _launcher.Spawn_GeneralSupervisor();
    }

    void Check_OrchestrationSupervisor(Sessions.OrchestrationSession.IOrchestrationSession session)
    {
        if (Is_WithinSpawnGrace(session.SupervisorSpawnedUtc))
            return;

        if (Is_SessionAlive(_paths.Get_SupervisorPidFile(session.OrchId)))
            return;

        if (!Try_ClaimRespawnSlot($"sup:{session.OrchId}"))
            return;

        _log.Log_Warning(session.OrchId, "Supervisor session not running — respawning (it resumes from the channels)");
        _launcher.Respawn_Supervisor(session.OrchId);
    }

    void Check_Implementer(string orchId, string memberId, DateTime? spawnedUtc)
    {
        if (Is_WithinSpawnGrace(spawnedUtc))
            return;

        if (Is_SessionAlive(_paths.Get_ImplementerPidFile(orchId, memberId)))
            return;

        if (!Try_ClaimRespawnSlot($"imp:{orchId}/{memberId}"))
            return;

        _log.Log_Warning(orchId, $"Implementer '{memberId}' session not running — respawning (it resumes from its channel)");
        _launcher.Respawn_Implementer(orchId, memberId);
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
