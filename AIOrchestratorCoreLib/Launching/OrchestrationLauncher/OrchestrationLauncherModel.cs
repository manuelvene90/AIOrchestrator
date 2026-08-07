using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.Spawning;
using AIOrchestratorCoreLib.Spawning.SessionSpawner;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Launching.OrchestrationLauncher;

internal sealed class OrchestrationLauncherModel(
    ISupervisionPaths paths,
    IOrchestratorConfigProvider configProvider,
    IOrchestrationSessionStore store,
    ISessionSpawner spawner,
    IOrchestrationLog log) : IOrchestrationLauncher
{
    /// <summary>The shell writes its pid file within ~1 s of starting; 40 × 500 ms is generous.</summary>
    const int PID_FILE_WAIT_ATTEMPTS = 40;
    const int PID_FILE_POLL_MILLISECONDS = 500;

    readonly ISupervisionPaths _paths = paths;
    readonly IOrchestratorConfigProvider _configProvider = configProvider;
    readonly IOrchestrationSessionStore _store = store;
    readonly ISessionSpawner _spawner = spawner;
    readonly IOrchestrationLog _log = log;

    public IOrchestrationSession Start_Orchestration(string repoName, string repoPath)
    {
        if (!Directory.Exists(repoPath))
            throw new Exception($"Repo path '{repoPath}' for '{repoName}' does not exist — cannot start an orchestration");

        var orchId = OrchId_Allocator.Allocate_NextOrchId(_paths, repoName);

        _store.Create_Orchestration(orchId, repoName, repoPath);

        // The ledger exists from minute one: the card shows a bar and /progress answers, instead
        // of both looking broken until the supervisor gets around to writing the file.
        Planning.PlanSeed_Writer.Ensure_Exists(_paths, orchId, repoName);

        _log.Log_Info(orchId, $"Orchestration created for repo '{repoName}' ({repoPath})");

        Respawn_Supervisor(orchId);

        // The communicator is the always-responsive press-secretary voice: while the supervisor
        // is mid-turn (unreachable), it narrates what the supervisor is doing to the owner.
        Respawn_Communicator(orchId);

        // Every orchestration starts with one implementer ready (owner directive): the supervisor
        // briefs imp-1 when the first task arrives instead of requesting a spawn first.
        return Add_Implementer(orchId);
    }

    public IOrchestrationSession Add_Implementer(string orchId)
    {
        var session = _store.Add_Implementer(orchId);
        var newMember = session.Members[session.Members.Count - 1];

        Respawn_Implementer(orchId, newMember.MemberId);

        // Respawn stamped the new member's spawn state — return the fresh session, not the
        // pre-spawn snapshot.
        return _store.Get_Session(orchId);
    }

    public void Respawn_Supervisor(string orchId)
    {
        var session = _store.Get_Session(orchId);
        var pidFile = _paths.Get_SupervisorPidFile(orchId);

        var command = SpawnCommand_Builder.Build_ForSupervisor(
            orchId,
            session.RepoPath,
            session.SupervisorModelOverride ?? _configProvider.Get_Current().SupervisorModel,
            pidFile);

        // Stamp the spawn (watchdog grace) BEFORE deleting the stale pid file, so no tick can see
        // "no pid file + no grace" and double-spawn. The stale file must go: the sync below must
        // never read the PREVIOUS session's pid as the new one.
        _store.Set_SupervisorPid(orchId, null);
        Delete_StalePidFile_BestEffort(pidFile);

        _spawner.Spawn(command);
        Sync_TruePid_FromPidFile(pidFile, orchId, "supervisor", truePid => Store_SupervisorTruePid_IfStillOpen(orchId, truePid));

        _log.Log_Info(orchId, "Supervisor session spawned");
    }

    public void Respawn_Communicator(string orchId)
    {
        var session = _store.Get_Session(orchId);
        var pidFile = _paths.Get_CommunicatorPidFile(orchId);

        var command = SpawnCommand_Builder.Build_ForCommunicator(
            orchId,
            session.RepoPath,
            _configProvider.Get_Current().CommunicatorModel,
            pidFile);

        // No pid lands in session.json for the communicator — the pid file is the liveness
        // source and nothing else needs it. Only the spawn-grace stamp is stored.
        _store.Stamp_CommunicatorSpawned(orchId);
        Delete_StalePidFile_BestEffort(pidFile);

        _spawner.Spawn(command);
        _log.Log_Info(orchId, "Communicator session spawned");
    }

    public void Respawn_Implementer(string orchId, string memberId)
    {
        var session = _store.Get_Session(orchId);
        var pidFile = _paths.Get_ImplementerPidFile(orchId, memberId);

        var command = SpawnCommand_Builder.Build_ForImplementer(
            orchId,
            memberId,
            session.RepoPath,
            session.ImplementerModelOverride ?? _configProvider.Get_Current().ImplementerModel,
            pidFile);

        _store.Set_MemberPid(orchId, memberId, null);
        Delete_StalePidFile_BestEffort(pidFile);

        _spawner.Spawn(command);
        Sync_TruePid_FromPidFile(pidFile, orchId, memberId, truePid => Store_MemberTruePid_IfStillOpen(orchId, memberId, truePid));

        _log.Log_Info(orchId, $"Implementer '{memberId}' session spawned");
    }

    public void Spawn_GeneralSupervisor()
    {
        GeneralChannel_Initializer.Ensure_Exists(_paths);

        // The general folder is the general supervisor's PERMANENT working directory: its
        // CLAUDE.md (persistent, machine-portable knowledge) auto-loads there, and --continue
        // resumes unambiguously because only general sessions ever run in it.
        var command = SpawnCommand_Builder.Build_ForGeneralSupervisor(
            _paths.GeneralFolder, _configProvider.Get_Current().GeneralSupervisorModel, _paths.GeneralPidFile);

        _spawner.Spawn(command);

        _log.Log_Info(ChannelDiscovery.GENERAL_ORCH_ID, "General supervisor session spawned (resume-if-possible)");
    }

    /// <summary>
    /// Process.Start's pid is wt.exe's short-lived DELEGATOR (it hands off to the terminal service
    /// and exits) — recording it in session.json made a supervisor conclude LIVE implementers were
    /// dead and retire them mid-work. The TRUE session-host pid is the one the spawned shell writes
    /// into its pid file; sync THAT into session.json once it appears. Until then the stored pid is
    /// null, meaning "spawning" — never "dead".
    /// </summary>
    void Sync_TruePid_FromPidFile(string pidFilePath, string orchId, string sessionLabel, Action<int> storeTruePid)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                for (var attempt = 0; attempt < PID_FILE_WAIT_ATTEMPTS; attempt++)
                {
                    await Task.Delay(PID_FILE_POLL_MILLISECONDS);

                    if (!File.Exists(pidFilePath))
                        continue;

                    if (int.TryParse(File.ReadAllText(pidFilePath).Trim(), out var truePid))
                    {
                        storeTruePid(truePid);
                        return;
                    }
                }

                _log.Log_Warning(orchId, $"{sessionLabel}: pid file '{pidFilePath}' never appeared — the session may have failed to start (the watchdog will respawn it)");
            }
            catch (Exception ex)
            {
                _log.Log_Warning(orchId, $"{sessionLabel}: true-pid sync from '{pidFilePath}' failed: {ex.Message}");
            }
        });
    }

    void Store_SupervisorTruePid_IfStillOpen(string orchId, int truePid)
    {
        var current = _store.Get_Session_OrNull(orchId);

        if (current == null || current.ClosedUtc != null)
            return;

        _store.Set_SupervisorPid(orchId, truePid);
    }

    void Store_MemberTruePid_IfStillOpen(string orchId, string memberId, int truePid)
    {
        var current = _store.Get_Session_OrNull(orchId);

        if (current == null || current.ClosedUtc != null)
            return;

        // A member closed during the sync window must stay closed — writing its pid would reopen it.
        foreach (var member in current.Members)
        {
            if (member.MemberId == memberId && member.ClosedUtc == null)
                _store.Set_MemberPid(orchId, memberId, truePid);
        }
    }

    static void Delete_StalePidFile_BestEffort(string pidFilePath)
    {
        try
        {
            if (File.Exists(pidFilePath))
                File.Delete(pidFilePath);
        }
        catch
        {
            // A locked pid file is tolerable — the sync may then read a stale pid for one poll,
            // but the shell overwrites it within a second of starting.
        }
    }
}
