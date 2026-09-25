using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Termination.SessionProcesses;

namespace AIOrchestratorCoreLib.Bridge.LimitRescue;

public static class LimitRescuer_Factory
{
    public static ILimitRescuer Create(
        ISupervisionPaths paths,
        IOrchestrationSessionStore store,
        IOrchestrationLog log,
        ISessionProcesses processes)
    {
        return new LimitRescuerModel(paths, store, log, processes);
    }
}
