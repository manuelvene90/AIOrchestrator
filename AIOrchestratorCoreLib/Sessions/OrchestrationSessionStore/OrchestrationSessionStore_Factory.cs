using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;

public static class OrchestrationSessionStore_Factory
{
    public static IOrchestrationSessionStore Create(ISupervisionPaths paths)
    {
        return new OrchestrationSessionStoreModel(paths);
    }
}
