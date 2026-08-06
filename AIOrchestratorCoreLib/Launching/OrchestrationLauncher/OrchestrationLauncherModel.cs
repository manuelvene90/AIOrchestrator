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
        _log.Log_Info(orchId, $"Orchestration created for repo '{repoName}' ({repoPath})");

        Respawn_Supervisor(orchId);

        // Every orchestration starts with one implementer ready (owner directive): the supervisor
        // briefs imp-1 when the first task arrives instead of requesting a spawn first.
        return Add_Implementer(orchId);
    }

    public IOrchestrationSession Add_Implementer(string orchId)
    {
        var session = _store.Add_Implementer(orchId);
        var newMember = session.Members[session.Members.Count - 1];

        Respawn_Implementer(orchId, newMember.MemberId);
        return session;
    }

    public void Respawn_Supervisor(string orchId)
    {
        var session = _store.Get_Session(orchId);

        var command = SpawnCommand_Builder.Build_ForSupervisor(
            orchId,
            session.RepoPath,
            session.SupervisorModelOverride ?? _configProvider.Get_Current().SupervisorModel,
            _paths.Get_SupervisorPidFile(orchId));

        var pid = _spawner.Spawn(command);

        _store.Set_SupervisorPid(orchId, pid);
        _log.Log_Info(orchId, "Supervisor session spawned");
    }

    public void Respawn_Implementer(string orchId, string memberId)
    {
        var session = _store.Get_Session(orchId);

        var command = SpawnCommand_Builder.Build_ForImplementer(
            orchId,
            memberId,
            session.RepoPath,
            session.ImplementerModelOverride ?? _configProvider.Get_Current().ImplementerModel,
            _paths.Get_ImplementerPidFile(orchId, memberId));

        var pid = _spawner.Spawn(command);

        _store.Set_MemberPid(orchId, memberId, pid);
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
}
