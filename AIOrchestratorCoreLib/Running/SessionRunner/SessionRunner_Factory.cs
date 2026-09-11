using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.Spawning.SessionSpawner;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.SessionRunner;

public static class SessionRunner_Factory
{
    public static ISessionRunner Create_Terminal(ISessionSpawner spawner)
    {
        return new TerminalRunnerModel(spawner);
    }

    /// <summary>
    /// The store is here because REGISTERING a supervisor has to know its members: its cursors are
    /// baselined at registration, one per channel it is woken by, and the roster is what says which
    /// those are.
    /// </summary>
    public static ISessionRunner Create_Print(ISupervisionPaths paths, IOrchestrationSessionStore store, IOrchestrationLog log)
    {
        return new BridgeDrivenRunnerModel(SessionRunners.Print, paths, store, log);
    }

    public static ISessionRunner Create_Stream(ISupervisionPaths paths, IOrchestrationSessionStore store, IOrchestrationLog log)
    {
        return new BridgeDrivenRunnerModel(SessionRunners.Stream, paths, store, log);
    }
}
