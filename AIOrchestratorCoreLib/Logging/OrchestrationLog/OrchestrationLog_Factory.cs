using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Logging.OrchestrationLog;

public static class OrchestrationLog_Factory
{
    public static IOrchestrationLog Create(ISupervisionPaths paths)
    {
        return new OrchestrationLogModel(paths);
    }
}
